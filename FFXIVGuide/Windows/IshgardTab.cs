using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>
/// Ishgard Restoration и Diadem: что сдавать на твоём уровне (ремесленник или собиратель), сколько опыта и скрипов дают,
/// где собирать в Diadem и какая там погода (особые погоды дают редкие точки).
/// </summary>
internal sealed class IshgardTab
{
    private int mode;          // 0 — ремесленники, 1 — собиратели, 2 — Diadem
    private int craftJob = 8;  // CRP

    private sealed record Supply(string Item, uint ItemId, uint Icon, int Level, int LevelMax, int[] Ratings, (uint Exp, int Scrips)[] Rewards);
    private static Dictionary<int, List<Supply>>? crafter;

    private static Dictionary<int, List<Supply>> Crafter()
    {
        if (crafter is not null) return crafter;
        crafter = new();
        var rew = Plugin.Data.GetExcelSheet<HWDCrafterSupplyReward>();
        (uint, int) R(uint id) => rew.GetRowOrDefault(id) is { } r ? (r.ExpReward, r.ScriptRewardAmount) : (0, 0);
        foreach (var row in Plugin.Data.GetExcelSheet<HWDCrafterSupply>())
        {
            if (row.RowId == 0) continue;
            var list = new List<Supply>();
            foreach (var p in row.HWDCrafterSupplyParams)
            {
                if (p.ItemTradeIn.RowId == 0 || p.ItemTradeIn.ValueNullable is not { } it) continue;
                // После завершения восстановления действуют награды «PostPhase»
                list.Add(new Supply(it.Name.ExtractText(), it.RowId, it.Icon, p.Level, p.LevelMax,
                    new[] { (int)p.BaseCollectableRating, (int)p.MidCollectableRating, (int)p.HighCollectableRating },
                    new[] { R(p.BaseCollectableRewardPostPhase.RowId), R(p.MidCollectableRewardPostPhase.RowId), R(p.HighCollectableRewardPostPhase.RowId) }));
            }
            crafter[(int)row.RowId + 7] = list.GroupBy(s => s.ItemId).Select(g => g.First()).OrderBy(s => s.Level).ToList();
        }
        return crafter;
    }

    public void Draw()
    {
        Theme.Header("Ishgard Restoration и Diadem", "Что сдавать на твоём уровне, сколько опыта и скрипов дают, где собирать в Diadem и какая там погода");
        Theme.Chips("ishmode", new[] { "Ремесленники", "Собиратели", "Diadem" }, ref mode);
        Theme.Panel("ishhow", () =>
        {
            ImGui.TextColored(Theme.TextDim, "Открывается квестом «Towards the Firmament» в Foundation (Ishgard), нужен 20+ уровень ремесла или сбора и Heavensward. " +
                                             "Предметы для сдачи — коллекционные: крафти их через «Collectable Synthesis» и сдавай у Potkin в Firmament. " +
                                             "Чем выше коллекционность (порог 1/2/3), тем больше опыта и Skybuilders' Scrips.");
        });
        switch (mode)
        {
            case 0: DrawCrafter(); break;
            case 1: DrawGatherer(); break;
            default: DrawDiadem(); break;
        }
    }

