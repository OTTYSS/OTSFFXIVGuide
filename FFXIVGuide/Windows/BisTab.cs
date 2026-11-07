using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>
/// BiS-сеты: готовые наборы xivgear (Balance) для профессии или свой по ссылке xivgear / Etro.
/// Показывает, какие слоты уже собраны, чего не хватает и где это взять.
/// </summary>
internal sealed class BisTab
{
    private sealed record BisSet(string Name, Dictionary<string, (uint Item, List<uint> Materia)> Slots);
    private sealed record Sheet(string Title, List<BisSet> Sets);

    private static readonly HttpClient Http = Net.Create(TimeSpan.FromSeconds(30));
    private static readonly ConcurrentDictionary<string, object?> cache = new();
    private static readonly ConcurrentDictionary<string, byte> loading = new();

    private string link = "";
    private string? loadedKey;
    private int setIndex;
    private List<(string Path, string Title)>? presets;
    private string presetsFor = "";
    private string? error;

    private static readonly (string Key, string Name)[] SlotOrder =
    {
        ("Weapon", "Оружие"), ("OffHand", "Вторая рука"), ("Head", "Голова"), ("Body", "Тело"), ("Hand", "Руки"),
        ("Legs", "Ноги"), ("Feet", "Обувь"), ("Ears", "Серьги"), ("Neck", "Ожерелье"), ("Wrist", "Браслеты"),
        ("RingLeft", "Кольцо"), ("RingRight", "Кольцо"),
    };

    static BisTab() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("OTSFFXIVGuide (Dalamud plugin)");

    // ───────────── Загрузка ─────────────

    private static T? Fetch<T>(string url, Func<string, T> parse) where T : class
    {
        if (cache.TryGetValue(url, out var v)) return v as T;
        if (loading.TryAdd(url, 0))
            _ = Task.Run(async () =>
            {
                try { cache[url] = parse(await Http.GetStringAsync(url).ConfigureAwait(false)); }
                catch (Exception e)
                {
                    Plugin.Log.Warning(e, $"BiS: не удалось загрузить {url}");
                    cache[url] = null;
                }
                finally { loading.TryRemove(url, out _); }
            });
        return null;
    }

    /// <summary>Загрузка закончилась ошибкой (в кеше пусто и запрос не идёт).</summary>
    private static bool Failed(string url) => cache.TryGetValue(url, out var v) && v is null && !loading.ContainsKey(url);

    private static void Retry(string url) => cache.TryRemove(url, out _);

    private static List<(string, string)> ParseIndex(string json, string job)
    {
        var res = new List<(string, string)>();
        using var doc = JsonDocument.Parse(json);
        void Walk(JsonElement n, string path)
        {
            var name = n.TryGetProperty("fileName", out var f) ? f.GetString() ?? "" : "";
            var p = path.Length == 0 && name == "work" ? "" : path + "/" + name;
            if (n.TryGetProperty("type", out var t) && t.GetString() == "file")
            {
                if (p.StartsWith("/" + job + "/", StringComparison.OrdinalIgnoreCase) && !p.Contains("/archive/"))
                    res.Add((p.TrimStart('/'), n.TryGetProperty("contentName", out var c) ? c.GetString() ?? p : p));
            }
            if (n.TryGetProperty("children", out var ch))
                foreach (var c2 in ch.EnumerateArray()) Walk(c2, p);
        }
        Walk(doc.RootElement, "");
        return res.OrderBy(x => x.Item1.EndsWith("current.json") ? 0 : 1).ThenBy(x => x.Item2).ToList();
    }

