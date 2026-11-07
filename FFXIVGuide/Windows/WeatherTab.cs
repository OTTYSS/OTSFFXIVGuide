using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Время Эорзеи, прогноз погоды по зонам и напоминания о нужной погоде.</summary>
internal sealed class WeatherTab
{
    private sealed record Zone(uint PlaceName, uint Territory, uint Rate, string Name, string Expansion, uint ExVersion);

    private List<Zone>? zones;
    private string search = "";
    private uint open;

    private static List<Zone> Build()
    {
        var map = new Dictionary<uint, Zone>();
        foreach (var t in Plugin.Data.GetExcelSheet<TerritoryType>())
        {
            if (t.TerritoryIntendedUse.RowId is not (0 or 1) || t.WeatherRate.RowId == 0 || t.PlaceName.RowId == 0) continue;
            var name = t.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
            if (name.Length == 0 || map.ContainsKey(t.PlaceName.RowId)) continue;
            if (!EorzeaWeather.PossibleWeathers(t.WeatherRate.RowId).Any()) continue;
            map[t.PlaceName.RowId] = new Zone(t.PlaceName.RowId, t.RowId, t.WeatherRate.RowId, name, t.ExVersion.ValueNullable?.Name.ExtractText() ?? "", t.ExVersion.RowId);
        }
        return map.Values.OrderBy(z => z.ExVersion).ThenBy(z => z.Name).ToList();
    }

    private static void WeatherIcon(uint weather, float size, string tip)
    {
        var pos = ImGui.GetCursorScreenPos();
        Theme.GameIcon(EorzeaWeather.WeatherIcon(weather), pos, size, 4f);
        ImGui.Dummy(new Vector2(size));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
    }

    public void Draw()
    {
        zones ??= Build();
        Theme.Header("Погода и время", "Время Эорзеи, прогноз погоды по зонам и напоминания о нужной погоде");

        var now = EorzeaWeather.Now;
        var (h, m) = EorzeaWeather.EorzeaTime(now);
        Theme.Panel("wclock", () =>
        {
            Theme.FaIcon(FontAwesomeIcon.Clock, Theme.Gold);
            ImGui.SameLine();
            Theme.LargeText(Theme.Gold, $"{h:00}:{m:00} ET");
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextDim, $"· местное {DateTime.Now:HH:mm} · смена погоды через {EorzeaWeather.Until(EorzeaWeather.PeriodStart(now) + EorzeaWeather.PeriodSeconds)}");
            ImGui.TextColored(Theme.Gray, "1 час ET = 2 мин 55 с. Погода меняется в 00:00, 08:00 и 16:00 ET.");
        });

        DrawAlerts(now);

        Theme.Search("##wsearch", "Зона…", ref search);
        var term = search.Trim();
        var here = Plugin.ClientState.TerritoryType;
        var rows = zones.Where(z => term.Length == 0 || NameTranslations.Matches(z.Name, term))
            .OrderByDescending(z => z.Territory == here).ToList();

