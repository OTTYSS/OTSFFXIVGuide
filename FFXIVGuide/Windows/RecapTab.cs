using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

/// <summary>Разбор боёв: кто и после какой механики умирал, что кастовал босс.</summary>
internal sealed class RecapTab
{
    private DutyRun? selected;

    public void Draw()
    {
        Theme.Header("Разбор боёв", "Смерти группы и вероятные причины по последним заходам (хранятся до перезапуска игры)");

        var runs = DutyRecorder.Runs.ToList();
        if (DutyRecorder.Current is { } cur) runs.Insert(0, cur);
        if (runs.Count == 0)
        {
            Theme.EmptyState(FontAwesomeIcon.Skull, "Пока нет записей", "Зайди в любой данж, триал или рейд — запись идёт сама.");
            return;
        }
        selected ??= runs[0];
        if (!runs.Contains(selected)) selected = runs[0];

        var avail = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##rruns", new Vector2(avail.X * 0.35f, 0)))
        {
            foreach (var r in runs)
            {
                var live = r.End is null;
                var sub = $"{r.Start:HH:mm} · {(int)r.Duration.TotalMinutes} мин" + (live ? " · идёт" : "");
                if (Theme.Card($"##run{r.Start.Ticks}", 0, r.Duty, sub, $"смертей {r.Deaths.Count}",
                        r.Deaths.Count == 0 ? Theme.Green : Theme.Red, selected == r, 44 * Theme.S))
                    selected = r;
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("##rdetail", Vector2.Zero))
            DrawRun(selected);
        ImGui.EndChild();
    }

    private static void DrawRun(DutyRun r)
    {
        Theme.Panel("rsum", () =>
        {
            Theme.LargeText(Theme.Gold, r.Duty);
            ImGui.TextColored(Theme.TextDim, $"Начало {r.Start:HH:mm} · {(int)r.Duration.TotalMinutes} мин {r.Duration.Seconds} с · кастов {r.Casts.Count} · смертей {r.Deaths.Count}");
        });

        Theme.Panel("rdeaths", () =>
        {
            Theme.SubHeader("Смерти");
            if (r.Deaths.Count == 0)
            {
                Theme.FaIcon(FontAwesomeIcon.CheckCircle, Theme.Green);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Green, "Никто не умер.");
                return;
            }
            foreach (var d in r.Deaths)
            {
                Theme.FaIcon(FontAwesomeIcon.Skull, Theme.Red);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Text, $"{d.Time:HH:mm:ss} — {d.Player}");
                var cause = d.RecentCasts.LastOrDefault();
                if (cause is null)
                {
                    ImGui.TextColored(Theme.TextDim, "    перед смертью кастов не было (автоатаки, лужи или механика без каста)");
                    continue;
                }
                ImGui.TextColored(Theme.TextDim, $"    перед этим: {string.Join(" → ", d.RecentCasts.Select(c => c.Ability))}");
                var mech = d.RecentCasts.LastOrDefault(c => c.Mechanic is not null)?.Mechanic;
                if (mech is not null)
                    ImGui.TextColored(Theme.Gold, $"    → {mech.Tip}");
            }
        });

        var killers = r.Deaths
            .Select(d => d.RecentCasts.LastOrDefault())
            .Where(c => c is not null)
            .GroupBy(c => c!.Ability)
            .OrderByDescending(g => g.Count())
            .ToList();
        if (killers.Count > 0)
            Theme.Panel("rkill", () =>
            {
                Theme.SubHeader("Опаснее всего было");
                foreach (var g in killers)
                {
                    var mech = g.First()!.Mechanic;
                    ImGui.TextColored(Theme.Text, $"{g.Key} — смертей: {g.Count()}");
                    if (mech is not null)
                        ImGui.TextColored(Theme.TextDim, $"    {mech.What} → {mech.Tip}");
                }
            });

        Theme.Panel("rcasts", () =>
        {
            Theme.SubHeader("Что кастовали враги");
            foreach (var g in r.Casts.GroupBy(c => c.Ability).OrderByDescending(g => g.Count()).Take(20))
            {
                var mech = g.First().Mechanic;
                ImGui.TextColored(mech is null ? Theme.TextDim : Theme.Text, $"{g.Key} ×{g.Count()}");
                if (mech is not null)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.Gray, $"— {mech.Tip}");
                }
            }
        });
    }
}
