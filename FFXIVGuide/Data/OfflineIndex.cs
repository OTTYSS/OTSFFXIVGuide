using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

/// <summary>Индексы офлайн-базы в памяти и сборка карточек вики из них.</summary>
public sealed class OfflineIndex
{
    public sealed record Trade(string Type, uint[] Npcs, WikiAmount[] Get, WikiAmount[] Pay);
    public sealed record NpcInfo(string Name, string Title, uint Zone, uint Map, float X, float Y);
    public sealed record MobPos(uint BaseId, uint Zone, uint Map, float X, float Y, int Level);
    public sealed record FishSrc(uint Spot, int Hookset, int Tug, uint Bait, int Spawn, int Duration, uint[] Weathers, uint[] WeathersFrom);
    public sealed record FishSpotInfo(uint PlaceName, uint Map, float X, float Y, int Level);

    public readonly Dictionary<uint, List<(uint Npc, long Price)>> Vendors = new();
    public readonly Dictionary<uint, List<Trade>> Trades = new();
    public readonly Dictionary<uint, List<Trade>> CurrencyUse = new();
    public readonly Dictionary<uint, HashSet<uint>> NpcShops = new();
    public readonly Dictionary<uint, NpcInfo> Npcs = new();
    public readonly Dictionary<uint, List<MobPos>> MobPositions = new();
    public readonly Dictionary<uint, List<uint>> Drops = new();      // предмет → BNpcName
    public readonly Dictionary<uint, List<uint>> MobDrops = new();   // BNpcName → предметы
    public readonly Dictionary<uint, List<uint>> ItemInstances = new();
    public readonly Dictionary<uint, List<uint>> InstanceItems = new();
    public readonly Dictionary<uint, List<uint>> Loot = new();
    public readonly Dictionary<uint, List<WikiAmount>> QuestRewards = new();
    public readonly Dictionary<uint, List<uint>> ItemQuests = new();
    public readonly Dictionary<uint, List<uint>> Reduction = new();
    public readonly Dictionary<uint, List<uint>> Desynth = new();
    public readonly Dictionary<uint, int> Ventures = new();
    public readonly Dictionary<uint, List<uint>> FateSources = new();
    public readonly Dictionary<uint, List<FishSrc>> Fishing = new();
    public readonly Dictionary<uint, FishSpotInfo> FishingSpots = new();
    public readonly HashSet<uint> Spearfish = new();
    public readonly HashSet<uint> Gardening = new();
    public readonly HashSet<uint> Voyages = new();
    public readonly Dictionary<uint, int> Mogstation = new();

    // ───────────── Чтение ─────────────

    private static JsonDocument? Open(string dir, string file)
    {
        var p = Path.Combine(dir, file);
        return File.Exists(p) ? JsonDocument.Parse(File.ReadAllBytes(p)) : null;
    }