        if (!ImGui.BeginChild("##wlist", Vector2.Zero)) { ImGui.EndChild(); return; }
        var period = EorzeaWeather.PeriodStart(now);
        var size = 26 * Theme.S;
        string? lastEx = null;
        foreach (var z in rows)
        {
            if (z.Territory != here && z.Expansion != lastEx)
            {
                lastEx = z.Expansion;
                Theme.SubHeader(z.Expansion);
            }
            ImGui.PushID((int)z.PlaceName);
            var isOpen = open == z.PlaceName;
            if (ImGui.Selectable($"##row", isOpen, ImGuiSelectableFlags.None, new Vector2(0, size)))
                open = isOpen ? 0 : z.PlaceName;
            ImGui.SetItemAllowOverlap();
            ImGui.SameLine(8 * Theme.S);
            ImGui.TextColored(z.Territory == here ? Theme.Gold : Theme.Text, z.Name + (z.Territory == here ? " (ты здесь)" : ""));
            // Сейчас и следующие 5 периодов
            ImGui.SameLine(Math.Max(220 * Theme.S, ImGui.GetContentRegionAvail().X * 0.35f));
            for (var i = 0; i < 6; i++)
            {
                var t = period + i * EorzeaWeather.PeriodSeconds;
                var w = EorzeaWeather.WeatherAt(z.Rate, t);
                WeatherIcon(w, size, $"{EorzeaWeather.WeatherName(w)}\n{(i == 0 ? "сейчас" : $"с {EorzeaWeather.Local(t)} (через {EorzeaWeather.Until(t)})")}");
                ImGui.SameLine(0, i == 0 ? 14 * Theme.S : 4 * Theme.S);
            }
            ImGui.NewLine();
            if (isOpen) DrawZoneDetails(z, now);
            ImGui.PopID();
        }
        ImGui.EndChild();
    }

    private static void DrawZoneDetails(Zone z, long now)
    {
        Theme.Panel($"wz{z.PlaceName}", () =>
        {
            ImGui.TextColored(Theme.TextDim, "Когда будет каждая погода:");
            foreach (var w in EorzeaWeather.PossibleWeathers(z.Rate))
            {
                var win = EorzeaWeather.NextWindow(z.Rate, new[] { w }, null);
                WeatherIcon(w, 22 * Theme.S, EorzeaWeather.WeatherName(w));
                ImGui.SameLine();
                ImGui.TextColored(Theme.Text, EorzeaWeather.WeatherName(w));
                ImGui.SameLine(220 * Theme.S);
                if (win is not { } x) ImGui.TextColored(Theme.Gray, "не скоро");
                else if (x.Start <= now) ImGui.TextColored(Theme.Green, $"сейчас, ещё {EorzeaWeather.Until(x.End)}");
                else ImGui.TextColored(Theme.TextDim, $"через {EorzeaWeather.Until(x.Start)} (в {EorzeaWeather.Local(x.Start)})");
                ImGui.SameLine();
                var key = $"{z.PlaceName}|{w}";
                var on = Plugin.Config.WeatherAlerts.Contains(key);
                if (ImGui.SmallButton(on ? $"Не напоминать##{key}" : $"Напомнить##{key}"))
                {
                    if (on) Plugin.Config.WeatherAlerts.Remove(key); else Plugin.Config.WeatherAlerts.Add(key);
                    Plugin.Config.Save();
                }
            }
        });
    }

    private static void DrawAlerts(long now)
    {
        var alerts = Plugin.Config.WeatherAlerts;
        if (alerts.Count == 0) return;
        Theme.Panel("walerts", () =>
        {
            Theme.SubHeader("Напоминания (сообщение в чат за 3 минуты и в начале)");
            string? remove = null;
            foreach (var a in alerts)
            {
                var p = a.Split('|');
                if (p.Length != 2 || !uint.TryParse(p[0], out var place) || !uint.TryParse(p[1], out var w)) continue;
                var rate = EorzeaWeather.RateForPlace(place);
                var win = EorzeaWeather.NextWindow(rate, new[] { w }, null);
                Theme.FaIcon(FontAwesomeIcon.Bell, Theme.Gold);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Text, $"{WikiService.ZoneName(place)} · {EorzeaWeather.WeatherName(w)}");
                ImGui.SameLine();
                ImGui.TextColored(win is { } x && x.Start <= now ? Theme.Green : Theme.TextDim,
                    win is not { } y ? "не скоро" : y.Start <= now ? "идёт сейчас" : $"через {EorzeaWeather.Until(y.Start)}");
                ImGui.SameLine();
                if (ImGui.SmallButton($"Убрать##{a}")) remove = a;
            }
            if (remove is not null) { alerts.Remove(remove); Plugin.Config.Save(); }
        });
    }

    /// <summary>Текст про ближайшее окно из напоминаний о погоде (для мини-окна и «Обзора»).</summary>
    public static string? NextAlert()
    {
        var now = EorzeaWeather.Now;
        (long Start, string Text)? best = null;
        foreach (var a in Plugin.Config.WeatherAlerts)
        {
            var p = a.Split('|');
            if (p.Length != 2 || !uint.TryParse(p[0], out var place) || !uint.TryParse(p[1], out var w)) continue;
            var win = EorzeaWeather.NextWindow(EorzeaWeather.RateForPlace(place), new[] { w }, null, maxDays: 3);
            if (win is not { } x) continue;
            var text = x.Start <= now ? $"{WikiService.ZoneName(place)}: {EorzeaWeather.WeatherName(w)} сейчас" : $"{WikiService.ZoneName(place)}: {EorzeaWeather.WeatherName(w)} через {EorzeaWeather.Until(x.Start)}";
            if (best is null || x.Start < best.Value.Start) best = (x.Start, text);
        }
        return best?.Text;
    }

    // ───────────── Фоновая проверка напоминаний ─────────────

    private static readonly HashSet<string> notified = new();
    private static long nextTick;

    public static void Tick()
    {
        var now = EorzeaWeather.Now;
        if (now < nextTick || !PlayerInfo.Ready) return;
        nextTick = now + 10;
        foreach (var a in Plugin.Config.WeatherAlerts)
        {
            var p = a.Split('|');
            if (p.Length != 2 || !uint.TryParse(p[0], out var place) || !uint.TryParse(p[1], out var w)) continue;
            var rate = EorzeaWeather.RateForPlace(place);
            if (rate == 0) continue;
            var period = EorzeaWeather.PeriodStart(now);
            var next = period + EorzeaWeather.PeriodSeconds;
            var zone = WikiService.ZoneName(place);
            var name = EorzeaWeather.WeatherName(w);
            if (next - now <= 180 && EorzeaWeather.WeatherAt(rate, next) == w && notified.Add($"{a}|soon|{next}"))
                Notify.Send(Notify.Kind.Weather, "Погода", $"Через {(next - now) / 60 + 1} мин в {zone} будет {name}.");
            if (EorzeaWeather.WeatherAt(rate, now) == w && EorzeaWeather.WeatherAt(rate, period - 1) != w && now - period < 60 && notified.Add($"{a}|now|{period}"))
                Notify.Send(Notify.Kind.Weather, "Погода", $"Сейчас в {zone}: {name}.");
        }
    }
}
