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
    private sealed record BisSet(string Name, Dictionary<string, (uint Item, List<uint> Materia)> Slots, uint Food = 0);
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
                var food = s.TryGetProperty("food", out var fd) && fd.ValueKind == JsonValueKind.Number && fd.TryGetUInt32(out var fid) ? fid : 0u;
                sets.Add(new BisSet(s.TryGetProperty("name", out var n) ? n.GetString() ?? "Сет" : "Сет", slots, food));
            }
        if (sets.Count == 0 && r.TryGetProperty("items", out _))
        {
            // Ссылка на один сет (embed): корень сам является сетом
            var one = ParseXivgear("{\"sets\":[" + r.GetRawText() + "]}").Sets;
            var title = r.TryGetProperty("name", out var n1) ? n1.GetString() ?? "Сет" : "Сет";
            return new Sheet(title, one);
        }
        return new Sheet(r.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "", sets);
    }

    // ───────────── Balance (thebalanceffxiv.com) ─────────────

    private static readonly Dictionary<string, string> BalanceSlug = new()
    {
        ["pld"] = "tanks/paladin", ["war"] = "tanks/warrior", ["drk"] = "tanks/dark-knight", ["gnb"] = "tanks/gunbreaker",
        ["whm"] = "healers/white-mage", ["sch"] = "healers/scholar", ["ast"] = "healers/astrologian", ["sge"] = "healers/sage",
        ["mnk"] = "melee/monk", ["drg"] = "melee/dragoon", ["nin"] = "melee/ninja", ["sam"] = "melee/samurai",
        ["rpr"] = "melee/reaper", ["vpr"] = "melee/viper",
        ["brd"] = "ranged/bard", ["mch"] = "ranged/machinist", ["dnc"] = "ranged/dancer",
        ["blm"] = "casters/black-mage", ["smn"] = "casters/summoner", ["rdm"] = "casters/red-mage", ["pct"] = "casters/pictomancer",
    };

    /// <summary>Ссылки xivgear со страницы BiS Balance: сначала целые листы, потом отдельные сеты.</summary>
    private static List<string> ParseBalance(string html)
    {
        var res = new List<(string Id, bool Embed)>();
        foreach (Match m in Regex.Matches(Uri.UnescapeDataString(html), @"xivgear\.app/\?page=(embed\|)?sl\|([0-9a-f\-]{36})", RegexOptions.IgnoreCase))
        {
            var id = m.Groups[2].Value.ToLowerInvariant();
            if (res.All(x => x.Id != id)) res.Add((id, m.Groups[1].Success));
        }
        return res.OrderBy(x => x.Embed).Select(x => x.Id).ToList();
    }

    private void DrawBalance(string abbr)
    {
        if (!BalanceSlug.TryGetValue(abbr, out var slug)) return;
        var pageUrl = $"https://www.thebalanceffxiv.com/jobs/{slug}/best-in-slot/";
        Theme.SubHeader($"BiS от The Balance для {abbr.ToUpperInvariant()}");
        var ids = Fetch(pageUrl, ParseBalance);
        if (ids is null)
        {
            if (Failed(pageUrl))
            {
                ImGui.TextColored(Theme.Red, "Не удалось открыть страницу Balance.");
                ImGui.SameLine();
                if (ImGui.SmallButton("Повторить##balr")) Retry(pageUrl);
            }
            else ImGui.TextColored(Theme.TextDim, "Загружаю страницу Balance…");
            return;
        }
        if (ids.Count == 0) { ImGui.TextColored(Theme.TextDim, "На странице Balance ссылок не нашлось."); return; }
        foreach (var id in ids.Take(10))
        {
            var url = $"https://api.xivgear.app/shortlink/{id}";
            var sh = Fetch<Sheet>(url, ParseXivgear);
            string label;
            if (sh is not null) label = sh.Sets.Count > 1 ? $"{sh.Title} ({sh.Sets.Count} сетов)" : sh.Title.Length > 0 ? sh.Title : "Сет";
            else if (Failed(url)) continue;
            else label = "загружаю…";
            if (ImGui.Selectable($"{label}##bal{id}", loadedKey == url) && sh is not null)
            {
                loadedKey = url;
                setIndex = 0;
                error = null;
            }
        }
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
            else if (presets.Count == 0) ImGui.TextColored(Theme.TextDim, "У xivgear готовых наборов для этой профессии нет — смотри наборы Balance ниже.");
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
            DrawBalance(abbr);
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

        DrawConsumables(set, rows.Select(r => r.Materia).ToList());
        DrawPlan(rows.Where(r => !equipped.Contains(r.Item) && Owned(r.Item) == 0).Select(r => (r.Name, r.Item)).ToList());

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

    // ───────────── Материя и расходники ─────────────

    private static int foodCount = 10, potCount = 10;
    private static readonly Dictionary<uint, uint> potionByJob = new();

    /// <summary>Лучшее зелье основного стата профессии (Grade N Gemdraught of …).</summary>
    private static uint PotionFor(uint jobId)
    {
        if (potionByJob.TryGetValue(jobId, out var v)) return v;
        uint best = 0;
        try
        {
            var job = Plugin.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(jobId);
            var stat = job is null ? "" : Plugin.Data.GetExcelSheet<BaseParam>(Dalamud.Game.ClientLanguage.English)
                .GetRowOrDefault(job.Value.PrimaryStat)?.Name.ExtractText() ?? "";
            if (stat.Length > 0)
                best = Plugin.Data.GetExcelSheet<Item>(Dalamud.Game.ClientLanguage.English)
                    .Where(i => i.Name.ExtractText().EndsWith("Gemdraught of " + stat, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(i => i.LevelItem.RowId).Select(i => i.RowId).FirstOrDefault();
        }
        catch (Exception e) { Plugin.Log.Warning(e, "BiS: не удалось найти зелье"); }
        return potionByJob[jobId] = best;
    }

    /// <summary>Сколько материи нужно на весь сет, еда сета и зелья — с кнопкой «в покупки».</summary>
    private static void DrawConsumables(BisSet set, List<List<uint>> materiaPerSlot)
    {
        var materia = materiaPerSlot.SelectMany(m => m).GroupBy(m => m).Select(g => (Item: g.Key, Need: g.Count())).OrderByDescending(x => x.Need).ToList();
        var jobId = Plugin.PlayerState.ClassJob.RowId;
        var potion = PotionFor(jobId);
        if (materia.Count == 0 && set.Food == 0 && potion == 0) return;

        Theme.Panel("bcons", () =>
        {
            Theme.SubHeader("Материя и расходники");
            ImGui.TextColored(Theme.Gray, "Материя — на весь сет по раскладке из листа. «Есть» считает сумки и ретейнеров (уже вставленная в вещи не считается).");
            var shopping = new List<(uint Item, int Qty)>();
            foreach (var (item, need) in materia)
            {
                var have = Owned(item);
                var lack = Math.Max(0, need - have);
                if (lack > 0) shopping.Add((item, lack));
                WikiView.ItemLine(item, need, lack == 0 ? $"есть {have} ✓" : $"есть {have}, не хватает {lack}", "bcm" + item);
            }
            if (set.Food != 0)
            {
                ImGui.SetNextItemWidth(90 * Theme.S);
                ImGui.InputInt("штук еды##bcf", ref foodCount);
                foodCount = Math.Clamp(foodCount, 0, 999);
                var have = Owned(set.Food);
                if (foodCount > have) shopping.Add((set.Food, foodCount - have));
                WikiView.ItemLine(set.Food, foodCount, $"еда из сета · есть {have}", "bcfood");
            }
            if (potion != 0)
            {
                ImGui.SetNextItemWidth(90 * Theme.S);
                ImGui.InputInt("штук зелий##bcp", ref potCount);
                potCount = Math.Clamp(potCount, 0, 999);
                var have = Owned(potion);
                if (potCount > have) shopping.Add((potion, potCount - have));
                WikiView.ItemLine(potion, potCount, $"зелье основного стата · есть {have}", "bcpot");
            }
            ImGui.Dummy(new Vector2(0, 3 * Theme.S));
            if (shopping.Count == 0) ImGui.TextColored(Theme.Green, "Всего хватает.");
            else if (ImGui.Button($"Добавить недостающее в «Покупки» ({shopping.Count} поз.)##bcadd"))
            {
                foreach (var (item, qty) in shopping) ShoppingTab.Add(item, qty);
                Plugin.Chat.Print($"[OTSFFXIVGuide] В список покупок добавлено позиций: {shopping.Count}.");
            }
        });
    }

    // ───────────── План сбора ─────────────

    private static int Owned(uint item) => PlayerInfo.ItemCount(item) + RetainerTracker.CountOf(item).Total;

    /// <summary>Что купить за томестоны, за книги, чем улучшить и что выбить — и сколько недель это займёт.</summary>
    private static void DrawPlan(List<(string Slot, uint Item)> missing)
    {
        if (missing.Count == 0) return;
        RaidTier.Build();
        var tomeBuys = new List<(string Slot, uint Item, uint Cost)>();
        var bookBuys = new List<(string Slot, uint Item, uint Book, uint Cost)>();
        var upgrades = new Dictionary<uint, int>();
        var drops = new List<(string Slot, uint Item)>();
        var tomeNeed = 0L;
        var bookNeed = new Dictionary<uint, long>();

        foreach (var (slot, item) in missing)
        {
            var ex = RaidTier.Exchange(item);
            if (ex is null) { drops.Add((slot, item)); continue; }
            var book = ex.FirstOrDefault(c => RaidTier.Books.Contains(c.Item));
            if (book is not null && ex.Count == 1)
            {
                bookBuys.Add((slot, item, book.Item, book.Amount));
                bookNeed[book.Item] = bookNeed.GetValueOrDefault(book.Item) + book.Amount;
                continue;
            }
            var tome = ex.FirstOrDefault(c => c.Tomestone);
            if (tome is not null && ex.Count == 1)
            {
                tomeBuys.Add((slot, item, tome.Amount));
                tomeNeed += tome.Amount;
                continue;
            }
            // Улучшенная вещь: базовая вещь за томестоны + материал из саваджа
            var handled = false;
            foreach (var c in ex)
            {
                if (c.Tomestone) continue;
                var sub = RaidTier.Exchange(c.Item);
                var tomeOfSub = sub?.FirstOrDefault(x => x.Tomestone);
                if (tomeOfSub is not null && WikiView.ItemInfo(c.Item).Ilvl > 1)
                {
                    if (Owned(c.Item) == 0)
                    {
                        tomeBuys.Add((slot, c.Item, tomeOfSub.Amount));
                        tomeNeed += tomeOfSub.Amount;
                    }
                }
                else upgrades[c.Item] = upgrades.GetValueOrDefault(c.Item) + (int)c.Amount;
                handled = true;
            }
            if (!handled) drops.Add((slot, item));
        }

        // Материалы улучшения можно выменять на книги — учитываем в неделях
        foreach (var (mat, n) in upgrades)
        {
            var lack = Math.Max(0, n - Owned(mat));
            if (lack == 0) continue;
            var viaBook = RaidTier.Exchange(mat)?.FirstOrDefault(c => RaidTier.Books.Contains(c.Item));
            if (viaBook is not null) bookNeed[viaBook.Item] = bookNeed.GetValueOrDefault(viaBook.Item) + viaBook.Amount * lack;
        }

        var (_, weekly, tomesHave) = RaidTier.Tomes();
        var perWeek = weekly > 0 ? weekly : 450;
        var tomeWeeks = tomeNeed <= tomesHave ? 0 : (int)Math.Ceiling((tomeNeed - tomesHave) / (double)perWeek);
        var bookWeeks = bookNeed.Count == 0 ? 0 : (int)bookNeed.Max(b => Math.Max(0, b.Value - Owned(b.Key)));
        var weeks = Math.Max(tomeWeeks, bookWeeks);

        Theme.Panel("bplan", () =>
        {
            Theme.SubHeader("План сбора");
            ImGui.TextColored(Theme.Text, weeks == 0
                ? "Всё, что покупается за валюту, можно взять уже сейчас."
                : $"Примерно {weeks} нед.: полный лимит томестонов и одна книга с каждого этажа в неделю.");
            ImGui.TextColored(Theme.Gray, "Обычно сначала берут оружие, потом тело и ноги — они дают больше всего статов.");

            if (tomeBuys.Count > 0)
            {
                ImGui.Dummy(new Vector2(0, 3 * Theme.S));
                var tname = RaidTier.Tomestone == 0 ? "томестоны" : NameTranslations.Show(WikiView.ItemInfo(RaidTier.Tomestone).Name);
                ImGui.TextColored(Theme.Gold, $"За томестоны: нужно {tomeNeed:N0} ({tname}), есть {tomesHave:N0}" + (tomeWeeks > 0 ? $" — ~{tomeWeeks} нед." : ""));
                foreach (var (slot, item, cost) in tomeBuys.OrderByDescending(t => t.Cost))
                    WikiView.ItemLine(item, 0, $"{slot} · {cost} томестонов", "bpt" + slot + item);
            }
            if (bookBuys.Count > 0)
            {
                ImGui.Dummy(new Vector2(0, 3 * Theme.S));
                ImGui.TextColored(Theme.Gold, "За книги саваджа:");
                foreach (var (slot, item, book, cost) in bookBuys.OrderBy(b => RaidTier.Books.IndexOf(b.Book)))
                {
                    var have = Owned(book);
                    WikiView.ItemLine(item, 0, $"{slot} · {cost} книг этажа {RaidTier.Books.IndexOf(book) + 1}, есть {have}" + (have >= cost ? " — можно купить" : ""), "bpb" + slot + item);
                }
            }
            if (upgrades.Count > 0)
            {
                ImGui.Dummy(new Vector2(0, 3 * Theme.S));
                ImGui.TextColored(Theme.Gold, "Улучшение томестоновых вещей:");
                foreach (var (mat, n) in upgrades)
                {
                    var viaBook = RaidTier.Exchange(mat)?.FirstOrDefault(c => RaidTier.Books.Contains(c.Item));
                    var how = viaBook is not null ? $" · или {viaBook.Amount} книг этажа {RaidTier.Books.IndexOf(viaBook.Item) + 1} за штуку" : "";
                    WikiView.ItemLine(mat, n, $"есть {Owned(mat)}{how}", "bpu" + mat);
                }
            }
            if (drops.Count > 0)
            {
                ImGui.Dummy(new Vector2(0, 3 * Theme.S));
                ImGui.TextColored(Theme.Gold, "Только выбить или скрафтить:");
                foreach (var (slot, item) in drops)
                    WikiView.ItemLine(item, 0, $"{slot} · {SourceHint(item)}", "bpd" + slot + item);
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