    private static uint U(JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetUInt32(out var v) ? v : e.ValueKind == JsonValueKind.String && uint.TryParse(e.GetString(), out var s) ? s : 0;
    private static int I(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
    private static float F(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? (float)v.GetDouble() : 0;
    private static string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static void Add<TK, TV>(Dictionary<TK, List<TV>> d, TK k, TV v) where TK : notnull
    {
        if (!d.TryGetValue(k, out var l)) d[k] = l = new List<TV>();
        l.Add(v);
    }

    /// <summary>Файлы вида { "itemId": [id, id, …] }.</summary>
    private static void IdLists(string dir, string file, Dictionary<uint, List<uint>> target)
    {
        using var doc = Open(dir, file);
        if (doc is null) return;
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (!uint.TryParse(p.Name, out var k) || p.Value.ValueKind != JsonValueKind.Array) continue;
            var list = p.Value.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("id", out var id) ? U(id) : U(x)).Where(x => x != 0).Distinct().ToList();
            if (list.Count > 0) target[k] = list;
        }
    }

    private static void Keys(string dir, string file, HashSet<uint> target)
    {
        using var doc = Open(dir, file);
        if (doc is null) return;
        foreach (var p in doc.RootElement.EnumerateObject())
            if (uint.TryParse(p.Name, out var k)) target.Add(k);
    }

    public static OfflineIndex Build(string dir)
    {
        var x = new OfflineIndex();

        // Магазины
        using (var doc = Open(dir, "shops.json"))
            if (doc is not null)
                foreach (var shop in doc.RootElement.EnumerateArray())
                {
                    var type = S(shop, "type");
                    var npcs = shop.TryGetProperty("npcs", out var n) ? n.EnumerateArray().Select(U).Where(v => v != 0).ToArray() : Array.Empty<uint>();
                    if (!shop.TryGetProperty("trades", out var trades)) continue;
                    foreach (var t in trades.EnumerateArray())
                    {
                        WikiAmount[] Amounts(string key) => t.TryGetProperty(key, out var a)
                            ? a.EnumerateArray().Select(c => new WikiAmount(c.TryGetProperty("id", out var id) ? U(id) : 0, Math.Max(1, I(c, "amount")))).Where(c => c.Item != 0).ToArray()
                            : Array.Empty<WikiAmount>();
                        var get = Amounts("items");
                        var pay = Amounts("currencies");
                        if (get.Length == 0) continue;
                        foreach (var npc in npcs)
                        {
                            if (!x.NpcShops.TryGetValue(npc, out var set)) x.NpcShops[npc] = set = new HashSet<uint>();
                            foreach (var g in get) set.Add(g.Item);
                        }
                        if (type == "GilShop" && pay.Length == 1 && pay[0].Item == 1)
                        {
                            foreach (var g in get)
                                foreach (var npc in npcs.Take(30))
                                    Add(x.Vendors, g.Item, (npc, (long)pay[0].Amount));
                            continue;
                        }
                        var trade = new Trade(type, npcs, get, pay);
                        foreach (var g in get) Add(x.Trades, g.Item, trade);
                        foreach (var p in pay)
                            if (p.Item > 1) Add(x.CurrencyUse, p.Item, trade);
                    }
                }

        // NPC
        using (var doc = Open(dir, "npcs.json"))
            if (doc is not null)
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (!uint.TryParse(p.Name, out var id)) continue;
                    var o = p.Value;
                    var name = S(o, "en");
                    if (name.Length == 0) continue;
                    var title = o.TryGetProperty("title", out var tt) && tt.ValueKind == JsonValueKind.Object ? S(tt, "en") : "";
                    uint zone = 0, map = 0; float px = 0, py = 0;
                    if (o.TryGetProperty("position", out var pos) && pos.ValueKind == JsonValueKind.Object)
                    {
                        zone = pos.TryGetProperty("zoneid", out var z) ? U(z) : 0;
                        map = pos.TryGetProperty("map", out var m) ? U(m) : 0;
                        px = F(pos, "x"); py = F(pos, "y");
                    }
                    x.Npcs[id] = new NpcInfo(name, title, zone, map, px, py);
                }

        // Мобы: позиции (ключ — BNpcName)
        using (var doc = Open(dir, "monsters.json"))
            if (doc is not null)
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (!uint.TryParse(p.Name, out var nameId)) continue;
                    var baseId = p.Value.TryGetProperty("baseid", out var b) ? U(b) : 0;
                    if (!p.Value.TryGetProperty("positions", out var arr)) continue;
                    var list = arr.EnumerateArray()
                        .Select(o => new MobPos(baseId, o.TryGetProperty("zoneid", out var z) ? U(z) : 0, o.TryGetProperty("map", out var m) ? U(m) : 0, F(o, "x"), F(o, "y"), I(o, "level")))
                        .Where(m => m.Zone != 0)
                        .GroupBy(m => m.Zone)
                        .Select(g => new MobPos(baseId, g.Key, g.First().Map, g.Average(m => m.X), g.Average(m => m.Y), g.Max(m => m.Level)))
                        .ToList();
                    if (list.Count > 0) x.MobPositions[nameId] = list;
                }