    private void DrawCrafter()
    {
        var cj = Plugin.Data.GetExcelSheet<ClassJob>();
        for (var j = 8; j <= 15; j++)
        {
            if (j > 8) ImGui.SameLine();
            var pos = ImGui.GetCursorScreenPos();
            var size = 34 * Theme.S;
            if (ImGui.InvisibleButton($"##ishj{j}", new Vector2(size))) craftJob = j;
            if (craftJob == j) ImGui.GetWindowDrawList().AddRectFilled(pos - new Vector2(2 * Theme.S), pos + new Vector2(size + 2 * Theme.S), Theme.U32(Theme.Alpha(Theme.Gold, 0.3f)), 6f * Theme.S);
            Theme.GameIcon(62100u + (uint)j, pos, size, 6f);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(GuideData.Capitalize(cj.GetRow((uint)j).Name.ExtractText()));
        }
        var level = PlayerInfo.Ready ? Plugin.PlayerState.GetClassJobLevel(cj.GetRow((uint)craftJob)) : (short)0;
        if (!Crafter().TryGetValue(craftJob, out var list)) { ImGui.TextColored(Theme.TextDim, "Нет данных."); return; }
        Theme.Panel("ishcr", () =>
        {
            ImGui.TextColored(Theme.Text, $"{GuideData.Capitalize(cj.GetRow((uint)craftJob).Name.ExtractText())}: уровень {level}");
            if (!ImGui.BeginTable("##ishct", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp)) return;
            ImGui.TableSetupColumn("Предмет", ImGuiTableColumnFlags.None, 2.2f);
            ImGui.TableSetupColumn("Уровни", ImGuiTableColumnFlags.None, 0.8f);
            ImGui.TableSetupColumn("Порог 1", ImGuiTableColumnFlags.None, 1.3f);
            ImGui.TableSetupColumn("Порог 2", ImGuiTableColumnFlags.None, 1.3f);
            ImGui.TableSetupColumn("Порог 3", ImGuiTableColumnFlags.None, 1.3f);
            ImGui.TableHeadersRow();
            foreach (var s in list)
            {
                var mine = level >= s.Level && level <= s.LevelMax;
                ImGui.TableNextRow();
                if (mine) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, Theme.U32(Theme.Alpha(Theme.Gold, 0.14f)));
                ImGui.TableNextColumn();
                if (ImGui.Selectable($"{s.Item}##ish{s.ItemId}")) WikiNav.Open(WikiKind.Item, s.ItemId, s.Item);
                ImGui.TableNextColumn();
                ImGui.TextColored(mine ? Theme.Gold : Theme.TextDim, $"{s.Level}–{s.LevelMax}");
                for (var t = 0; t < 3; t++)
                {
                    ImGui.TableNextColumn();
                    ImGui.TextColored(Theme.TextDim, $"≥{s.Ratings[t]}: {s.Rewards[t].Exp:N0} оп., {s.Rewards[t].Scrips} скр.");
                }
            }
            ImGui.EndTable();
            ImGui.TextColored(Theme.Gray, "Подсвечен предмет для твоего уровня. Опыт — за одну сдачу; выше коллекционность — больше награда.");
        });
    }

    private static void DrawGatherer()
    {
        var gi = Plugin.Data.GetExcelSheet<HWDGathererInspection>();
        var items = Plugin.Data.GetExcelSheet<Item>();
        var rew = Plugin.Data.GetExcelSheet<HWDGathererInspectionReward>();
        Theme.Panel("ishg", () =>
        {
            ImGui.TextColored(Theme.TextDim, "Собиратели сдают добытое в Diadem на проверку (Inspection) — за это дают скрипы и очки восстановления, а опыт идёт за сам сбор в Diadem.");
            if (!ImGui.BeginTable("##ishgt", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp)) return;
            ImGui.TableSetupColumn("Что собрать", ImGuiTableColumnFlags.None, 2f);
            ImGui.TableSetupColumn("Сколько", ImGuiTableColumnFlags.None, 0.6f);
            ImGui.TableSetupColumn("Что получишь", ImGuiTableColumnFlags.None, 2f);
            ImGui.TableSetupColumn("Награда", ImGuiTableColumnFlags.None, 1f);
            ImGui.TableHeadersRow();
            var seen = new HashSet<uint>();
            foreach (var row in gi)
                foreach (var d in row.HWDGathererInspectionData)
                {
                    var need = d.RequiredItem.ValueNullable?.Item.RowId ?? 0;
                    if (need == 0 || d.ItemReceived.RowId == 0 || !seen.Add(need)) continue;
                    var name = items.GetRowOrDefault(need)?.Name.ExtractText() ?? "?";
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    if (ImGui.Selectable($"{name}##ishgi{need}")) WikiNav.Open(WikiKind.Item, need, name);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(d.AmountRequired.ToString());
                    ImGui.TableNextColumn();
                    ImGui.TextColored(Theme.TextDim, d.ItemReceived.ValueNullable?.Name.ExtractText() ?? "");
                    ImGui.TableNextColumn();
                    var r = d.Reward.Count > 0 ? rew.GetRowOrDefault(d.Reward[0].RowId) : null;
                    ImGui.TextColored(Theme.TextDim, r is { } rr ? $"{rr.Scrips} скр." : "—");
                }
            ImGui.EndTable();
        });
    }

    private const uint DiademTerritory = 939;

    private static void DrawDiadem()
    {
        var tt = Plugin.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(DiademTerritory);
        var rate = tt?.WeatherRate.RowId ?? 0;
        Theme.Panel("ishw", () =>
        {
            Theme.SubHeader("Погода в Diadem");
            ImGui.TextColored(Theme.TextDim, "Особые погоды (Umbral Flare, Umbral Duststorm, Umbral Levin, Umbral Tempest) открывают редкие точки сбора и дают больше очков.");
            if (rate == 0) return;
            var now = EorzeaWeather.Now;
            var start = EorzeaWeather.PeriodStart(now);
            for (var i = 0; i < 6; i++)
            {
                var t = start + i * 1400L;
                var w = EorzeaWeather.WeatherAt(rate, t);
                var name = EorzeaWeather.WeatherName(w);
                var special = name.StartsWith("Umbral");
                Theme.GameIcon(EorzeaWeather.WeatherIcon(w), ImGui.GetCursorScreenPos(), 22 * Theme.S, 4f);
                ImGui.Dummy(new Vector2(26 * Theme.S, 22 * Theme.S));
                ImGui.SameLine();
                ImGui.TextColored(special ? Theme.Gold : Theme.Text, name);
                ImGui.SameLine();
                ImGui.TextColored(Theme.TextDim, i == 0 ? "сейчас" : $"через {EorzeaWeather.Until(t)}");
            }
        });

        var items = Plugin.Data.GetExcelSheet<Item>();
        var spots = GatherIndex.All
            .SelectMany(kv => kv.Value.Where(s => s.Territory == DiademTerritory).Select(s => (Item: kv.Key, Spot: s)))
            .GroupBy(x => (x.Spot.Type, x.Spot.Level, x.Spot.Place))
            .OrderBy(g => g.Key.Type).ThenBy(g => g.Key.Level).ToList();
        Theme.Panel("ishsp", () =>
        {
            Theme.SubHeader("Точки сбора в Diadem");
            if (spots.Count == 0) { ImGui.TextColored(Theme.TextDim, "В данных игры не нашлось точек Diadem."); return; }
            var n = 0;
            foreach (var g in spots)
            {
                var sp = g.First().Spot;
                ImGui.TextColored(Theme.Gold, $"{g.Key.Type} ур. {g.Key.Level}");
                ImGui.SameLine();
                ImGui.TextUnformatted($"{g.Key.Place} ({sp.X:0.0}, {sp.Y:0.0})");
                ImGui.SameLine();
                if (ImGui.SmallButton($"Флаг##dia{n++}")) GatherIndex.Flag(sp);
                ImGui.TextColored(Theme.TextDim, "   " + string.Join(", ", g.Select(x => items.GetRowOrDefault(x.Item)?.Name.ExtractText() ?? "").Where(s => s.Length > 0).Distinct().Take(6)));
            }
        });
    }
}
