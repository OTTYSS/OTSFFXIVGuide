using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>PvP: ранг, Series Malmstones и награды сезона, статистика режимов.</summary>
internal sealed unsafe class PvPTab
{
    public void Draw()
    {
        Theme.Header("PvP", "Ранг, Series Malmstones с наградами сезона и статистика по режимам");
        var p = PvPProfile.Instance();
        if (!PlayerInfo.Ready || p == null || !p->IsLoaded)
        {
            Theme.EmptyState(FontAwesomeIcon.FistRaised, "Профиль PvP не загружен", "Открой в игре окно PvP Profile (меню PvP) — после этого здесь появятся ранг и награды.");
            return;
        }

        var seriesLvl = (int)p->SeriesCurrentRank;
        var claimed = (int)p->SeriesClaimedRank;
        var levels = Plugin.Data.GetExcelSheet<PvPSeriesLevel>();
        var need = levels.GetRowOrDefault((uint)seriesLvl)?.ExpToNext ?? 0;

        Theme.Grid("pvptop", 3, 300f, i =>
        {
            switch (i)
            {
                case 0:
                    Theme.Panel("pvprank", () =>
                    {
                        Theme.SubHeader("PvP-ранг");
                        Theme.LargeText(Theme.Gold, $"{p->GetPvPRank()}");
                        var cur = (int)p->GetPvPCurrentRankExperience();
                        var max = (int)p->GetPvPCurrentRankNeededExperience();
                        if (max > 0) Theme.Progress("До следующего ранга", cur, max, Theme.Blue);
                    });
                    break;
                case 1:
                    Theme.Panel("pvpseries", () =>
                    {
                        Theme.SubHeader($"Series Malmstones (сезон {p->Series})");
                        Theme.LargeText(Theme.Gold, $"Уровень {seriesLvl}");
                        if (need > 0) Theme.Progress("До следующего уровня", p->SeriesExperience, need, Theme.Gold);
                        if (claimed < seriesLvl) ImGui.TextColored(Theme.Blue, $"Не забрано наград: {seriesLvl - claimed} — забери в окне PvP Profile.");
                    });
                    break;
                default:
                    Theme.Panel("pvpcc", () =>
                    {
                        Theme.SubHeader($"Crystalline Conflict (сезон {p->CrystallineConflictSeason})");
                        ImGui.TextColored(Theme.Text, $"Рейтинговые: {p->CrystallineConflictRankedMatchesWon} побед из {p->CrystallineConflictRankedMatches}");
                        ImGui.TextColored(Theme.Text, $"Обычные: {p->CrystallineConflictCasualMatchesWon} побед из {p->CrystallineConflictCasualMatches}");
                        ImGui.TextColored(Theme.TextDim, $"Кредиты: {p->CrystallineConflictCurrentCrystalCredit} (лучшее {p->CrystallineConflictHighestCrystalCredit})");
                    });
                    break;
            }
        });

        Theme.Panel("pvpmodes", () =>
        {
            Theme.SubHeader("Другие режимы");
            ImGui.TextColored(Theme.Text, $"Frontline: всего {p->FrontlineTotalMatches} боёв, 1-е место {p->FrontlineTotalFirstPlace} раз; на этой неделе {p->FrontlineWeeklyMatches} боёв.");
            ImGui.TextColored(Theme.Text, $"Rival Wings: {p->RivalWingsTotalMatchesWon} побед из {p->RivalWingsTotalMatches}.");
            ImGui.PushTextWrapPos(0);
            ImGui.TextColored(Theme.Gray, "Быстрее всего уровень Series даёт Frontline (много опыта даже за поражение) и ежедневная рулетка Frontline. Crystalline Conflict короче, но даёт меньше.");
            ImGui.PopTextWrapPos();
        });

        var series = Plugin.Data.GetExcelSheet<PvPSeries>().GetRowOrDefault(p->Series);
        if (series is not { } s || s.LevelRewards.Count == 0) return;
        Theme.Panel("pvprewards", () =>
        {
            Theme.SubHeader("Награды сезона по уровням");
            if (!ImGui.BeginTable("##pvprw", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) return;
            ImGui.TableSetupColumn("Ур.", ImGuiTableColumnFlags.WidthFixed, 50 * Theme.S);
            ImGui.TableSetupColumn("Награда");
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 110 * Theme.S);
            for (var i = 0; i < s.LevelRewards.Count; i++)
            {
                var lr = s.LevelRewards[i];
                var items = lr.LevelRewardItem.Select((it, k) => (Id: it.RowId, Count: k < lr.LevelRewardCount.Count ? (int)lr.LevelRewardCount[k] : 1)).Where(x => x.Id != 0).ToList();
                if (items.Count == 0) continue;
                var lvl = i + 1;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(lvl <= seriesLvl ? Theme.Green : Theme.Text, $"{lvl}");
                ImGui.TableNextColumn();
                foreach (var (id, cnt) in items) WikiView.ItemLine(id, cnt > 1 ? cnt : 0, "", $"pvr{i}_{id}");
                ImGui.TableNextColumn();
                ImGui.TextColored(lvl <= claimed ? Theme.Green : lvl <= seriesLvl ? Theme.Blue : Theme.TextDim,
                    lvl <= claimed ? "забрано" : lvl <= seriesLvl ? "можно забрать" : "");
            }
            ImGui.EndTable();
        });
    }
}
