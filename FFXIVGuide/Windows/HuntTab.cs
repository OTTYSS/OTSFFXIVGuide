using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

/// <summary>Hunt-марки: настройки оповещений и последние замеченные марки.</summary>
internal sealed class HuntTab
{
    private readonly HuntTracker tracker;

    public HuntTab(HuntTracker tracker) => this.tracker = tracker;

    public void Draw()
    {
        Theme.Header("Hunt-марки", "Оповещение со звуком и ссылкой на карту, когда рядом появилась марка");
        var cfg = Plugin.Config;

        Theme.Panel("hset", () =>
        {
            Theme.SubHeader("Оповещения");
            var changed = false;
            var on = cfg.HuntAlerts; changed |= ImGui.Checkbox("Включить оповещения", ref on); cfg.HuntAlerts = on;
            var snd = cfg.HuntSound; changed |= ImGui.Checkbox("Звук", ref snd); cfg.HuntSound = snd;
            ImGui.TextColored(Theme.TextDim, "Ранги:");
            ImGui.SameLine();
            var s = cfg.HuntRankS; changed |= ImGui.Checkbox("S / SS", ref s); cfg.HuntRankS = s;
            ImGui.SameLine();
            var a = cfg.HuntRankA; changed |= ImGui.Checkbox("A", ref a); cfg.HuntRankA = a;
            ImGui.SameLine();
            var b = cfg.HuntRankB; changed |= ImGui.Checkbox("B", ref b); cfg.HuntRankB = b;
            if (changed) cfg.Save();
            ImGui.TextColored(Theme.Gray, "Марку видно, только когда она в зоне прорисовки рядом с тобой (как в самой игре).");
        });

        Theme.Panel("hrecent", () =>
        {
            Theme.SubHeader($"Последние замеченные · {tracker.Recent.Count}");
            if (tracker.Recent.Count == 0)
            {
                ImGui.TextColored(Theme.TextDim, "Пока никого. Марки появятся здесь, как только окажутся рядом.");
                return;
            }

            foreach (var h in tracker.Recent)
            {
                var color = h.Rank is "S" or "SS" ? Theme.Red : h.Rank == "A" ? Theme.Gold : Theme.Blue;
                if (Theme.Card($"##hunt{h.ObjectId}{h.Seen.Ticks}", 0, $"[{h.Rank}] {h.Name}",
                        $"{h.Zone} · {h.Coords} · {Ago(h.Seen)} · клик — флаг на карте", $"{h.Rank}-ранг", color, false, 42 * Theme.S))
                    HuntTracker.Flag(h);
            }
        });
    }

    private static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalMinutes < 1 ? "только что" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} мин назад" : $"{(int)d.TotalHours} ч назад";
    }
}
