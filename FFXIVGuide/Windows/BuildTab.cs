using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Материя, еда и зелья под текущую профессию.</summary>
internal sealed unsafe class BuildTab
{
    private static readonly string[] Parts = { "Материя", "Еда", "Зелья" };

    private readonly MarketService market;
    private int part;
    private (uint Job, List<(uint Id, string Name, uint Icon, uint Ilvl, string Stats)> Food)? foodCache;

    public BuildTab(MarketService market) => this.market = market;

    /// <summary>Приоритет вторичных статов по роли (общепринятый порядок).</summary>
    public static uint[] Priority(ClassJob job) => job.RowId switch
    {
        >= 8 and <= 15 => new uint[] { 70, 71, 11 },       // крафтеры: Craftsmanship, Control, CP
        >= 16 and <= 18 => new uint[] { 72, 73, 10 },      // собиратели: Gathering, Perception, GP
        _ => job.Role switch
        {
            1 => new uint[] { 27, 44, 22, 19 },            // танк: Crit, Det, DH, Tenacity
            4 => new uint[] { 27, 44, 46, 6 },             // хил: Crit, Det, Spell Speed, Piety
            _ when job.PrimaryStat == 4 => new uint[] { 27, 22, 44, 46 }, // маги
            _ => new uint[] { 27, 22, 44, 45 },            // физ. урон: Crit, DH, Det, Skill Speed
        },
    };

    private static string StatName(uint id) => GuideData.StatNames.TryGetValue(id, out var n) ? n : $"#{id}";

    public void Draw()
    {
        Theme.Header("Материя, еда и зелья", "Что вставлять, есть и пить на текущей профессии");
        if (!PlayerInfo.Ready || Plugin.PlayerState.ClassJob.ValueNullable is not { } job)
        {
            Theme.Panel("bnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем."));
            return;
        }

        var prio = Priority(job);
        ImGui.TextColored(Theme.Text, $"{GuideData.Capitalize(job.Name.ExtractText())}: приоритет статов — {string.Join(" > ", prio.Select(StatName))}");
        ImGui.TextColored(Theme.Gray, "Общий ориентир. Скорость (Speed) — по вкусу, Piety хилу — только если не хватает MP.");
        Theme.Chips("bpart", Parts, ref part);

        var region = MarketService.CurrentRegion(true);
        if (!ImGui.BeginChild("##build", Vector2.Zero))
        {
            ImGui.EndChild();
            return;
        }

        switch (part)
        {
            case 0: DrawMateria(prio, region); break;
            case 1: DrawFood(job, prio, region); break;
            default: DrawPotions(job, region); break;
        }

