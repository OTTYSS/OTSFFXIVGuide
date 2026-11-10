using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Калькулятор окраски чокобо и справочник садоводства (скрещивание семян).</summary>
internal sealed class ChocoboGardenTab
{
    private static readonly string[] Sections = { "Окраска чокобо", "Садоводство" };
    private int section;

    // ───────────── Чокобо ─────────────

    private sealed record Color(uint Id, string Name, int R, int G, int B);

    /// <summary>Фрукты: изменение R/G/B за одно кормление (шаг 5).</summary>
    private static readonly (string En, string Ru, int R, int G, int B)[] Fruits =
    {
        ("Xelphatol Apple", "Яблоко (Xelphatol Apple)", 5, -5, -5),
        ("Mamook Pear", "Груша (Mamook Pear)", -5, 5, -5),
        ("O'Ghomoro Berries", "Ягоды (O'Ghomoro Berries)", -5, -5, 5),
        ("Doman Plum", "Слива (Doman Plum)", -5, 5, 5),
        ("Valfruit", "Валфрукт (Valfruit)", 5, -5, 5),
        ("Cieldalaes Pineapple", "Ананас (Cieldalaes Pineapple)", 5, 5, -5),
    };

    private List<Color>? colors;
    private uint[] fruitIds = Array.Empty<uint>();
    private uint lemonId;
    private int fromIdx, toIdx;
    private (int[] Counts, List<(int Fruit, int Count)> Steps, Color Final, bool Ordered)? plan;
    private string planKey = "";

    private void BuildColors()
    {
        colors = Plugin.Data.GetExcelSheet<Stain>()
            .Where(s => s.RowId is >= 1 and <= 85 && s.Name.ExtractText().Length > 0)
            .Select(s => new Color(s.RowId, GuideData.Capitalize(s.Name.ExtractText()), (int)((s.Color >> 16) & 0xFF), (int)((s.Color >> 8) & 0xFF), (int)(s.Color & 0xFF)))
            .OrderBy(c => c.Id).ToList();
        var en = Plugin.Data.GetExcelSheet<Item>(Dalamud.Game.ClientLanguage.English);
        uint Find(string n) => en.FirstOrDefault(i => i.Name.ExtractText().Equals(n, StringComparison.OrdinalIgnoreCase)).RowId;
        fruitIds = Fruits.Select(f => Find(f.En)).ToArray();
        lemonId = Find("Han Lemon");
        var enStain = Plugin.Data.GetExcelSheet<Stain>(Dalamud.Game.ClientLanguage.English);
        fromIdx = Math.Max(0, colors.FindIndex(c => enStain.GetRowOrDefault(c.Id)?.Name.ExtractText().Equals("Desert Yellow", StringComparison.OrdinalIgnoreCase) == true));
    }

    private Color Nearest(int r, int g, int b) =>
        colors!.OrderBy(c => (c.R - r) * (c.R - r) + (c.G - g) * (c.G - g) + (c.B - b) * (c.B - b)).First();

