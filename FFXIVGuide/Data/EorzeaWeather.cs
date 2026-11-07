using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

/// <summary>
/// Время Эорзеи и прогноз погоды. Погода в игре детерминирована: считается по времени
/// и таблице WeatherRate зоны — тот же алгоритм используют все погодные трекеры.
/// </summary>
public static class EorzeaWeather
{
    /// <summary>1 час Эорзеи = 175 секунд реального времени; погода меняется каждые 8 часов ET.</summary>
    public const int EtHourSeconds = 175;
    public const int PeriodSeconds = EtHourSeconds * 8;

    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Текущее время Эорзеи (часы и минуты).</summary>
    public static (int Hour, int Minute) EorzeaTime(long unix)
    {
        var et = unix * 3600 / EtHourSeconds;
        return ((int)(et / 3600 % 24), (int)(et / 60 % 60));
    }

    public static int EtHour(long unix) => (int)(unix / EtHourSeconds % 24);

    public static long PeriodStart(long unix) => unix - unix % PeriodSeconds;

    private static int Target(long unix)
    {
        var bell = unix / EtHourSeconds;
        var increment = (uint)((bell + 8 - bell % 8) % 24);
        var totalDays = (uint)(unix / 4200);
        var calcBase = totalDays * 100 + increment;
        var step1 = (calcBase << 11) ^ calcBase;
        var step2 = (step1 >> 8) ^ step1;
        return (int)(step2 % 100);
    }

    private static readonly Dictionary<uint, (uint[] Weather, byte[] Rate)> rates = new();

    private static (uint[] Weather, byte[] Rate)? Rates(uint weatherRate)
    {
        if (weatherRate == 0) return null;
        if (rates.TryGetValue(weatherRate, out var r)) return r;
        var row = Plugin.Data.GetExcelSheet<WeatherRate>().GetRowOrDefault(weatherRate);
        if (row is null) return null;
        r = (row.Value.Weather.Select(w => w.RowId).ToArray(), row.Value.Rate.ToArray());
        rates[weatherRate] = r;
        return r;
    }

    /// <summary>Погода (id из листа Weather) в зоне с данным WeatherRate в момент unix.</summary>
    public static uint WeatherAt(uint weatherRate, long unix)
    {
        if (Rates(weatherRate) is not { } r) return 0;
        var t = Target(unix);
        var cum = 0;
        for (var i = 0; i < r.Rate.Length; i++)
        {
            cum += r.Rate[i];
            if (t < cum) return r.Weather[i];
        }
        return r.Weather.LastOrDefault();
    }

    /// <summary>Все возможные погоды зоны.</summary>
    public static IEnumerable<uint> PossibleWeathers(uint weatherRate) =>
        Rates(weatherRate) is { } r ? r.Weather.Where((w, i) => w != 0 && r.Rate[i] > 0).Distinct() : Enumerable.Empty<uint>();

    public static string WeatherName(uint id) =>
        Plugin.Data.GetExcelSheet<Weather>().GetRowOrDefault(id)?.Name.ExtractText() ?? "";

    public static uint WeatherIcon(uint id) =>
        (uint)(Plugin.Data.GetExcelSheet<Weather>().GetRowOrDefault(id)?.Icon ?? 0);

    public static uint WeatherByName(string name) =>
        Plugin.Data.GetExcelSheet<Weather>().FirstOrDefault(w => w.Name.ExtractText().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)).RowId;

    // ───────────── Зоны ─────────────

    private static Dictionary<uint, uint>? placeToRate;

    /// <summary>WeatherRate открытой зоны по PlaceName (как в Garland Tools).</summary>
    public static uint RateForPlace(uint placeName)
    {
        if (placeToRate is null)
        {
            var map = new Dictionary<uint, uint>();
            foreach (var t in Plugin.Data.GetExcelSheet<TerritoryType>())
                if (t.PlaceName.RowId != 0 && t.WeatherRate.RowId != 0 && (!map.ContainsKey(t.PlaceName.RowId) || t.TerritoryIntendedUse.RowId == 1))
                    map[t.PlaceName.RowId] = t.WeatherRate.RowId;
            placeToRate = map;
        }
        return placeToRate.GetValueOrDefault(placeName);
    }

    /// <summary>
    /// Ближайшее окно (unix начала и конца), когда в зоне нужная погода (и, если задано, переход из погоды «до»)
    /// и время ET в [startHour, endHour). Ищет вперёд до maxDays реальных дней.
    /// </summary>
    public static (long Start, long End)? NextWindow(uint weatherRate, ICollection<uint>? weathers, ICollection<uint>? previous,
                                                     int startHour = 0, int endHour = 24, long? from = null, int maxDays = 14)
    {
        var now = from ?? Now;
        var t = now - now % EtHourSeconds;
        var limit = now + maxDays * 86400L;
        long? start = null;
        for (; t < limit; t += EtHourSeconds)
        {
            var ok = HourOk(EtHour(t), startHour, endHour);
            if (ok && weathers is { Count: > 0 }) ok = weathers.Contains(WeatherAt(weatherRate, t));
            if (ok && previous is { Count: > 0 }) ok = previous.Contains(WeatherAt(weatherRate, PeriodStart(t) - 1));
            if (ok) start ??= t;
            else if (start is not null) return (Math.Max(start.Value, now), t);
        }
        return start is null ? null : (Math.Max(start.Value, now), t);
    }

    private static bool HourOk(int h, int start, int end) =>
        start == end || (start < end ? h >= start && h < end : h >= start || h < end);

    public static string Until(long unix)
    {
        var d = TimeSpan.FromSeconds(Math.Max(0, unix - Now));
        return d.TotalDays >= 1 ? $"{(int)d.TotalDays} д {d.Hours} ч" : d.TotalHours >= 1 ? $"{(int)d.TotalHours} ч {d.Minutes} мин" : $"{d.Minutes} мин {d.Seconds:00} с";
    }

    public static string Local(long unix) => DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime.ToString("HH:mm");
}