        ImGui.EndChild();
    }

    // ───────────── Материя ─────────────

    private void DrawMateria(uint[] prio, string? region)
    {
        var materia = Plugin.Data.GetExcelSheet<Materia>();
        var items = Plugin.Data.GetExcelSheet<Item>();

        Theme.Panel("bmrec", () =>
        {
            Theme.SubHeader("Какую материю вставлять");
            foreach (var stat in prio)
            {
                var row = materia.FirstOrDefault(m => m.BaseParam.RowId == stat);
                if (row.RowId == 0 && row.BaseParam.RowId != stat) continue;
                // две старшие доступные ступени
                var grades = Enumerable.Range(0, row.Item.Count)
                    .Where(i => row.Item[i].RowId != 0 && !(row.Item[i].ValueNullable?.Name.IsEmpty ?? true))
                    .Reverse().Take(2).ToList();
                foreach (var g in grades)
                {
                    var it = row.Item[g].Value;
                    if (region is not null) market.Request(region, [it.RowId]);
                    var p = region is null ? null : market.Get(region, it.RowId);
                    var pos = ImGui.GetCursorScreenPos();
                    Theme.GameIcon(it.Icon, pos, ImGui.GetTextLineHeight() * 1.3f, 3f);
                    ImGui.Dummy(new Vector2(ImGui.GetTextLineHeight() * 1.3f));
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.Text, $"{it.Name.ExtractText()} (+{row.Value[g]} {StatName(stat)})");
                    if (p is not null) { ImGui.SameLine(); Theme.InlineBadge($"{p.Price:N0} гил", Theme.Gold); }
                }
            }
            ImGui.TextColored(Theme.Gray, "Старшая ступень лучше, но в «переплавленные» слоты (overmeld) шанс ниже — туда ставят ступень попроще.");
        });

        Theme.Panel("bmeq", () =>
        {
            Theme.SubHeader("Что сейчас вставлено");
            var im = InventoryManager.Instance();
            var eq = im == null ? null : im->GetInventoryContainer(InventoryType.EquippedItems);
            if (eq == null) return;
            for (var i = 0; i < eq->Size; i++)
            {
                var s = eq->GetInventorySlot(i);
                if (s == null || s->GetBaseItemId() == 0) continue;
                var row = items.GetRowOrDefault(s->GetBaseItemId());
                if (row is null || row.Value.MateriaSlotCount == 0 && !row.Value.IsAdvancedMeldingPermitted) continue;

                var melds = new List<string>();
                for (byte m = 0; m < 5; m++)
                {
                    var mid = s->GetMateriaId(m);
                    if (mid == 0) continue;
                    var mrow = materia.GetRowOrDefault(mid);
                    var grade = s->GetMateriaGrade(m);
                    if (mrow is null || grade >= mrow.Value.Item.Count) continue;
                    var stat = mrow.Value.BaseParam.RowId;
                    melds.Add($"{StatName(stat)} +{mrow.Value.Value[grade]}" + (prio.Contains(stat) ? "" : " ⚠"));
                }

                ImGui.TextColored(Theme.Text, row.Value.Name.ExtractText());
                ImGui.SameLine();
                ImGui.TextColored(Theme.TextDim, $"слотов {row.Value.MateriaSlotCount}" + (row.Value.IsAdvancedMeldingPermitted ? " (+overmeld)" : ""));
                ImGui.SameLine();
                ImGui.TextColored(melds.Count == 0 ? Theme.Yellow : Theme.Green, melds.Count == 0 ? "пусто" : string.Join(", ", melds));
            }
            ImGui.TextColored(Theme.Gray, "⚠ — стат не из приоритета твоей профессии.");
        });
    }

    // ───────────── Еда ─────────────

    private void DrawFood(ClassJob job, uint[] prio, string? region)
    {
        if (foodCache?.Job != job.RowId)
            foodCache = (job.RowId, BuildFood(prio));
        var food = foodCache.Value.Food;
        if (region is not null) market.Request(region, food.Select(f => f.Id));

        Theme.Panel("bfood", () =>
        {
            Theme.SubHeader("Лучшая еда под твои статы");
            if (food.Count == 0) ImGui.TextColored(Theme.TextDim, "Не нашлось еды с нужными статами.");
            foreach (var f in food)
            {
                var p = region is null ? null : market.Get(region, f.Id);
                var have = PlayerInfo.ItemCount(f.Id);
                Theme.Card($"##bf{f.Id}", f.Icon, f.Name, $"iLvl {f.Ilvl} · {f.Stats}",
                    p is not null ? $"{p.Price:N0} гил" : have > 0 ? $"есть ×{have}" : null, p is not null ? Theme.Gold : Theme.Green, false, 42 * Theme.S);
            }
            ImGui.TextColored(Theme.Gray, "Бонус еды ограничен потолком (cap) — на низком iLvl дорогая еда даёт столько же, сколько дешёвая.");
        });
    }

    private static List<(uint, string, uint, uint, string)> BuildFood(uint[] prio)
    {
        var foodSheet = Plugin.Data.GetExcelSheet<ItemFood>();
        var list = new List<(uint, string, uint, uint, string, int)>();
        foreach (var it in Plugin.Data.GetExcelSheet<Item>())
        {
            var act = it.ItemAction.ValueNullable;
            if (act is null || act.Value.Data.Count < 2 || it.Name.IsEmpty) continue;
            if (act.Value.Action.RowId is not (844 or 845)) continue; // еда
            var food = foodSheet.GetRowOrDefault(act.Value.Data[1]);
            if (food is null) continue;

            var stats = new List<string>();
            var score = 0;
            foreach (var p in food.Value.Params)
            {
                var id = p.BaseParam.RowId;
                if (id == 0) continue;
                stats.Add($"{StatName(id)} {p.ValueHQ}%≤{p.MaxHQ}");
                var idx = Array.IndexOf(prio, id);
                if (idx >= 0) score += (prio.Length - idx) * p.MaxHQ;
            }
            if (score == 0) continue;
            list.Add((it.RowId, it.Name.ExtractText(), it.Icon, it.LevelItem.RowId, string.Join(", ", stats), score));
        }
        return list.OrderByDescending(x => x.Item4).ThenByDescending(x => x.Item6).Take(12)
            .Select(x => (x.Item1, x.Item2, x.Item3, x.Item4, x.Item5)).ToList();
    }

    // ───────────── Зелья ─────────────

    private void DrawPotions(ClassJob job, string? region)
    {
        var statWord = job.PrimaryStat switch { 1 => "Strength", 2 => "Dexterity", 4 => "Intelligence", 5 => "Mind", _ => "" };
        // Боевые зелья: действие предмета 846, в параметрах — основной стат профессии
        var foodSheet = Plugin.Data.GetExcelSheet<ItemFood>();
        var potions = Plugin.Data.GetExcelSheet<Item>()
            .Where(it => !it.Name.IsEmpty && statWord.Length > 0 && it.ItemAction.ValueNullable is { } a
                         && a.Action.RowId == 846 && a.Data.Count > 1
                         && foodSheet.GetRowOrDefault(a.Data[1]) is { } f && f.Params.Any(p => p.BaseParam.RowId == job.PrimaryStat))
            .OrderByDescending(it => it.LevelItem.RowId)
            .Take(6)
            .ToList();
        if (region is not null) market.Request(region, potions.Select(p => p.RowId));

        Theme.Panel("bpot", () =>
        {
            Theme.SubHeader($"Зелья на основной стат ({StatName(job.PrimaryStat)})");
            if (potions.Count == 0) ImGui.TextColored(Theme.TextDim, "Для этой профессии боевые зелья не нужны.");
            foreach (var it in potions)
            {
                var p = region is null ? null : market.Get(region, it.RowId);
                Theme.Card($"##bp{it.RowId}", it.Icon, it.Name.ExtractText(), $"iLvl {it.LevelItem.RowId}",
                    p is not null ? $"{p.Price:N0} гил" : null, Theme.Gold, false, 42 * Theme.S);
            }
            ImGui.TextColored(Theme.Gray, "Зелья пьют в сложном контенте (Savage, Extreme) под рейдовые баффы. В обычных данжах не нужны.");
        });
    }
}