    /// <summary>Минимум фруктов, чтобы итоговый цвет «прилип» к нужному; порядок, при котором R/G/B не выходят за 0–255.</summary>
    private void Solve(Color from, Color to)
    {
        double dr = (to.R - from.R) / 5.0, dg = (to.G - from.G) / 5.0, db = (to.B - from.B) / 5.0;
        double p0 = -(dg + db) / 2, q0 = -(dr + db) / 2, s0 = -(dr + dg) / 2;
        (int P, int Q, int S, int N)? best = null;
        for (var p = (int)Math.Round(p0) - 8; p <= (int)Math.Round(p0) + 8; p++)
        for (var q = (int)Math.Round(q0) - 8; q <= (int)Math.Round(q0) + 8; q++)
        for (var s = (int)Math.Round(s0) - 8; s <= (int)Math.Round(s0) + 8; s++)
        {
            var n = Math.Abs(p) + Math.Abs(q) + Math.Abs(s);
            if (best is not null && n >= best.Value.N) continue;
            int r = from.R + 5 * (p - q - s), g = from.G + 5 * (-p + q - s), b = from.B + 5 * (-p - q + s);
            if (r is < 0 or > 255 || g is < 0 or > 255 || b is < 0 or > 255) continue;
            if (Nearest(r, g, b).Id != to.Id) continue;
            best = (p, q, s, n);
        }
        if (best is null) { plan = null; return; }
        var (P, Q, S, _) = best.Value;
        // индексы: 0 яблоко, 1 груша, 2 ягоды, 3 слива, 4 валфрукт, 5 ананас
        var counts = new int[6];
        if (P > 0) counts[0] = P; else counts[3] = -P;
        if (Q > 0) counts[1] = Q; else counts[4] = -Q;
        if (S > 0) counts[2] = S; else counts[5] = -S;

        // Порядок: на каждом шаге фрукт, после которого значения в пределах и ближе всего к центру
        var left = (int[])counts.Clone();
        int cr = from.R, cg = from.G, cb = from.B;
        var seq = new List<int>();
        var ok = true;
        while (left.Sum() > 0)
        {
            var pick = -1;
            var bestScore = double.MaxValue;
            for (var f = 0; f < 6; f++)
            {
                if (left[f] == 0) continue;
                int nr = cr + Fruits[f].R, ng = cg + Fruits[f].G, nb = cb + Fruits[f].B;
                if (nr is < 0 or > 255 || ng is < 0 or > 255 || nb is < 0 or > 255) continue;
                var score = Math.Abs(nr - 127.5) + Math.Abs(ng - 127.5) + Math.Abs(nb - 127.5) - left[f] * 0.01;
                if (score < bestScore) { bestScore = score; pick = f; }
            }
            if (pick < 0) { ok = false; break; }
            left[pick]--;
            cr += Fruits[pick].R; cg += Fruits[pick].G; cb += Fruits[pick].B;
            seq.Add(pick);
        }
        var steps = new List<(int, int)>();
        foreach (var f in seq)
            if (steps.Count > 0 && steps[^1].Item1 == f) steps[^1] = (f, steps[^1].Item2 + 1);
            else steps.Add((f, 1));
        int fr = from.R + 5 * (P - Q - S), fg = from.G + 5 * (-P + Q - S), fb = from.B + 5 * (-P - Q + S);
        plan = (counts, steps, Nearest(fr, fg, fb), ok);
    }

