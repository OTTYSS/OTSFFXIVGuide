using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

// ───────────── Модели вики ─────────────

public sealed record WikiMobRow(ulong Id, string Name, string Level, uint Zone);

public sealed record WikiMob(ulong Id, string Name, string Level, uint Zone, float X, float Y, bool HasCoords, bool Quest, List<uint> Drops);

public sealed record WikiFight(string Type, List<uint> Items);

public sealed record WikiCoffer(List<uint> Items, string Coords);

public sealed record WikiInstance(uint Id, string Name, List<WikiFight> Fights, List<WikiCoffer> Coffers, List<uint> Rewards);

/// <summary>Ссылка на NPC с местом (для флага на карте).</summary>
public sealed record WikiNpc(uint Id, string Name, uint Zone, float X, float Y, bool HasCoords);

public sealed record WikiAmount(uint Item, int Amount);

public sealed record WikiTrade(string Shop, List<WikiNpc> Npcs, List<WikiAmount> Get, List<WikiAmount> Pay);

public sealed record WikiCraft(uint Job, int Level, int Stars, List<WikiAmount> Ingredients, int Yield);

public sealed record WikiNode(uint Id, string Name, int Level, uint Zone, int Type, string Limited);

public sealed record WikiRef(string Id, string Name, string Extra);

public sealed record WikiNpcRow(uint Id, string Name, uint Zone, int Quests, string Title);

public sealed record WikiNpcShop(string Name, List<uint> Items);

public sealed record WikiNpcFull(uint Id, string Name, string Title, uint Zone, float X, float Y, bool HasCoords, List<WikiNpcShop> Shops);

public sealed record WikiFate(uint Id, string Name, int Level, string Type, uint Zone, float X, float Y);

public sealed record WikiFateFull(uint Id, string Description, int MaxLevel, List<uint> Items);

public sealed record WikiQuestFull(uint Id, string Name, List<WikiAmount> Rewards, List<WikiAmount> Optional, List<uint> UsedItems, List<WikiRef> Next, List<WikiRef> Previous, int Xp, WikiNpc? Issuer);

/// <summary>Как поймать рыбу на конкретном месте.</summary>
public sealed record WikiFishSpot(string Spot, uint Zone, float X, float Y, bool HasCoords, string Hookset, string Tug, List<List<uint>> Baits, string Time, string Weather, string Predators,
    int StartHour = 0, int EndHour = 0, List<string>? Weathers = null, List<string>? Previous = null);

/// <summary>Всё, что известно о предмете: откуда взять и куда потратить.</summary>
public sealed class WikiItem
{
    public uint Id;
    public List<WikiMobRow> Drops = new();
    public List<WikiNpc> Vendors = new();
    public long VendorPrice;
    public List<WikiTrade> TradeShops = new();
    public List<WikiTrade> TradeCurrency = new();
    public List<WikiCraft> Crafts = new();
    public List<WikiNode> Nodes = new();
    public List<WikiRef> Instances = new();
    public List<uint> DesynthFrom = new();
    public List<uint> ReducedFrom = new();
    public List<uint> Treasure = new();
    public List<WikiRef> Quests = new();
    public List<WikiRef> UsedInQuests = new();
    public List<WikiRef> Leves = new();
    public List<WikiRef> FishingSpots = new();
    public List<WikiAmount> IngredientOf = new();
    public int Ventures;
    public bool Supply;
    public string FishGuide = "";
    public List<WikiFishSpot> FishSpots = new();

    public bool HasAnySource =>
        Drops.Count + Vendors.Count + TradeShops.Count + Crafts.Count + Nodes.Count + Instances.Count +
        DesynthFrom.Count + ReducedFrom.Count + Treasure.Count + Quests.Count + FishingSpots.Count + Ventures > 0;
}

/// <summary>
/// Онлайн-вики по данным Garland Tools: мобы, лут данжей, источники предметов.
/// Документы кешируются на диск (неделю) — повторно открывать карточки можно без сети.
/// Только чтение: ничего не отправляет в игру.
/// </summary>
public sealed class WikiService : IDisposable
{
    public static WikiService Instance { get; private set; } = null!;

