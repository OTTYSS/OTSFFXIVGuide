using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

/// <summary>Дейлики и викли: что уже сделано, что осталось, сколько до сброса.</summary>
internal sealed class ChecklistTab
{
    public void Draw()
    {
        Theme.Header("Дейлики и викли", "Что осталось сделать до сброса. Часть пунктов игра отмечает сама, остальные — галочкой.");

        if (!ImGui.BeginChild("##check", Vector2.Zero))
        {
            ImGui.EndChild();
            return;
        }

        // Дневные и недельные — в две колонки на широком окне
        var periods = new[] { Period.Daily, Period.Weekly };
        Theme.Grid("clgrid", periods.Length, 420f, idx =>
        {
            var period = periods[idx];
            var items = Checklist.Items.Where(i => i.Period == period).ToList();
            var states = items.Select(Checklist.StateOf).ToList();
            var done = states.Count(s => s.Done);
            var left = Checklist.NextReset(period, DateTime.UtcNow) - DateTime.UtcNow;

            Theme.Panel($"cl{period}", () =>
            {
                Theme.SubHeader(period == Period.Daily ? "Каждый день" : "Каждую неделю");
                Theme.Progress($"Сброс через {Format(left)}", done, items.Count, done == items.Count ? Theme.Green : Theme.Gold);

                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var st = states[i];
                    // Карточка: ручные пункты отмечаются кликом, автоматические — по данным игры
                    var badge = st.Done ? "готово" : st.Progress.Length > 0 ? st.Progress : item.Auto is null ? "отметить" : "не сделано";
                    var color = st.Done ? Theme.Green : item.Auto is null ? Theme.Gray : Theme.Yellow;
                    var sub = item.Hint + (item.Auto is null ? " · клик — отметить" : " · отмечается само");
                    if (Theme.Card($"##cl{item.Key}", 0, (st.Done ? "✓ " : "") + item.Name, sub, badge, color, false, 42 * Theme.S)
                        && item.Auto is null)
                        Checklist.SetManual(item, !st.Done);
                }
            });
        });

        Theme.Panel("clopt", () =>
        {
            var remind = Plugin.Config.RemindChecklistOnLogin;
            if (ImGui.Checkbox("Напоминать в чате при входе в игру", ref remind))
            {
                Plugin.Config.RemindChecklistOnLogin = remind;
                Plugin.Config.Save();
            }
            ImGui.TextColored(Theme.Gray, "Сброс: дейлики в 15:00 UTC, викли во вторник в 08:00 UTC.");
        });

        ImGui.EndChild();
    }

    private static string Format(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays} д {t.Hours} ч" : $"{t.Hours} ч {t.Minutes} мин";
}
