using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Сбор: где добыть материал и что выгодно собирать на продажу.</summary>
internal sealed class GatherTab
{
    private sealed record Row(uint Id, string Name, uint Icon, bool Marketable, List<GatherSpot> Spots);

    private readonly MarketService market;
    private List<Row>? rows;
    private string search = "";
    private Row? selected;
    private List<Row>? profitCandidates;

    public GatherTab(MarketService market) => this.market = market;

    public void Draw()
    {
        rows ??= Build();
        Theme.Header("Сбор", "Где добыть материал (Mining, Logging, Harvesting, Quarrying) и что выгодно собирать");

        var region = MarketService.CurrentRegion(true);
        Theme.Search("##gsearch", "Название материала…", ref search);

        var avail = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##glist", new Vector2(avail.X * 0.45f, 0)))
        {
            var list = search.Trim().Length < 2
                ? new List<Row>()
                : rows.Where(r => r.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.Name.Length).Take(200).ToList();
            if (search.Trim().Length < 2)
                ImGui.TextColored(Theme.TextDim, $"Введи название. В базе {rows.Count} собираемых предметов.");

            var rowH = 42 * Theme.S;
            Theme.VirtualList(list.Count, rowH, i =>
            {
                var r = list[i];
                var first = r.Spots[0];
                if (Theme.Card($"##gr{r.Id}", r.Icon, r.Name, $"{first.Type} ур. {first.Level} · {first.Zone}", null, Theme.Gray, selected == r, rowH))
                    selected = r;
            });
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("##gdetails", Vector2.Zero))
        {
            if (selected is not null) DrawDetails(selected, region);
            DrawProfitable(region);
        }
        ImGui.EndChild();
    }

    private void DrawDetails(Row r, string? region)
    {
        if (region is not null && r.Marketable) market.Request(region, [r.Id]);
        Theme.Panel($"gd{r.Id}", () =>
        {
            Theme.LargeText(Theme.Gold, r.Name);
            var have = PlayerInfo.ItemCount(r.Id);
            ImGui.TextColored(have > 0 ? Theme.Green : Theme.TextDim, $"У тебя: {have}");
            if (region is not null && r.Marketable && market.Get(region, r.Id) is { } p)
            {
                ImGui.SameLine();
                Theme.InlineBadge($"рынок: {p.Price:N0} гил/шт", Theme.Gold);
            }

            Theme.SubHeader("Где собирать");
            foreach (var s in r.Spots.Take(12))
            {
                ImGui.PushID($"{s.Territory}{s.Place}{s.Level}");
                Theme.FaIcon(s.Timed ? FontAwesomeIcon.Star : FontAwesomeIcon.MapMarkerAlt, s.Timed ? Theme.Gold : Theme.GoldDim);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Text, $"{s.Type} ур. {s.Level} — {s.Zone}" + (s.Place.Length > 0 ? $", {s.Place}" : ""));
                if (s.Timed) { ImGui.SameLine(); Theme.InlineBadge("по таймеру", Theme.Gold); }
                ImGui.SameLine();
                if (ImGui.SmallButton("Флаг")) GatherIndex.Flag(s);
                ImGui.PopID();
            }
            ImGui.TextColored(Theme.Gray, "Точки «по таймеру» появляются только в определённое время по эорзейским часам.");
        });
    }

    private void DrawProfitable(string? region)
    {
        Theme.Panel("gprofit", () =>
        {
            Theme.SubHeader("Выгодно собирать на твоём мире");
            var levels = GathererLevels();
            if (region is null || levels.Values.All(v => v == 0))
            {
                ImGui.TextColored(Theme.TextDim, "Нужен персонаж с открытым Miner, Botanist или Fisher.");
                return;
            }
            ImGui.TextColored(Theme.TextDim, string.Join(" · ", levels.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key} {kv.Value}")));

            if (Theme.IconButton("gfind", FontAwesomeIcon.Coins, "Найти"))
            {
                profitCandidates = rows!
                    .Where(r => r.Marketable && r.Spots.Any(s => CanGather(s, levels)))
                    .OrderByDescending(r => r.Spots.Where(s => CanGather(s, levels)).Max(s => s.Level))
                    .Take(150)
                    .ToList();
                market.Request(region, profitCandidates.Select(r => r.Id));
            }
            ImGui.SameLine();
            ImGui.TextColored(Theme.Gray, "До 150 материалов твоего уровня сбора");

            if (profitCandidates is null) return;
            var priced = profitCandidates
                .Select(r => (r, p: market.Get(region, r.Id)))
                .Where(x => x.p is not null)
                .OrderByDescending(x => x.p!.Price)
                .Take(20)
                .ToList();
            if (priced.Count == 0) ImGui.TextColored(Theme.TextDim, "Загружаю цены…");
            foreach (var (r, p) in priced)
            {
                var s = r.Spots.First(x => CanGather(x, levels));
                if (Theme.Card($"##gp{r.Id}", r.Icon, r.Name, $"{s.Type} ур. {s.Level} · {s.Zone}" + (s.Timed ? " · по таймеру" : ""),
                        $"{p!.Price:N0} гил", Theme.Gold, selected == r, 42 * Theme.S))
                    selected = r;
            }
            ImGui.TextColored(Theme.Gray, "Цена за штуку, самый дешёвый лот. Смотри и количество лотов на Universalis.");
        });
    }

    private static bool CanGather(GatherSpot s, Dictionary<string, int> levels)
    {
        var lvl = s.Type.Contains("Mining", StringComparison.OrdinalIgnoreCase) || s.Type.Contains("Quarrying", StringComparison.OrdinalIgnoreCase)
            ? levels.GetValueOrDefault("Miner")
            : s.Type.Contains("Logging", StringComparison.OrdinalIgnoreCase) || s.Type.Contains("Harvesting", StringComparison.OrdinalIgnoreCase)
                ? levels.GetValueOrDefault("Botanist")
                : 0;
        return lvl > 0 && s.Level <= lvl;
    }

    private static Dictionary<string, int> GathererLevels()
    {
        var d = new Dictionary<string, int>();
        if (!PlayerInfo.Ready) return d;
        var jobs = Plugin.Data.GetExcelSheet<ClassJob>();
        foreach (var (id, name) in new[] { (16u, "Miner"), (17u, "Botanist"), (18u, "Fisher") })
            d[name] = jobs.GetRowOrDefault(id) is { } j ? Plugin.PlayerState.GetClassJobLevel(j) : 0;
        return d;
    }

    private static List<Row> Build()
    {
        var items = Plugin.Data.GetExcelSheet<Item>();
        var list = new List<Row>();
        foreach (var (id, spots) in GatherIndex.All)
        {
            var it = items.GetRowOrDefault(id);
            if (it is null) continue;
            var name = it.Value.Name.ExtractText();
            if (name.Length == 0) continue;
            list.Add(new Row(id, name, it.Value.Icon, it.Value.ItemSearchCategory.RowId != 0 && !it.Value.IsUntradable, spots));
        }
        return list;
    }
}