    /// <summary>Лист xivgear: { name, sets: [ { name, items: { Weapon: { id, materia: [{id}] } } } ] }.</summary>
    private static Sheet ParseXivgear(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var sets = new List<BisSet>();
        if (r.TryGetProperty("sets", out var arr))
            foreach (var s in arr.EnumerateArray())
            {
                if (s.TryGetProperty("isSeparator", out var sep) && sep.ValueKind == JsonValueKind.True) continue;
                if (!s.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object) continue;
                var slots = new Dictionary<string, (uint, List<uint>)>();
                foreach (var p in items.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.Object || !p.Value.TryGetProperty("id", out var id) || !id.TryGetUInt32(out var iid)) continue;
                    var mat = p.Value.TryGetProperty("materia", out var m) && m.ValueKind == JsonValueKind.Array
                        ? m.EnumerateArray().Select(x => x.TryGetProperty("id", out var mi) && mi.TryGetUInt32(out var u) ? u : 0u).Where(x => x > 0).ToList()
                        : new List<uint>();
                    slots[p.Name] = (iid, mat);
                }
                sets.Add(new BisSet(s.TryGetProperty("name", out var n) ? n.GetString() ?? "Сет" : "Сет", slots));
            }
        return new Sheet(r.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "", sets);
    }

    /// <summary>Etro: { name, weapon, offHand, head, body, hands, legs, feet, ears, neck, wrists, fingerL, fingerR }.</summary>
    private static Sheet ParseEtro(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var map = new (string Etro, string Slot)[]
        {
            ("weapon", "Weapon"), ("offHand", "OffHand"), ("head", "Head"), ("body", "Body"), ("hands", "Hand"), ("legs", "Legs"),
            ("feet", "Feet"), ("ears", "Ears"), ("neck", "Neck"), ("wrists", "Wrist"), ("fingerL", "RingLeft"), ("fingerR", "RingRight"),
        };
        var slots = new Dictionary<string, (uint, List<uint>)>();
        foreach (var (e, s) in map)
            if (r.TryGetProperty(e, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetUInt32(out var id) && id > 0)
                slots[s] = (id, new List<uint>());
        var name = r.TryGetProperty("name", out var n) ? n.GetString() ?? "Etro" : "Etro";
        return new Sheet(name, new List<BisSet> { new(name, slots) });
    }

    /// <summary>Ссылка → адрес API и способ разбора.</summary>
    private static (string Url, bool Etro)? Resolve(string input)
    {
        input = Uri.UnescapeDataString(input.Trim());
        var m = Regex.Match(input, @"etro\.gg/gearset/([0-9a-f\-]{36})", RegexOptions.IgnoreCase);
        if (m.Success) return ($"https://etro.gg/api/gearsets/{m.Groups[1].Value}/", true);
        m = Regex.Match(input, @"sl\|([0-9a-f\-]{36})", RegexOptions.IgnoreCase);
        if (m.Success) return ($"https://api.xivgear.app/shortlink/{m.Groups[1].Value}", false);
        m = Regex.Match(input, @"bis\|([a-z]{3})\|([\w\-/|]+)", RegexOptions.IgnoreCase);
        if (m.Success) return ($"https://staticbis.xivgear.app/{m.Groups[1].Value.ToLowerInvariant()}/{m.Groups[2].Value.Replace('|', '/')}.json", false);
        return null;
    }

    // ───────────── Окно ─────────────

    public void Draw()
    {
        Theme.Header("BiS-сеты", "Лучший сет профессии: что уже собрано, чего не хватает и где взять");
        if (!PlayerInfo.Ready || Plugin.PlayerState.ClassJob.ValueNullable is not { } job) { Theme.Panel("bnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем.")); return; }
        var abbr = GuideData.JobAbbrEnglish(job.RowId).ToLowerInvariant();

        Theme.Panel("bsrc", () =>
        {
            if (presetsFor != abbr) { presets = null; presetsFor = abbr; }
            const string indexUrl = "https://staticbis.xivgear.app/_index.json";
            var idx = Fetch(indexUrl, j => (object)j) as string;
            if (idx is not null && presets is null) presets = ParseIndex(idx, abbr);
            Theme.SubHeader($"Готовые наборы xivgear для {abbr.ToUpperInvariant()}");
            if (presets is null && Failed(indexUrl))
            {
                ImGui.TextColored(Theme.Red, "Не удалось загрузить список с xivgear.app.");
                ImGui.SameLine();
                if (ImGui.SmallButton("Повторить##bisidx")) Retry(indexUrl);
            }
            else if (presets is null) ImGui.TextColored(Theme.TextDim, "Загружаю список…");
            else if (presets.Count == 0) ImGui.TextColored(Theme.TextDim, "Для этой профессии готовых наборов нет (у хилеров их публикует Balance в виде ссылок) — вставь ссылку ниже.");
            else
                foreach (var (path, title) in presets.Take(8))
                {
                    if (ImGui.Selectable($"{title}##bp{path}", loadedKey == $"https://staticbis.xivgear.app/{path}"))
                    {
                        loadedKey = $"https://staticbis.xivgear.app/{path}";
                        setIndex = 0;
                        error = null;
                    }
                }
            ImGui.Dummy(new Vector2(0, 4 * Theme.S));
            ImGui.SetNextItemWidth(Math.Min(520 * Theme.S, ImGui.GetContentRegionAvail().X - 120 * Theme.S));
            ImGui.InputTextWithHint("##bislink", "Ссылка xivgear.app или etro.gg/gearset/…", ref link, 300);
            ImGui.SameLine();
            if (ImGui.Button("Загрузить##bisgo"))
            {
                var r = Resolve(link);
                if (r is null) error = "Не понял ссылку. Нужна ссылка xivgear (…?page=sl|…) или etro.gg/gearset/…";
                else { loadedKey = (r.Value.Etro ? "etro:" : "") + r.Value.Url; setIndex = 0; error = null; }
            }
            if (error is not null) ImGui.TextColored(Theme.Red, error);
        });

        if (loadedKey is null) return;
        var etro = loadedKey.StartsWith("etro:");
        var url = etro ? loadedKey[5..] : loadedKey;
        var sheet = Fetch<Sheet>(url, etro ? ParseEtro : ParseXivgear);
        if (sheet is null)
        {
            Theme.Panel("bload", () =>
            {
                if (!Failed(url)) { ImGui.TextColored(Theme.TextDim, "Загружаю сет…"); return; }
                ImGui.TextColored(Theme.Red, "Не удалось загрузить сет (ссылка устарела или сайт недоступен).");
                ImGui.SameLine();
                if (ImGui.SmallButton("Повторить##bisset")) Retry(url);
            });
            return;
        }
        if (sheet.Sets.Count == 0) { Theme.Panel("bempty", () => ImGui.TextColored(Theme.TextDim, "В листе нет сетов.")); return; }
        setIndex = Math.Clamp(setIndex, 0, sheet.Sets.Count - 1);

        if (!ImGui.BeginChild("##bis", Vector2.Zero)) { ImGui.EndChild(); return; }
        Theme.Panel("bsets", () =>
        {
            ImGui.TextColored(Theme.Gold, sheet.Title);
            var names = sheet.Sets.Select(s => s.Name).ToArray();
            ImGui.SetNextItemWidth(360 * Theme.S);
            if (ImGui.BeginCombo("##bisset", names[setIndex]))
            {
                for (var i = 0; i < names.Length; i++)
                    if (ImGui.Selectable($"{names[i]}##bs{i}", i == setIndex)) setIndex = i;
                ImGui.EndCombo();
            }
        });
        DrawSet(sheet.Sets[setIndex]);
        ImGui.EndChild();
    }

    private static void DrawSet(BisSet set)
    {
        var equipped = Enumerable.Range(0, 13).Select(PlayerInfo.EquippedItem).Where(x => x != 0).ToHashSet();
        var rows = SlotOrder.Where(s => set.Slots.ContainsKey(s.Key)).Select(s => (s.Name, set.Slots[s.Key].Item, set.Slots[s.Key].Materia)).ToList();
        var done = rows.Count(r => equipped.Contains(r.Item));
        var have = rows.Count(r => !equipped.Contains(r.Item) && PlayerInfo.ItemCount(r.Item) + RetainerTracker.CountOf(r.Item).Total > 0);

        Theme.Panel("bprog", () =>
        {
            Theme.Progress("Надето из сета", done, Math.Max(1, rows.Count), done == rows.Count ? Theme.Green : Theme.Gold);
            if (have > 0) ImGui.TextColored(Theme.Blue, $"Ещё {have} вещей уже есть в сумках или у ретейнеров — просто надень.");
        });

        Theme.Panel("bslots", () =>
        {
            foreach (var (slot, item, materia) in rows)
            {
                var on = equipped.Contains(item);
                var count = PlayerInfo.ItemCount(item) + RetainerTracker.CountOf(item).Total;
                var status = on ? "надето ✓" : count > 0 ? "есть — надень" : SourceHint(item);
                ImGui.TextColored(Theme.TextDim, slot);
                WikiView.ItemLine(item, 0, status, "bis" + slot);
                if (materia.Count > 0)
                    ImGui.TextColored(Theme.Gray, "    Материя: " + string.Join(", ", materia.GroupBy(m => m).Select(g => $"{(g.Count() > 1 ? g.Count() + "× " : "")}{NameTranslations.Show(WikiView.ItemInfo(g.Key).Name)}")));
            }
        });
    }

    /// <summary>Коротко, где взять: из офлайн-базы или Garland.</summary>
    private static string SourceHint(uint item)
    {
        var w = WikiService.Instance.Item(item);
        if (w is null) return "нет — нажми, где взять";
        if (w.TradeShops.FirstOrDefault() is { } t && t.Pay.Count > 0)
            return "обмен: " + string.Join(" + ", t.Pay.Select(p => $"{p.Amount}× {NameTranslations.Show(WikiView.ItemInfo(p.Item).Name)}"));
        if (w.Instances.FirstOrDefault() is { } i) return "выпадает: " + NameTranslations.Show(i.Name);
        if (w.Crafts.Count > 0) return "крафт";
        if (w.Vendors.Count > 0) return "у торговца";
        return "нет — нажми, где взять";
    }
}