    private static void Swatch(Color c, float size)
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(pos, pos + new Vector2(size, size), ImGui.GetColorU32(new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, 1)), 4f);
        ImGui.Dummy(new Vector2(size, size));
    }

    private bool ColorCombo(string id, ref int idx)
    {
        var changed = false;
        ImGui.SetNextItemWidth(260 * Theme.S);
        if (ImGui.BeginCombo(id, $"{colors![idx].Name} ({colors[idx].R}/{colors[idx].G}/{colors[idx].B})"))
        {
            for (var i = 0; i < colors.Count; i++)
            {
                Swatch(colors[i], ImGui.GetTextLineHeight());
                ImGui.SameLine();
                if (ImGui.Selectable($"{colors[i].Name}##{id}{i}", i == idx)) { idx = i; changed = true; }
            }
            ImGui.EndCombo();
        }
        return changed;
    }

    private void DrawChocobo()
    {
        if (colors is null) BuildColors();
        if (colors!.Count == 0) return;
        Theme.Panel("chpick", () =>
        {
            Theme.SubHeader("Какой цвет сейчас и какой нужен");
            Swatch(colors[fromIdx], 22 * Theme.S);
            ImGui.SameLine();
            ColorCombo("Сейчас##chfrom", ref fromIdx);
            Swatch(colors[toIdx], 22 * Theme.S);
            ImGui.SameLine();
            ColorCombo("Нужен##chto", ref toIdx);
            ImGui.TextColored(Theme.Gray, "Новый чокобо — Desert Yellow. Han Lemon возвращает цвет к Desert Yellow.");
        });

        var from = colors[fromIdx];
        var to = colors[toIdx];
        var key = from.Id + ">" + to.Id;
        if (key != planKey) { planKey = key; Solve(from, to); }

        Theme.Panel("chplan", () =>
        {
            Theme.SubHeader("Чем кормить");
            if (from.Id == to.Id) { ImGui.TextColored(Theme.Green, "Чокобо уже этого цвета."); return; }
            if (plan is not { } pl) { ImGui.TextColored(Theme.Red, "Прямо из этого цвета не получается — сначала покорми Han Lemon (вернёт Desert Yellow) и выбери «Сейчас: Desert Yellow»."); return; }
            var total = pl.Counts.Sum();
            ImGui.TextColored(Theme.Text, $"Всего фруктов: {total}. Корми в этом порядке за одну сессию (6 часов после первого фрукта):");
            var n = 1;
            foreach (var (f, cnt) in pl.Steps)
            {
                var id = fruitIds[f];
                var have = id == 0 ? 0 : PlayerInfo.ItemCount(id) + RetainerTracker.CountOf(id).Total;
                if (id != 0) WikiView.ItemLine(id, cnt, $"шаг {n} · есть {have}", $"chs{n}");
                else ImGui.TextColored(Theme.Text, $"{n}. {Fruits[f].Ru} ×{cnt}");
                n++;
            }
            if (!pl.Ordered) ImGui.TextColored(Theme.Yellow, "Не удалось подобрать порядок без выхода за 0–255 — чередуй фрукты вручную.");
            ImGui.TextColored(Theme.Gray, $"Итог прилипнет к ближайшему цвету: {pl.Final.Name}. Ошибся — забери чокобо из стойла, кормление сбросится.");

            var need = pl.Counts.Select((c, i) => (Id: fruitIds[i], Need: c)).Where(x => x.Need > 0 && x.Id != 0).ToList();
            var lack = need.Select(x => (x.Id, Lack: x.Need - PlayerInfo.ItemCount(x.Id) - RetainerTracker.CountOf(x.Id).Total)).Where(x => x.Lack > 0).ToList();
            if (lack.Count > 0 && ImGui.SmallButton($"Недостающие фрукты в «Покупки» ({lack.Count})##chbuy"))
                foreach (var (id, l) in lack) ShoppingTab.Add(id, l);
        });
        Theme.Panel("chhelp", () =>
        {
            ImGui.PushTextWrapPos(0);
            ImGui.TextColored(Theme.Gray, "Кормят чокобо в стойле (Chocobo Stable) у дома или квартиры FC. Фрукты растут в саду или продаются на рынке. Каждый фрукт меняет R/G/B на 5; если значение уйдёт за 0–255, окраска не сработает.");
            ImGui.PopTextWrapPos();
        });
    }

    // ───────────── Садоводство ─────────────

    private sealed record Seed(string Name, uint Item, List<string> Crosses, string Botanist, string Other, string Use);

    private const string SeedsPage = "Types_of_Seeds";
    private List<Seed>? seeds;
    private string seedsFrom = "";
    private string seedSearch = "";

    private static List<Seed> ParseSeeds(string wiki)
    {
        var en = Plugin.Data.GetExcelSheet<Item>(Dalamud.Game.ClientLanguage.English);
        var byName = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in en)
        {
            var n = it.Name.ExtractText();
            if (n.Length > 0) byName.TryAdd(n, it.RowId);
        }
        var res = new List<Seed>();
        // Строки таблицы разделены «|-»; первая ячейка — {{item icon|Семя|size=big}}
        foreach (var raw in wiki.Split("\n|-"))
        {
            var end = raw.IndexOf("\n|}", StringComparison.Ordinal);
            var block = end >= 0 ? raw[..end] : raw;
            var m = Regex.Match(block, @"\{\{item icon\|([^|}]+)\|size=big\}\}(.*)", RegexOptions.Singleline);
            if (!m.Success) continue;
            var name = m.Groups[1].Value.Trim();
            var cells = m.Groups[2].Value.Split("||");
            string Cell(int i) => i < cells.Length ? WikiPage.Clean(Regex.Replace(cells[i], @"^\s*(align\s*=\s*""?\w+""?\s*)?\|", "")) : "";
            var crosses = Cell(1).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length > 0).ToList();
            res.Add(new Seed(name, byName.GetValueOrDefault(name), crosses, Cell(2).Replace("\n", " "), Cell(3).Replace("\n", " "), Cell(4).Replace("\n", " ")));
        }
        return res;
    }

    private void DrawGarden()
    {
        var text = WikiPage.Get(SeedsPage);
        Theme.Panel("gdtop", () =>
        {
            ImGui.PushTextWrapPos(0);
            ImGui.TextColored(Theme.Gray, "Скрещивание: посади два разных семени рядом — при сборе урожая получишь семена третьего вида. «A ↔ B» — работает в обе стороны; «A ← B» — растение A рядом с B. Лучшая почва — Grade 3 Thanalan Topsoil.");
            ImGui.PopTextWrapPos();
            var when = WikiPage.Fetched(SeedsPage);
            ImGui.TextColored(Theme.TextDim, when is null ? "Данные: Console Games Wiki." : $"Данные: Console Games Wiki, обновлено {when:dd.MM.yyyy}.");
            ImGui.SameLine();
            if (ImGui.SmallButton(WikiPage.IsLoading(SeedsPage) ? "Загружаю…##gdr" : "Обновить##gdr") && !WikiPage.IsLoading(SeedsPage)) WikiPage.Get(SeedsPage, true);
            if (WikiPage.Error(SeedsPage) is { } err) ImGui.TextColored(Theme.Red, $"Не удалось загрузить: {err}");
        });
        if (text is null)
        {
            if (WikiPage.IsLoading(SeedsPage)) Theme.Panel("gdload", () => ImGui.TextColored(Theme.TextDim, "Загружаю таблицу семян…"));
            return;
        }
        if (seedsFrom != text) { seedsFrom = text; seeds = ParseSeeds(text); }

        ImGui.SetNextItemWidth(Math.Min(420 * Theme.S, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##gdsearch", "Семя или растение…", ref seedSearch, 64);
        var list = seeds!.Where(s => seedSearch.Length == 0 || s.Name.Contains(seedSearch, StringComparison.OrdinalIgnoreCase)
                                     || NameTranslations.Show(s.Name).Contains(seedSearch, StringComparison.OrdinalIgnoreCase)
                                     || s.Crosses.Any(c => c.Contains(seedSearch, StringComparison.OrdinalIgnoreCase))).ToList();
        ImGui.TextColored(Theme.TextDim, $"Семян: {list.Count}. Поиск по семени покажет и те, где оно участвует в скрещивании.");

        if (!ImGui.BeginChild("##garden", Vector2.Zero)) { ImGui.EndChild(); return; }
        foreach (var s in list)
            Theme.Panel($"gd{s.Name}", () =>
            {
                if (s.Item != 0) WikiView.ItemLine(s.Item, 0, $"есть {PlayerInfo.ItemCount(s.Item) + RetainerTracker.CountOf(s.Item).Total}" + (s.Use.Length > 0 ? $" · {s.Use}" : ""), "gdi" + s.Name);
                else ImGui.TextColored(Theme.Gold, s.Name);
                ImGui.PushTextWrapPos(0);
                if (s.Crosses.Count > 0)
                {
                    ImGui.TextColored(Theme.TextDim, "Скрещивание:");
                    foreach (var c in s.Crosses) ImGui.TextColored(Theme.Text, "  " + c);
                }
                if (s.Botanist.Length > 0) ImGui.TextColored(Theme.TextDim, "Собрать ботаником: " + s.Botanist);
                if (s.Other.Length > 0) ImGui.TextColored(Theme.TextDim, "Другое: " + s.Other);
                ImGui.PopTextWrapPos();
            });
        ImGui.EndChild();
    }

    public IEnumerable<SearchHit> Search(string term, Action<MainTab> go)
    {
        if (seeds is null && WikiPage.Get(SeedsPage) is { } text) { seedsFrom = text; seeds = ParseSeeds(text); }
        if (seeds is null) yield break;
        foreach (var s in seeds.Where(s => s.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            yield return new SearchHit("Семена", s.Name, s.Crosses.Count > 0 ? "скрещивание: " + string.Join("; ", s.Crosses.Take(2)) : s.Botanist, s.Item != 0 ? WikiView.ItemInfo(s.Item).Icon : 0,
                () => { section = 1; seedSearch = s.Name; go(MainTab.ChocoboGarden); });
    }

    public void Draw()
    {
        Theme.Header("Чокобо и сад", "Чем кормить чокобо до нужного цвета и какие семена скрещивать");
        Theme.Chips("cgsec", Sections, ref section);
        if (!ImGui.BeginChild("##chocgarden", Vector2.Zero)) { ImGui.EndChild(); return; }
        if (section == 0) DrawChocobo(); else DrawGarden();
        ImGui.EndChild();
    }
}
