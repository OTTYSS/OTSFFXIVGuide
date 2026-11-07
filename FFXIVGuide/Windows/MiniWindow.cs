using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

/// <summary>
/// Маленькая панель поверх игры: текущий сюжетный квест, дейлики, марки, FATE.
/// </summary>
public sealed class MiniWindow : Window
{
    private readonly GuideData data;
    private readonly HuntTracker hunts;
    private readonly Action<MainTab> open;
    private DateTime nextRefresh = DateTime.MinValue;
    private QuestEntry? msq;

    public MiniWindow(GuideData data, HuntTracker hunts, Action<MainTab> open)
        : base("FFXIV Guide · кратко###FFXIVGuideMini", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar)
    {
        this.data = data;
        this.hunts = hunts;
        this.open = open;
        RespectCloseHotkey = false;
    }

    public override void PreDraw() => Theme.Push();
    public override void PostDraw() => Theme.Pop();

    public override void OnClose()
    {
        Plugin.Config.MiniWindow = false;
        Plugin.Config.Save();
    }

    public override void Draw()
    {
        if (!PlayerInfo.Ready)
        {
            ImGui.TextColored(Theme.TextDim, "Персонаж не загружен");
            return;
        }

        if (DateTime.Now >= nextRefresh)
        {
            msq = FindMsq();
            nextRefresh = DateTime.Now.AddSeconds(3);
        }

        var blocks = Plugin.Config.MiniBlocks;

        // Сюжет
        if (blocks.Contains("story")) {
        Theme.FaIcon(FontAwesomeIcon.Book, Theme.Gold);
        ImGui.SameLine();
        if (msq is null)
        {
            ImGui.TextColored(Theme.TextDim, "Сюжет: нет доступного квеста");
        }
        else
        {
            if (ImGui.Selectable($"{msq.Name}##mini_msq", false, ImGuiSelectableFlags.None, new Vector2(ImGui.CalcTextSize(msq.Name).X, 0)))
                open(MainTab.Story);
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextDim, $"ур. {msq.Level}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Флаг##mini")) PlayerInfo.FlagQuestGiver(msq);
        }

        }

        // Дейлики
        if (blocks.Contains("daily")) {
        var openDaily = Checklist.Items.Count(i => i.Period == Period.Daily && !Checklist.StateOf(i).Done);
        var openWeekly = Checklist.Items.Count(i => i.Period == Period.Weekly && !Checklist.StateOf(i).Done);
        var left = Checklist.NextReset(Period.Daily, DateTime.UtcNow) - DateTime.UtcNow;
        Theme.FaIcon(FontAwesomeIcon.CheckCircle, openDaily == 0 ? Theme.Green : Theme.Yellow);
        ImGui.SameLine();
        if (ImGui.Selectable($"Дейлики: {openDaily} · викли: {openWeekly} · сброс через {left.Hours}ч {left.Minutes}м##mini_check"))
            open(MainTab.Checklist);

        }

        // FATE
        if (blocks.Contains("fate")) {
        var fates = Plugin.Fates.Count(f => f.State == FateState.Running);
        if (fates > 0)
        {
            Theme.FaIcon(FontAwesomeIcon.Map, Theme.Blue);
            ImGui.SameLine();
            if (ImGui.Selectable($"FATE в зоне: {fates}##mini_fate"))
                open(MainTab.Fates);
        }

        }

        // Таймеры: ближайший ретейнер / субмарина
        if (blocks.Contains("timers"))
        {
            var now = EorzeaWeather.Now;
            var next = Plugin.Config.Voyages.Values.Where(v => v.Return > 0).OrderBy(v => v.Return).FirstOrDefault();
            if (next is not null)
            {
                Theme.FaIcon(next.Kind == "retainer" ? FontAwesomeIcon.UserTie : FontAwesomeIcon.Ship, Theme.Blue);
                ImGui.SameLine();
                if (ImGui.Selectable($"{next.Name}: {(next.Return <= now ? "вернулся ✓" : "через " + EorzeaWeather.Until(next.Return))}##mini_tm"))
                    open(MainTab.Timers);
            }
        }

        // Океан: ближайший рейс
        if (blocks.Contains("ocean"))
        {
            var (start, route, boarding) = OceanTab.Next();
            Theme.FaIcon(FontAwesomeIcon.Water, boarding ? Theme.Green : Theme.Blue);
            ImGui.SameLine();
            if (ImGui.Selectable((boarding ? $"Океан: посадка! {route}" : $"Океан через {EorzeaWeather.Until(start)}: {route}") + "##mini_oc"))
                open(MainTab.Ocean);
        }

        // Погода: ближайшее напоминание
        if (blocks.Contains("weather") && WeatherTab.NextAlert() is { } wa)
        {
            Theme.FaIcon(FontAwesomeIcon.CloudSun, Theme.Gold);
            ImGui.SameLine();
            if (ImGui.Selectable($"{wa}##mini_wt")) open(MainTab.Weather);
        }

        // Цель: фото, уровень, дроп
        if (blocks.Contains("target") && BestiaryTab.TargetMobId() is { } tid)
        {
            ImGui.Separator();
            var mob = WikiService.Instance.Mob(tid);
            var tname = Plugin.Targets.Target?.Name.TextValue ?? "";
            Theme.FaIcon(FontAwesomeIcon.Crosshairs, Theme.Red);
            ImGui.SameLine();
            if (ImGui.Selectable($"{tname}##mini_tgt", false, ImGuiSelectableFlags.None, new Vector2(ImGui.CalcTextSize(tname).X, 0)))
                WikiNav.Open(WikiKind.Mob, tid, tname);
            Theme.Tooltip("Открыть карточку в бестиарии");
            if (mob is not null)
            {
                ImGui.SameLine();
                ImGui.TextColored(Theme.TextDim, $"ур. {mob.Level} · {WikiService.ZoneName(mob.Zone)}");
                ImGui.PushID("minitgt");
                ImGui.BeginGroup();
                var w0 = 300 * Theme.S;
                ImGui.Dummy(new Vector2(w0, 0));
                BestiaryTab.DrawPhoto(mob.Name, 110f);
                if (mob.Drops.Count > 0)
                {
                    ImGui.TextColored(Theme.TextDim, "Дроп:");
                    WikiView.ItemGrid(mob.Drops, "minidrop");
                }
                else ImGui.TextColored(Theme.Gray, "Дроп неизвестен");
                ImGui.EndGroup();
                ImGui.PopID();
            }
            else if (WikiService.Instance.Error("mob", tid) is not null)
            {
                ImGui.SameLine();
                ImGui.TextColored(Theme.Gray, "нет в базе");
            }
        }

        // Марки
        var hunt = hunts.Recent.FirstOrDefault(h => DateTime.Now - h.Seen < TimeSpan.FromMinutes(10));
        if (hunt is not null && blocks.Contains("hunt"))
        {
            Theme.FaIcon(FontAwesomeIcon.Crosshairs, Theme.Red);
            ImGui.SameLine();
            ImGui.TextColored(Theme.Text, $"[{hunt.Rank}] {hunt.Name} · {hunt.Coords}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Флаг##mini_hunt")) HuntTracker.Flag(hunt);
        }
    }

    /// <summary>Текущий сюжетный квест: взятый в журнал, иначе доступный самый «поздний» по главе.</summary>
    private QuestEntry? FindMsq()
    {
        var msqQuests = data.Quests.Values.Where(q => q.IsMsq).ToList();
        var accepted = msqQuests.Where(q => PlayerInfo.IsQuestAccepted(q.Id))
            .OrderBy(q => q.CategoryId).ThenBy(q => q.SortKey).FirstOrDefault();
        if (accepted is not null) return accepted;

        // Самая поздняя глава, где уже что-то выполнено — оттуда и продолжаем
        var lastDone = msqQuests.Where(q => PlayerInfo.IsQuestDone(q.Id))
            .OrderByDescending(q => q.CategoryId).ThenByDescending(q => q.SortKey).FirstOrDefault();
        return msqQuests
            .Where(q => lastDone is null || q.CategoryId > lastDone.CategoryId || (q.CategoryId == lastDone.CategoryId && q.SortKey >= lastDone.SortKey))
            .Where(q => PlayerInfo.GetQuestState(q) == QuestState.Available)
            .OrderBy(q => q.CategoryId).ThenBy(q => q.SortKey)
            .FirstOrDefault();
    }
}
