using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Действия свободной компании: какие бафы FC сейчас на тебе и сколько им осталось, ранг FC и что можно купить.</summary>
internal sealed class FcActionsTab
{
    public unsafe void Draw()
    {
        Theme.Header("Действия свободной компании", "Активные бафы FC и сколько им осталось, ранг FC и все действия, которые можно купить за кредиты компании");
        if (!PlayerInfo.Ready) { Theme.NeedLogin("Зайди в игру персонажем: бафы и данные FC берутся из игры."); return; }

        var fc = InfoProxyFreeCompany.Instance();
        var fcRank = fc != null ? fc->Rank : (byte)0;
        Theme.Panel("fcinfo", () =>
        {
            if (fc == null || fc->Id == 0) { ImGui.TextColored(Theme.TextDim, "Свободная компания не найдена (или данные ещё не пришли — открой окно FC в игре)."); return; }
            Theme.LargeText(Theme.Gold, $"{fc->NameString} · ранг {fc->Rank}");
            ImGui.TextColored(Theme.TextDim, $"Участников онлайн: {fc->OnlineMembers} из {fc->TotalMembers}. Кредиты компании видны в окне FC → Company Actions.");
        });

        var actions = Plugin.Data.GetExcelSheet<CompanyAction>().Where(a => a.RowId > 0 && !a.Name.IsEmpty).ToList();
        var names = actions.Select(a => a.Name.ExtractText()).ToHashSet();
        var active = new List<(string Name, float Left, uint Icon)>();
        var statuses = Plugin.Data.GetExcelSheet<Status>();
        if (Plugin.Objects.LocalPlayer is { } me)
            foreach (var s in me.StatusList)
                if (s.StatusId != 0 && statuses.GetRowOrDefault(s.StatusId) is { } st && names.Contains(st.Name.ExtractText()))
                    active.Add((st.Name.ExtractText(), s.RemainingTime, st.Icon));

        Theme.Panel("fcactive", () =>
        {
            Theme.SubHeader($"Активно сейчас: {active.Count} из 2");
            if (active.Count == 0) ImGui.TextColored(Theme.TextDim, "На тебе нет бафов FC. Одновременно действуют максимум два.");
            foreach (var (name, left, icon) in active)
            {
                Theme.GameIcon(icon, ImGui.GetCursorScreenPos(), 24 * Theme.S, 4f);
                ImGui.Dummy(new System.Numerics.Vector2(28 * Theme.S, 24 * Theme.S));
                ImGui.SameLine();
                ImGui.TextColored(Theme.Green, name);
                ImGui.SameLine();
                var t = TimeSpan.FromSeconds(left);
                ImGui.TextColored(left < 3600 ? Theme.Yellow : Theme.TextDim, left > 0 ? $"ещё {(int)t.TotalHours} ч {t.Minutes} мин" : "");
            }
        });

        Theme.Panel("fclist", () =>
        {
            Theme.SubHeader("Все действия FC");
            ImGui.TextColored(Theme.TextDim, "Покупаются за кредиты компании у OIC Quartermaster в штабе своей Grand Company, включаются в окне FC. Длительность — 24 часа (кроме особых).");
            if (!ImGui.BeginTable("##fca", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp)) return;
            ImGui.TableSetupColumn("Действие", ImGuiTableColumnFlags.None, 1.4f);
            ImGui.TableSetupColumn("Что даёт", ImGuiTableColumnFlags.None, 3f);
            ImGui.TableSetupColumn("Ранг FC", ImGuiTableColumnFlags.None, 0.6f);
            ImGui.TableSetupColumn("Цена", ImGuiTableColumnFlags.None, 0.8f);
            ImGui.TableHeadersRow();
            foreach (var g in actions.GroupBy(a => a.Name.ExtractText()))
            {
                var a = g.First();
                var on = active.Any(x => x.Name == g.Key);
                var rankNeed = a.FCRank.RowId;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                Theme.GameIcon((uint)a.Icon, ImGui.GetCursorScreenPos(), 20 * Theme.S, 4f);
                ImGui.Dummy(new System.Numerics.Vector2(24 * Theme.S, 20 * Theme.S));
                ImGui.SameLine();
                ImGui.TextColored(on ? Theme.Green : Theme.Text, g.Key + (on ? " ✓" : ""));
                ImGui.TableNextColumn();
                ImGui.TextWrapped(a.Description.ExtractText().Split('\n')[0]);
                ImGui.TableNextColumn();
                ImGui.TextColored(fcRank > 0 && fcRank < rankNeed ? Theme.Red : Theme.TextDim, rankNeed.ToString());
                ImGui.TableNextColumn();
                ImGui.TextColored(Theme.TextDim, string.Join(" / ", g.Select(x => x.Cost).Distinct().OrderBy(x => x).Select(x => x.ToString("N0"))));
            }
            ImGui.EndTable();
        });
    }
}