        IdLists(dir, "drop-sources.json", x.Drops);
        foreach (var (item, mobs) in x.Drops)
            foreach (var m in mobs) Add(x.MobDrops, m, item);
        IdLists(dir, "instance-sources.json", x.ItemInstances);
        IdLists(dir, "reverse-instance-sources.json", x.InstanceItems);
        IdLists(dir, "loot-sources.json", x.Loot);
        IdLists(dir, "quest-sources.json", x.ItemQuests);
        IdLists(dir, "reduction.json", x.Reduction);
        IdLists(dir, "desynth.json", x.Desynth);
        IdLists(dir, "fate-sources.json", x.FateSources);
        var ventures = new Dictionary<uint, List<uint>>();
        IdLists(dir, "venture-sources.json", ventures);
        foreach (var (k, v) in ventures) x.Ventures[k] = v.Count;
        Keys(dir, "spearfishing-sources.json", x.Spearfish);
        Keys(dir, "gardening-sources.json", x.Gardening);
        Keys(dir, "voyage-sources.json", x.Voyages);

        using (var doc = Open(dir, "mogstation-sources.json"))
            if (doc is not null)
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (uint.TryParse(p.Name, out var k)) x.Mogstation[k] = I(p.Value, "price");

        // Квесты: награды
        using (var doc = Open(dir, "quests.json"))
            if (doc is not null)
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (!uint.TryParse(p.Name, out var q) || !p.Value.TryGetProperty("rewards", out var r) || r.ValueKind != JsonValueKind.Array) continue;
                    var list = r.EnumerateArray().Select(o => new WikiAmount(o.TryGetProperty("id", out var id) ? U(id) : 0, Math.Max(1, I(o, "amount")))).Where(a => a.Item != 0).ToList();
                    if (list.Count > 0) x.QuestRewards[q] = list;
                }

        // Рыбалка
        using (var doc = Open(dir, "fishing-sources.json"))
            if (doc is not null)
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (!uint.TryParse(p.Name, out var fish)) continue;
                    var list = p.Value.EnumerateArray().Select(o => new FishSrc(
                        o.TryGetProperty("spot", out var sp) ? U(sp) : 0, I(o, "hookset"), I(o, "tug"), o.TryGetProperty("bait", out var bt) ? U(bt) : 0,
                        o.TryGetProperty("spawn", out var s1) && s1.ValueKind == JsonValueKind.Number ? s1.GetInt32() : -1,
                        o.TryGetProperty("duration", out var d1) && d1.ValueKind == JsonValueKind.Number ? d1.GetInt32() : 0,
                        o.TryGetProperty("weathers", out var w) && w.ValueKind == JsonValueKind.Array ? w.EnumerateArray().Select(U).ToArray() : Array.Empty<uint>(),
                        o.TryGetProperty("weathersFrom", out var wf) && wf.ValueKind == JsonValueKind.Array ? wf.EnumerateArray().Select(U).ToArray() : Array.Empty<uint>()))
                        .ToList();
                    if (list.Count > 0) x.Fishing[fish] = list;
                }
        using (var doc = Open(dir, "fishing-spots.json"))
            if (doc is not null)
                foreach (var o in doc.RootElement.EnumerateArray())
                {
                    var id = o.TryGetProperty("id", out var i) ? U(i) : 0;
                    if (id == 0) continue;
                    float cx = 0, cy = 0;
                    if (o.TryGetProperty("coords", out var c) && c.ValueKind == JsonValueKind.Object) { cx = F(c, "x"); cy = F(c, "y"); }
                    x.FishingSpots[id] = new FishSpotInfo(o.TryGetProperty("placeId", out var pl) ? U(pl) : 0, o.TryGetProperty("mapId", out var mp) ? U(mp) : 0, cx, cy, I(o, "level"));
                }

        return x;
    }

    // ───────────── Карточки из базы ─────────────

    private readonly ConcurrentDictionary<uint, WikiItem> itemCache = new();
    private readonly ConcurrentDictionary<ulong, WikiMob?> mobCache = new();
    private static Dictionary<uint, List<WikiAmount>>? ingredientOf;
    private static Dictionary<uint, List<uint>>? nextQuests;

    public static string BNpcName(uint id) =>
        GuideData.Capitalize(Plugin.Data.GetExcelSheet<BNpcName>().GetRowOrDefault(id)?.Singular.ExtractText() ?? "");

    public WikiNpc NpcRef(uint id) =>
        Npcs.TryGetValue(id, out var n)
            ? new WikiNpc(id, n.Name, n.Zone, n.X, n.Y, n.X > 0)
            : new WikiNpc(id, Plugin.Data.GetExcelSheet<ENpcResident>().GetRowOrDefault(id)?.Singular.ExtractText() is { Length: > 0 } s ? GuideData.Capitalize(s) : $"NPC #{id}", 0, 0, 0, false);

    public WikiMobRow MobRow(uint nameId)
    {
        var pos = MobPositions.GetValueOrDefault(nameId)?.FirstOrDefault();
        var id = (ulong)(pos?.BaseId ?? 0) * 10_000_000_000UL + nameId;
        return new WikiMobRow(id, BNpcName(nameId), pos is { Level: > 0 } ? pos.Level.ToString() : "", pos?.Zone ?? 0);
    }

    private static string TradeName(string type) => type switch
    {
        "GCShop" => "Grand Company",
        "SpecialShop" => "Обмен",
        _ => type,
    };

    private WikiTrade ToWiki(Trade t) => new(TradeName(t.Type), t.Npcs.Take(3).Select(NpcRef).ToList(), t.Get.ToList(), t.Pay.ToList());

    private static string Hookset(int h) => h switch { 1 => "Powerful Hookset", 2 => "Precision Hookset", _ => "" };
    private static string Tug(int t) => t switch { 0 => "Medium (!!)", 1 => "Heavy (!!!)", 2 => "Light (!)", _ => "" };

    public static (uint PlaceName, uint Territory) MapInfo(uint mapId)
    {
        var m = Plugin.Data.GetExcelSheet<Map>().GetRowOrDefault(mapId);
        return m is null ? (0, 0) : (m.Value.PlaceName.RowId, m.Value.TerritoryType.RowId);
    }

    private List<uint> BaitChain(uint bait, uint spot)
    {
        var chain = new List<uint> { bait };
        var guard = 0;
        while (guard++ < 5 && Fishing.TryGetValue(chain[0], out var src) && src.FirstOrDefault(s => s.Spot == spot) is { Bait: > 0 } prev && !chain.Contains(prev.Bait))
            chain.Insert(0, prev.Bait);
        return chain;
    }

    public WikiItem Item(uint id)
    {
        if (itemCache.TryGetValue(id, out var cached)) return cached;
        var w = new WikiItem { Id = id };

        if (Drops.TryGetValue(id, out var mobs)) w.Drops = mobs.Select(MobRow).ToList();
        if (Vendors.TryGetValue(id, out var v))
        {
            w.Vendors = v.Select(x => x.Npc).Distinct().Select(NpcRef).OrderByDescending(n => n.HasCoords).ToList();
            w.VendorPrice = v.Min(x => x.Price);
        }
        if (Trades.TryGetValue(id, out var tr)) w.TradeShops = tr.Select(ToWiki).ToList();
        if (CurrencyUse.TryGetValue(id, out var cu)) w.TradeCurrency = cu.Take(400).Select(ToWiki).ToList();

        // Крафт — из файлов игры
        var look = Plugin.Data.GetExcelSheet<RecipeLookup>().GetRowOrDefault(id);
        if (look is { } l)
            foreach (var r in new[] { l.CRP, l.BSM, l.ARM, l.GSM, l.LTW, l.WVR, l.ALC, l.CUL })
                if (r.RowId != 0 && r.ValueNullable is { } rec)
                {
                    var ings = new List<WikiAmount>();
                    for (var i = 0; i < rec.Ingredient.Count; i++)
                        if (rec.Ingredient[i].RowId != 0 && rec.AmountIngredient[i] > 0)
                            ings.Add(new WikiAmount(rec.Ingredient[i].RowId, rec.AmountIngredient[i]));
                    w.Crafts.Add(new WikiCraft(8 + rec.CraftType.RowId, rec.RecipeLevelTable.ValueNullable?.ClassJobLevel ?? 0,
                        rec.RecipeLevelTable.ValueNullable?.Stars ?? 0, ings, Math.Max(1, (int)rec.AmountResult)));
                }

        // Сбор — из файлов игры
        foreach (var s in GatherIndex.For(id).Take(6))
            w.Nodes.Add(new WikiNode(0, $"{s.Place}, {s.Zone}", s.Level, 0, s.Type switch { "Mining" => 0, "Quarrying" => 1, "Logging" => 2, "Harvesting" => 3, _ => 4 }, s.Timed ? "по времени" : ""));

        if (ItemInstances.TryGetValue(id, out var inst))
            w.Instances = inst.Select(iid =>
            {
                var ic = Plugin.Data.GetExcelSheet<InstanceContent>().GetRowOrDefault(iid);
                var cfc = Plugin.Data.GetExcelSheet<ContentFinderCondition>().FirstOrDefault(c => c.ContentLinkType == 1 && c.Content.RowId == iid);
                var name = cfc.RowId != 0 ? GuideData.Capitalize(cfc.Name.ExtractText()) : $"#{iid}";
                return new WikiRef(iid.ToString(), name, cfc.RowId != 0 ? cfc.ContentType.ValueNullable?.Name.ExtractText() ?? "" : "");
            }).ToList();

        if (Desynth.TryGetValue(id, out var ds)) w.DesynthFrom = ds;
        if (Reduction.TryGetValue(id, out var rd)) w.ReducedFrom = rd;
        if (Loot.TryGetValue(id, out var lt)) w.Treasure = lt;
        if (ItemQuests.TryGetValue(id, out var qs))
            w.Quests = qs.Select(q => new WikiRef(q.ToString(), Plugin.Data.GetExcelSheet<Quest>().GetRowOrDefault(q)?.Name.ExtractText() ?? $"#{q}", "")).ToList();
        if (Ventures.TryGetValue(id, out var vc)) w.Ventures = vc;

        // Рыбалка
        if (Fishing.TryGetValue(id, out var fish))
            foreach (var f in fish)
            {
                FishingSpots.TryGetValue(f.Spot, out var spot);
                var (zone, _) = spot is null ? (0u, 0u) : MapInfo(spot.Map);
                var spotName = spot is null ? $"Место #{f.Spot}" : Plugin.Data.GetExcelSheet<PlaceName>().GetRowOrDefault(spot.PlaceName)?.Name.ExtractText() ?? "";
                var weathers = f.Weathers.Select(EorzeaWeather.WeatherName).Where(s => s.Length > 0).ToList();
                var from = f.WeathersFrom.Select(EorzeaWeather.WeatherName).Where(s => s.Length > 0).ToList();
                var timed = f.Spawn >= 0 && f.Duration > 0 && f.Duration < 24;
                var sh = timed ? f.Spawn : 0;
                var eh = timed ? (f.Spawn + f.Duration) % 24 : 0;
                w.FishSpots.Add(new WikiFishSpot(spotName, zone, spot?.X ?? 0, spot?.Y ?? 0, spot is { X: > 0 },
                    Hookset(f.Hookset), Tug(f.Tug), f.Bait != 0 ? new List<List<uint>> { BaitChain(f.Bait, f.Spot) } : new(),
                    timed ? $"{sh:00}:00–{eh:00}:00 ET" : "",
                    (from.Count > 0 ? string.Join(", ", from) + " → " : "") + string.Join(", ", weathers), "",
                    sh, eh, weathers, from));
            }
        if (Spearfish.Contains(id)) w.FishGuide = "Ловится гарпуном (spearfishing).";

        // Где используется — рецепты из файлов игры
        ingredientOf ??= BuildIngredientOf();
        if (ingredientOf.TryGetValue(id, out var io)) w.IngredientOf = io;

        itemCache[id] = w;
        return w;
    }

    private static Dictionary<uint, List<WikiAmount>> BuildIngredientOf()
    {
        var map = new Dictionary<uint, List<WikiAmount>>();
        foreach (var r in Plugin.Data.GetExcelSheet<Recipe>())
        {
            if (r.ItemResult.RowId == 0) continue;
            for (var i = 0; i < r.Ingredient.Count; i++)
                if (r.Ingredient[i].RowId != 0 && r.AmountIngredient[i] > 0)
                    Add(map, r.Ingredient[i].RowId, new WikiAmount(r.ItemResult.RowId, r.AmountIngredient[i]));
        }
        foreach (var k in map.Keys.ToList()) map[k] = map[k].GroupBy(a => a.Item).Select(g => g.First()).ToList();
        return map;
    }

    public WikiMob? Mob(ulong id)
    {
        if (mobCache.TryGetValue(id, out var c)) return c;
        var nameId = (uint)(id % 10_000_000_000UL);
        var name = BNpcName(nameId);
        WikiMob? m = null;
        if (name.Length > 0)
        {
            var pos = MobPositions.GetValueOrDefault(nameId)?.FirstOrDefault();
            m = new WikiMob(id, name, pos is { Level: > 0 } ? pos.Level.ToString() : "", pos?.Zone ?? 0, pos?.X ?? 0, pos?.Y ?? 0, pos is { X: > 0 },
                false, MobDrops.GetValueOrDefault(nameId) ?? new List<uint>());
        }
        mobCache[id] = m;
        return m;
    }

    public WikiNpcFull? Npc(uint id)
    {
        if (!Npcs.TryGetValue(id, out var n)) return null;
        var shops = NpcShops.TryGetValue(id, out var items) ? new List<WikiNpcShop> { new("Магазин и обмен", items.ToList()) } : new List<WikiNpcShop>();
        return new WikiNpcFull(id, n.Name, n.Title, n.Zone, n.X, n.Y, n.X > 0, shops);
    }

    public WikiInstance? Duty(uint instanceId) =>
        InstanceItems.TryGetValue(instanceId, out var items) ? new WikiInstance(instanceId, "", new(), new(), items) : null;

    public WikiQuestFull? Quest(uint id)
    {
        var q = Plugin.Data.GetExcelSheet<Quest>().GetRowOrDefault(id);
        if (q is null) return null;
        nextQuests ??= Plugin.Data.GetExcelSheet<Quest>()
            .SelectMany(x => x.PreviousQuest.Where(p => p.RowId != 0).Select(p => (Prev: p.RowId, Next: x.RowId)))
            .GroupBy(x => x.Prev).ToDictionary(g => g.Key, g => g.Select(x => x.Next).ToList());
        var rewards = QuestRewards.GetValueOrDefault(id) ?? new List<WikiAmount>();
        if (q.Value.GilReward > 0) rewards = rewards.Prepend(new WikiAmount(1, (int)q.Value.GilReward)).ToList();
        var next = (nextQuests.GetValueOrDefault(id) ?? new List<uint>()).Select(n => new WikiRef(n.ToString(), "", "")).ToList();
        var prev = q.Value.PreviousQuest.Where(p => p.RowId != 0).Select(p => new WikiRef(p.RowId.ToString(), "", "")).ToList();
        WikiNpc? issuer = q.Value.IssuerStart.RowId is >= 1000000 and < 2000000 ? NpcRef(q.Value.IssuerStart.RowId) : null;
        return new WikiQuestFull(id, q.Value.Name.ExtractText(), rewards, new List<WikiAmount>(), new List<uint>(), next, prev, 0, issuer);
    }
}
