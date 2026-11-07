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
/// Океаническая рыбалка (Limsa Lominsa): расписание рейсов и что ловить.
/// Рейс каждые 2 часа; маршрут = IKDRouteTable[(88 + номер рейса) % 144] — та же формула, что у Lulu's Tools.
/// </summary>
internal sealed class OceanTab
{
    private const int Interval = 7200;
    private const int Offset = 88;
    private const int Boarding = 15 * 60;
    private int open = -1;

    private static readonly string[] TimeNames = { "", "день", "закат", "ночь" };

    /// <summary>Ближайший рейс: время посадки, маршрут, открыта ли посадка сейчас.</summary>
    public static (long Start, string Route, bool Boarding) Next()
    {
        var now = EorzeaWeather.Now;
        var v = now / Interval;
        var boarding = now - v * Interval < Boarding;
        if (!boarding) v++;
        var r = Plugin.Data.GetExcelSheet<IKDRoute>().GetRowOrDefault(RouteAt(v));
        var times = r?.Time.Select(x => (int)x.RowId).ToList() ?? new List<int>();
        var name = (r?.Name.ExtractText() ?? "?") + (times.Count > 0 && times[^1] < TimeNames.Length ? $" ({TimeNames[times[^1]]})" : "");
        return (v * Interval, name, boarding);
    }

    private static uint RouteAt(long voyage)
    {
        var table = Plugin.Data.GetExcelSheet<IKDRouteTable>();
        var count = table.Count;
        if (count == 0) return 0;
        var idx = (uint)((Offset + voyage) % count);
        return table.GetRowOrDefault(idx)?.Route.RowId ?? 0;
    }

    public void Draw()
    {
        Theme.Header("Океаническая рыбалка", "Рейсы из Limsa Lominsa каждые 2 часа: куда идёт судно, когда посадка, что ловить");
        var now = EorzeaWeather.Now;
        var voyage = now / Interval;
        var start = voyage * Interval;
        var boardingOpen = now - start < Boarding;

        Theme.Panel("ocnow", () =>
        {
            Theme.FaIcon(FontAwesomeIcon.Ship, Theme.Gold);
            ImGui.SameLine();
            if (boardingOpen)
                ImGui.TextColored(Theme.Green, $"Посадка открыта! Ещё {EorzeaWeather.Until(start + Boarding)} — Limsa Lominsa Lower Decks, Dryskthota.");
            else
                ImGui.TextColored(Theme.Text, $"Следующая посадка через {EorzeaWeather.Until(start + Interval)} (в {EorzeaWeather.Local(start + Interval)}).");
            ImGui.TextColored(Theme.Gray, "Посадка — первые 15 минут каждого рейса. Доступ открывает квест «All the Fish in the Sea» (Limsa Lominsa).");
        });

        if (!ImGui.BeginChild("##ocean", Vector2.Zero)) { ImGui.EndChild(); return; }
        var routes = Plugin.Data.GetExcelSheet<IKDRoute>();
        for (var i = 0; i < 12; i++)
        {
            var v = voyage + (boardingOpen ? 0 : 1) + i;
            var t = v * Interval;
            var route = routes.GetRowOrDefault(RouteAt(v));
            if (route is null) continue;
            var r = route.Value;
            var name = r.Name.ExtractText();
            var times = r.Time.Select(x => (int)x.RowId).ToList();
            var label = $"{EorzeaWeather.Local(t)} · {name} · {string.Join(" → ", times.Select(x => x < TimeNames.Length ? TimeNames[x] : "?"))}";
            ImGui.PushID(i);
            if (ImGui.Selectable($"{label}##voy", open == i)) open = open == i ? -1 : i;
            ImGui.SameLine(Math.Max(ImGui.GetContentRegionAvail().X * 0.75f, 300 * Theme.S));
            ImGui.TextColored(t <= now ? Theme.Green : Theme.TextDim, t <= now ? "сейчас" : $"через {EorzeaWeather.Until(t)}");
            if (open == i) DrawRoute(r);
            ImGui.PopID();
        }
        ImGui.EndChild();
    }

    private static void DrawRoute(IKDRoute r)
    {
        var spots = r.Spot.Select(s => s.RowId).ToList();
        var times = r.Time.Select(x => (int)x.RowId).ToList();
        var ikd = Plugin.Data.GetExcelSheet<IKDSpot>();
        Theme.Panel("ocroute", () =>
        {
            for (var k = 0; k < spots.Count; k++)
            {
                var s = ikd.GetRowOrDefault(spots[k]);
                if (s is null) continue;
                var time = k < times.Count && times[k] < TimeNames.Length ? TimeNames[times[k]] : "";
                Theme.SubHeader($"{k + 1}. {s.Value.PlaceName.ValueNullable?.Name.ExtractText()} · {time}");
                var main = s.Value.SpotMain.ValueNullable;
                var sub = s.Value.SpotSub.ValueNullable;
                if (main is { } m)
                {
                    ImGui.TextColored(Theme.TextDim, "Обычная зона:");
                    WikiView.ItemGrid(m.Item.Where(x => x.RowId != 0).Select(x => x.RowId).Distinct(), $"om{k}");
                }
                if (sub is { } sp)
                {
                    ImGui.TextColored(Theme.Blue, "Спектральное течение:");
                    WikiView.ItemGrid(sp.Item.Where(x => x.RowId != 0).Select(x => x.RowId).Distinct(), $"os{k}");
                }
            }
            ImGui.TextColored(Theme.Gray, "Наведи на рыбу — наживка и подробности в карточке (клик).");
        });
    }
}