    private const string Base = "https://www.garlandtools.org/db/doc/";
    private static readonly TimeSpan DiskTtl = TimeSpan.FromDays(7);

    private readonly HttpClient http = Net.Create(TimeSpan.FromSeconds(30));
    private readonly string cacheDir;
    private readonly ConcurrentDictionary<string, object?> docs = new();
    private readonly ConcurrentDictionary<string, byte> inFlight = new();
    private readonly ConcurrentDictionary<string, string> errors = new();

    public WikiService()
    {
        Instance = this;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FFXIVGuide/1.8");
        cacheDir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "wiki");
        try { Directory.CreateDirectory(cacheDir); } catch { /* без кеша тоже работает */ }
    }

    // ───────────── Публичные запросы ─────────────

    // Офлайн-база в приоритете; Garland дополняет её (дроп мобов, лут по боссам), если разрешено в настройках.
    private static OfflineIndex? Offline => OfflineDb.Instance?.Index;
    private static bool Online => Offline is null || Plugin.Config.WikiUseOnline;

    private T? Online_<T>(string path, Func<JsonElement, T> parse) where T : class => Online ? Get(path, parse) : null;

    public WikiMob? Mob(ulong id)
    {
        var on = Online_($"mob/en/2/{id}", ParseMob);
        if (on is not null || Offline is null) return on;
        // Пока Garland грузится или недоступен — показываем то, что есть в базе
        return Offline.Mob(id);
    }

    public WikiInstance? Duty(uint id)
    {
        var on = Online_($"instance/en/2/{id}", ParseInstance);
        if (on is not null && (on.Fights.Any(f => f.Items.Count > 0) || on.Rewards.Count > 0)) return on;
        return Offline?.Duty(id) ?? on;
    }

    private readonly ConcurrentDictionary<uint, WikiItem> merged = new();

    public WikiItem? Item(uint id)
    {
        var on = Online_($"item/en/3/{id}", ParseItem);
        var off = Offline?.Item(id);
        if (off is null) return on;
        if (on is null) return off;
        if (merged.TryGetValue(id, out var m)) return m;
        m = Merge(off, on);
        merged[id] = m;
        return m;
    }

    /// <summary>Склеивает карточку из базы и Garland: берём более полный список каждого источника.</summary>
    private static WikiItem Merge(WikiItem a, WikiItem b)
    {
        List<T> Pick<T>(List<T> x, List<T> y) => y.Count > x.Count ? y : x;
        return new WikiItem
        {
            Id = a.Id,
            Drops = a.Drops.Concat(b.Drops).GroupBy(d => d.Name + "|" + d.Zone).Select(g => g.First()).ToList(),
            Vendors = Pick(a.Vendors, b.Vendors),
            VendorPrice = a.VendorPrice > 0 ? a.VendorPrice : b.VendorPrice,
            TradeShops = Pick(a.TradeShops, b.TradeShops),
            TradeCurrency = Pick(a.TradeCurrency, b.TradeCurrency),
            Crafts = Pick(a.Crafts, b.Crafts),
            Nodes = Pick(b.Nodes, a.Nodes),
            Instances = Pick(a.Instances, b.Instances),
            DesynthFrom = Pick(a.DesynthFrom, b.DesynthFrom),
            ReducedFrom = Pick(a.ReducedFrom, b.ReducedFrom),
            Treasure = Pick(a.Treasure, b.Treasure),
            Quests = Pick(a.Quests, b.Quests),
            UsedInQuests = b.UsedInQuests,
            Leves = b.Leves,
            FishingSpots = b.FishingSpots,
            IngredientOf = Pick(a.IngredientOf, b.IngredientOf),
            Ventures = Math.Max(a.Ventures, b.Ventures),
            Supply = b.Supply,
            FishGuide = b.FishGuide.Length > 0 ? b.FishGuide : a.FishGuide,
            FishSpots = a.FishSpots.Count > 0 ? a.FishSpots : b.FishSpots,
        };
    }

    /// <summary>Сбросить карточки в памяти (после загрузки или удаления базы).</summary>
    public void ResetMemory()
    {
        docs.Clear();
        errors.Clear();
        merged.Clear();
    }
    public List<WikiMobRow>? MobIndex() => Get("browse/en/2/mob", ParseMobIndex);
    public List<WikiNpcRow>? NpcIndex() => Get("browse/en/2/npc", ParseNpcIndex);
    public List<WikiFate>? FateIndex() => Get("browse/en/2/fate", ParseFateIndex);
    public WikiNpcFull? Npc(uint id)
    {
        var off = Offline?.Npc(id);
        if (off is not null && (off.Shops.Count > 0 || !Online)) return off;
        return Online_($"npc/en/2/{id}", ParseNpc) ?? off;
    }

    public WikiQuestFull? Quest(uint id)
    {
        var on = Online_($"quest/en/2/{id}", ParseQuest);
        return on ?? Offline?.Quest(id);
    }
    public WikiFateFull? Fate(uint id) => Get($"fate/en/2/{id}", ParseFate);

    public bool IsLoading(string kind, object id) => inFlight.ContainsKey(Key(kind, id));
    public string? Error(string kind, object id) => errors.GetValueOrDefault(Key(kind, id));

    private static string Key(string kind, object id) => kind switch
    {
        "mob" => $"mob/en/2/{id}",
        "instance" => $"instance/en/2/{id}",
        "item" => $"item/en/3/{id}",
        "npc" => $"npc/en/2/{id}",
        "quest" => $"quest/en/2/{id}",
        "fate" => $"fate/en/2/{id}",
        "browse" => $"browse/en/2/{id}",
        _ => "browse/en/2/mob",
    };

    /// <summary>Сбросить ошибку и попробовать ещё раз.</summary>
    public void Retry(string kind, object id) => errors.TryRemove(Key(kind, id), out _);

    public void ClearCache()
    {
        docs.Clear();
        errors.Clear();
        try
        {
            foreach (var f in Directory.EnumerateFiles(cacheDir, "*.json")) File.Delete(f);
        }
        catch (Exception e) { Plugin.Log.Warning(e, "Вики: не удалось очистить кеш"); }
    }

    private long cacheSize;
    private DateTime cacheSizeAt;

    /// <summary>Размер кеша на диске (пересчитывается не чаще раза в 5 секунд).</summary>
    public long CacheSizeBytes()
    {
        if (DateTime.UtcNow - cacheSizeAt < TimeSpan.FromSeconds(5)) return cacheSize;
        cacheSizeAt = DateTime.UtcNow;
        try { cacheSize = Directory.EnumerateFiles(cacheDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); }
        catch { cacheSize = 0; }
        return cacheSize;
    }

    // ───────────── Загрузка ─────────────

    private T? Get<T>(string path, Func<JsonElement, T> parse) where T : class
    {
        if (docs.TryGetValue(path, out var o)) return o as T;
        if (errors.ContainsKey(path) || !inFlight.TryAdd(path, 0)) return null;
        _ = Task.Run(() => Load(path, parse));
        return null;
    }

    private async Task Load<T>(string path, Func<JsonElement, T> parse) where T : class
    {
        var file = Path.Combine(cacheDir, path.Replace('/', '_') + ".json");
        try
        {
            string? json = null;
            if (path.StartsWith("browse/") && OfflineDb.Instance is { } db)
            {
                var dbFile = Path.Combine(db.Dir, "garland-" + path.Split('/').Last() + ".json");
                if (File.Exists(dbFile)) file = dbFile;
            }
            var fresh = File.Exists(file) && (file.Contains(Path.DirectorySeparatorChar + "db" + Path.DirectorySeparatorChar) || DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < DiskTtl);
            if (fresh) json = await File.ReadAllTextAsync(file).ConfigureAwait(false);

            if (json is null)
            {
                try
                {
                    using var resp = await http.GetAsync(Base + path + ".json").ConfigureAwait(false);
                    if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        docs[path] = null;
                        errors[path] = "Нет данных на Garland Tools";
                        return;
                    }
                    resp.EnsureSuccessStatusCode();
                    json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    try { await File.WriteAllTextAsync(file, json).ConfigureAwait(false); } catch { /* кеш не обязателен */ }
                }
                catch when (File.Exists(file))
                {
                    // Сети нет — берём устаревшую копию.
                    json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                }
            }

            using var doc = JsonDocument.Parse(json);
            docs[path] = parse(doc.RootElement);
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, $"Вики: не удалось загрузить {path}");
            errors[path] = "Не удалось загрузить (нет сети или сайт недоступен)";
        }
        finally
        {
            inFlight.TryRemove(path, out _);
        }
    }

    // ───────────── Разбор JSON ─────────────

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? "",
                JsonValueKind.Number => v.GetRawText(),
                _ => "",
            }
            : "";

    private static double Num(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number => v.GetDouble(),
        JsonValueKind.String when double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
        _ => 0,
    };

    private static double Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? Num(v) : 0;

    private static IEnumerable<JsonElement> Arr(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private static List<uint> Ids(JsonElement e, string name) =>
        Arr(e, name).Select(x => (uint)Num(x)).Where(x => x != 0).Distinct().ToList();

    /// <summary>Краткие описания связанных объектов (имена NPC, мобов, данжей).</summary>
    private static Dictionary<(string, string), JsonElement> Partials(JsonElement root)
    {
        var map = new Dictionary<(string, string), JsonElement>();
        foreach (var p in Arr(root, "partials"))
            map[(Str(p, "type"), Str(p, "id"))] = p.TryGetProperty("obj", out var o) ? o.Clone() : default;
        return map;
    }

    private static WikiMobRow MobRow(JsonElement o) =>
        new((ulong)Num(o, "i"), Str(o, "n"), Str(o, "l"), (uint)Num(o, "z"));

    private static List<WikiMobRow> ParseMobIndex(JsonElement root) =>
        Arr(root, "browse").Select(MobRow).Where(m => m.Name.Length > 0).ToList();

    private static List<WikiNpcRow> ParseNpcIndex(JsonElement root) =>
        Arr(root, "browse")
            .Select(o => new WikiNpcRow((uint)Num(o, "i"), Str(o, "n"), (uint)Num(o, "l"), (int)Num(o, "q"), Str(o, "t")))
            .Where(n => n.Name.Length > 0 && !n.Name.StartsWith("Nameless"))
            .ToList();

    private static List<WikiFate> ParseFateIndex(JsonElement root) =>
        Arr(root, "browse").Select(o =>
        {
            var c = Arr(o, "c").Select(Num).ToList();
            return new WikiFate((uint)Num(o, "i"), Str(o, "n"), (int)Num(o, "l"), Str(o, "t"), (uint)Num(o, "z"),
                c.Count > 0 ? (float)c[0] : 0, c.Count > 1 ? (float)c[1] : 0);
        }).Where(f => f.Name.Length > 0).ToList();

    private static WikiNpcFull ParseNpc(JsonElement root)
    {
        var n = root.GetProperty("npc");
        var c = Arr(n, "coords").Select(Num).ToList();
        var shops = Arr(n, "shops").Select(s => new WikiNpcShop(Str(s, "name"),
            Arr(s, "entries").Select(e => e.ValueKind == JsonValueKind.Object
                ? (uint)Num(e.TryGetProperty("item", out var it) && it.ValueKind == JsonValueKind.Array && it.GetArrayLength() > 0 ? it[0] : e, "id")
                : (uint)Num(e)).Where(x => x != 0).Distinct().ToList())).Where(s => s.Items.Count > 0).ToList();
        return new WikiNpcFull((uint)Num(n, "id"), Str(n, "name"), Str(n, "title"), (uint)Num(n, "zoneid"),
            c.Count > 0 ? (float)c[0] : 0, c.Count > 1 ? (float)c[1] : 0, c.Count > 1, shops);
    }

    private static WikiFateFull ParseFate(JsonElement root)
    {
        var f = root.GetProperty("fate");
        return new WikiFateFull((uint)Num(f, "id"), Str(f, "description"), (int)Num(f, "maxlvl"), Ids(f, "items"));
    }

    private static WikiQuestFull ParseQuest(JsonElement root)
    {
        var q = root.GetProperty("quest");
        var partials = Partials(root);
        var rewards = new List<WikiAmount>();
        var optional = new List<WikiAmount>();
        var xp = 0;
        if (q.TryGetProperty("reward", out var r) && r.ValueKind == JsonValueKind.Object)
        {
            xp = (int)Num(r, "xp");
            foreach (var it in Arr(r, "items"))
            {
                var a = new WikiAmount((uint)Num(it, "id"), Math.Max(1, (int)Num(it, "num")));
                if (a.Item == 0) continue;
                if (Num(it, "one") > 0) optional.Add(a); else rewards.Add(a);
            }
            if (r.TryGetProperty("gil", out var g)) rewards.Insert(0, new WikiAmount(1, (int)Num(g)));
        }
        var prev = new List<WikiRef>();
        if (q.TryGetProperty("reqs", out var reqs) && reqs.ValueKind == JsonValueKind.Object)
            prev = Refs(reqs, "quests", "quest", partials);
        WikiNpc? issuer = null;
        if (Num(q, "issuer") > 0) issuer = Npc(partials, (uint)Num(q, "issuer"));
        return new WikiQuestFull((uint)Num(q, "id"), Str(q, "name"), rewards, optional, Ids(q, "usedItems"),
            Refs(q, "next", "quest", partials), prev, xp, issuer);
    }

    private static WikiMob ParseMob(JsonElement root)
    {
        var m = root.GetProperty("mob");
        var coords = Arr(m, "coords").Select(Num).ToList();
        return new WikiMob(
            (ulong)Num(m, "id"), Str(m, "name"), Str(m, "lvl"), (uint)Num(m, "zoneid"),
            coords.Count > 0 ? (float)coords[0] : 0, coords.Count > 1 ? (float)coords[1] : 0, coords.Count > 1,
            Num(m, "quest") > 0, Ids(m, "drops"));
    }

    private static WikiInstance ParseInstance(JsonElement root)
    {
        var i = root.GetProperty("instance");
        var fights = Arr(i, "fights").Select(f =>
            new WikiFight(Str(f, "type"), f.TryGetProperty("coffer", out var c) ? Ids(c, "items") : new List<uint>())).ToList();
        var coffers = Arr(i, "coffers").Select(c =>
            new WikiCoffer(Ids(c, "items"), string.Join(", ", Arr(c, "coords").Select(x => Num(x).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))))).ToList();
        return new WikiInstance((uint)Num(i, "id"), Str(i, "name"), fights, coffers, Ids(i, "rewards"));
    }

    private static WikiNpc Npc(Dictionary<(string, string), JsonElement> partials, uint id)
    {
        if (!partials.TryGetValue(("npc", id.ToString()), out var o) || o.ValueKind != JsonValueKind.Object)
            return new WikiNpc(id, $"NPC #{id}", 0, 0, 0, false);
        var c = Arr(o, "c").Select(Num).ToList();
        return new WikiNpc(id, Str(o, "n"), (uint)Num(o, "a"), c.Count > 0 ? (float)c[0] : 0, c.Count > 1 ? (float)c[1] : 0, c.Count > 1);
    }

    private static List<WikiAmount> Amounts(JsonElement e, string name) =>
        Arr(e, name).Select(x => new WikiAmount((uint)Num(x, "id"), Math.Max(1, (int)Num(x, "amount")))).Where(a => a.Item != 0).ToList();

    private static List<WikiTrade> Trades(JsonElement item, string name, Dictionary<(string, string), JsonElement> partials)
    {
        var list = new List<WikiTrade>();
        foreach (var shop in Arr(item, name))
        {
            var npcs = Ids(shop, "npcs").Select(id => Npc(partials, id)).ToList();
            foreach (var l in Arr(shop, "listings"))
                list.Add(new WikiTrade(Str(shop, "shop"), npcs, Amounts(l, "item"), Amounts(l, "currency")));
        }
        return list;
    }

    private static List<WikiRef> Refs(JsonElement item, string name, string type, Dictionary<(string, string), JsonElement> partials, Func<JsonElement, string>? extra = null)
    {
        var list = new List<WikiRef>();
        foreach (var id in Arr(item, name).Select(x => x.ValueKind == JsonValueKind.Object ? Str(x, "id") : Num(x).ToString("0")).Distinct())
        {
            partials.TryGetValue((type, id), out var o);
            var n = o.ValueKind == JsonValueKind.Object ? Str(o, "n") : $"#{id}";
            list.Add(new WikiRef(id, n, o.ValueKind == JsonValueKind.Object && extra is not null ? extra(o) : ""));
        }
        return list;
    }

    private static WikiItem ParseItem(JsonElement root)
    {
        var i = root.GetProperty("item");
        var partials = Partials(root);
        var w = new WikiItem { Id = (uint)Num(i, "id") };

        foreach (var id in Arr(i, "drops").Select(x => Num(x).ToString("0")).Distinct())
            w.Drops.Add(partials.TryGetValue(("mob", id), out var o) && o.ValueKind == JsonValueKind.Object
                ? MobRow(o)
                : new WikiMobRow(ulong.Parse(id), $"Моб #{id}", "", 0));

        w.Vendors = Ids(i, "vendors").Select(id => Npc(partials, id)).ToList();
        w.VendorPrice = (long)Num(i, "price");
        w.TradeShops = Trades(i, "tradeShops", partials);
        w.TradeCurrency = Trades(i, "tradeCurrency", partials);

        foreach (var c in Arr(i, "craft"))
            w.Crafts.Add(new WikiCraft((uint)Num(c, "job"), (int)Num(c, "lvl"), (int)Num(c, "stars"), Amounts(c, "ingredients"), Math.Max(1, (int)Num(c, "yield"))));

        foreach (var id in Ids(i, "nodes"))
        {
            partials.TryGetValue(("node", id.ToString()), out var o);
            w.Nodes.Add(o.ValueKind == JsonValueKind.Object
                ? new WikiNode(id, Str(o, "n"), (int)Num(o, "l"), (uint)Num(o, "z"), (int)Num(o, "t"), Str(o, "lt"))
                : new WikiNode(id, $"Точка #{id}", 0, 0, 0, ""));
        }

        w.Instances = Refs(i, "instances", "instance", partials, o => Str(o, "t"));
        w.DesynthFrom = Ids(i, "desynthedFrom");
        w.ReducedFrom = Ids(i, "reducedFrom");
        w.Treasure = Ids(i, "treasure");
        w.Quests = Refs(i, "quests", "quest", partials, o => Str(o, "l"));
        w.UsedInQuests = Refs(i, "usedInQuest", "quest", partials, o => Str(o, "l"));
        w.Leves = Refs(i, "leves", "leve", partials);
        w.FishingSpots = Refs(i, "fishingSpots", "fishing", partials);
        w.Ventures = Arr(i, "ventures").Count();

        if (i.TryGetProperty("fish", out var fish) && fish.ValueKind == JsonValueKind.Object)
        {
            w.FishGuide = Str(fish, "guide");
            foreach (var sp in Arr(fish, "spots"))
            {
                var spotId = Str(sp, "spot");
                partials.TryGetValue(("fishing", spotId), out var so);
                var hasSpot = so.ValueKind == JsonValueKind.Object;
                var baits = Arr(sp, "baits").Select(chain => chain.ValueKind == JsonValueKind.Array
                    ? chain.EnumerateArray().Select(x => (uint)Num(x)).ToList()
                    : new List<uint> { (uint)Num(chain) }).Where(c => c.Count > 0).ToList();
                if (baits.Count == 0 && Num(sp, "bait") > 0) baits.Add(new List<uint> { (uint)Num(sp, "bait") });
                var time = "";
                int sh = 0, eh = 0;
                if (sp.TryGetProperty("during", out var d) && d.ValueKind == JsonValueKind.Object)
                {
                    sh = (int)Num(d, "start"); eh = (int)Num(d, "end");
                    time = $"{sh:00}:00–{eh:00}:00 ET";
                }
                var wl = Arr(sp, "weather").Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.GetRawText()).Where(x => x.Length > 0).ToList();
                var tl = Arr(sp, "transition").Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.GetRawText()).Where(x => x.Length > 0).ToList();
                var weather = string.Join(", ", wl);
                var trans = string.Join(", ", tl);
                if (trans.Length > 0) weather = $"{trans} → {weather}";
                var preds = string.Join(", ", Arr(sp, "predator").Select(p => $"{Num(p, "amount")}× #{Num(p, "id")}"));
                w.FishSpots.Add(new WikiFishSpot(
                    hasSpot ? Str(so, "n") : $"Место #{spotId}",
                    hasSpot ? (uint)Num(so, "z") : 0,
                    hasSpot ? (float)Num(so, "x") : 0, hasSpot ? (float)Num(so, "y") : 0, hasSpot && Num(so, "x") > 0,
                    Str(sp, "hookset"), Str(sp, "tug"), baits, time, weather, preds, sh, eh, wl, tl));
            }
            // Если подробностей нет — хотя бы список мест
            if (w.FishSpots.Count == 0)
                foreach (var f in w.FishingSpots)
                    if (partials.TryGetValue(("fishing", f.Id), out var so) && so.ValueKind == JsonValueKind.Object)
                        w.FishSpots.Add(new WikiFishSpot(Str(so, "n"), (uint)Num(so, "z"), (float)Num(so, "x"), (float)Num(so, "y"), Num(so, "x") > 0, "", "", new(), "", "", ""));
        }
        w.Supply = i.TryGetProperty("supply", out _);

        if (i.TryGetProperty("ingredient_of", out var ing) && ing.ValueKind == JsonValueKind.Object)
            foreach (var p in ing.EnumerateObject())
                if (uint.TryParse(p.Name, out var id))
                    w.IngredientOf.Add(new WikiAmount(id, Math.Max(1, (int)Num(p.Value))));
        return w;
    }

    // ───────────── Место на карте ─────────────

    private static Dictionary<uint, (uint Territory, uint Map)>? placeToTerritory;

    /// <summary>Название зоны по id из Garland (это PlaceName игры).</summary>
    public static string ZoneName(uint placeNameId) =>
        placeNameId == 0 ? "" : NameTranslations.Show(Plugin.Data.GetExcelSheet<PlaceName>().GetRowOrDefault(placeNameId)?.Name.ExtractText() ?? "");

    /// <summary>Поставить флаг по координатам карты (как в игре: X 12.3, Y 24.5).</summary>
    public static bool Flag(uint placeNameId, float x, float y)
    {
        if (placeToTerritory is null)
        {
            var map = new Dictionary<uint, (uint, uint)>();
            foreach (var t in Plugin.Data.GetExcelSheet<TerritoryType>())
            {
                if (t.Map.RowId == 0 || t.PlaceName.RowId == 0) continue;
                // Открытый мир (1) важнее инстансов с тем же названием
                if (!map.ContainsKey(t.PlaceName.RowId) || t.TerritoryIntendedUse.RowId == 1)
                    map[t.PlaceName.RowId] = (t.RowId, t.Map.RowId);
            }
            placeToTerritory = map;
        }
        if (!placeToTerritory.TryGetValue(placeNameId, out var tm)) return false;
        var link = new Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload(tm.Territory, tm.Map, x, y, 0.05f);
        return Plugin.GameGui.OpenMapWithMapLink(link);
    }

    public static bool CanFlag(uint placeNameId) => placeNameId != 0;

    public void Dispose() => http.Dispose();
}
