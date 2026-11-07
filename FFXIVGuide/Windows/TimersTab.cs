using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

[Serializable]
public sealed class VoyageInfo
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";   // "retainer", "sub", "airship"
    public long Return { get; set; }         // unix, 0 — не отправлен
    public bool Notified { get; set; }
}

/// <summary>
/// Таймеры: вылазки ретейнеров, субмарины и дирижабли, сбросы и события недели.
/// Данные ретейнеров и мастерской игра отдаёт, только когда ты у колокольчика или в мастерской, — запоминаем.
/// </summary>
internal sealed unsafe class TimersTab
{
    // ───────────── Сбор данных ─────────────

    private static DateTime nextTick;

    public static void Tick()
    {
        if (DateTime.Now < nextTick || !PlayerInfo.Ready) return;
        nextTick = DateTime.Now.AddSeconds(5);
        var cfg = Plugin.Config;
        var changed = false;

        try
        {
            var rm = RetainerManager.Instance();
            if (rm != null && rm->GetRetainerCount() > 0)
                for (var i = 0u; i < rm->GetRetainerCount(); i++)
                {
                    var r = rm->GetRetainerBySortedIndex(i);
                    if (r == null || r->RetainerId == 0) continue;
                    var key = "r" + r->RetainerId;
                    var ret = r->VentureId == 0 ? 0 : (long)r->VentureComplete;
                    if (!cfg.Voyages.TryGetValue(key, out var v) || v.Return != ret || v.Name != r->NameString)
                    {
                        cfg.Voyages[key] = new VoyageInfo { Name = r->NameString, Kind = "retainer", Return = ret, Notified = ret <= EorzeaWeather.Now && v?.Notified == true };
                        changed = true;
                    }
                }

            var hm = HousingManager.Instance();
            if (hm != null && hm->WorkshopTerritory != null)
            {
                var ws = hm->WorkshopTerritory;
                var subs = ws->Submersible.Data;
                for (var i = 0; i < subs.Length; i++)
                {
                    var s = subs[i];
                    var name = s.NameString;
                    if (string.IsNullOrEmpty(name)) continue;
                    changed |= Store(cfg, $"s{i}", name, "sub", s.GetReturnTime());
                }
                var ships = ws->Airship.Data;
                for (var i = 0; i < ships.Length; i++)
                {
                    var s = ships[i];
                    var name = s.NameString;
                    if (string.IsNullOrEmpty(name)) continue;
                    changed |= Store(cfg, $"a{i}", name, "airship", s.GetReturnTime());
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Debug($"Таймеры: {e.Message}");
        }

        // Уведомления о возвращении
        var now = EorzeaWeather.Now;
        foreach (var v in cfg.Voyages.Values)
        {
            if (v.Return == 0 || v.Notified || v.Return > now) continue;
            v.Notified = true;
            changed = true;
            var on = v.Kind == "retainer" ? cfg.NotifyRetainers : cfg.NotifyVoyages;
            if (on)
            {
                if (v.Kind == "retainer") Notify.Send(Notify.Kind.Retainer, "Ретейнеры", $"Ретейнер {v.Name} вернулся с вылазки.");
                else Notify.Send(Notify.Kind.Voyage, "Мастерская", v.Kind == "sub" ? $"Субмарина {v.Name} вернулась." : $"Дирижабль {v.Name} вернулся.");
            }
        }
        if (changed) cfg.Save();
    }

    private static bool Store(Configuration cfg, string key, string name, string kind, DateTime ret)
    {
        var r = ret.Year < 2000 ? 0 : new DateTimeOffset(ret.ToUniversalTime()).ToUnixTimeSeconds();
        if (cfg.Voyages.TryGetValue(key, out var v) && v.Return == r && v.Name == name) return false;
        cfg.Voyages[key] = new VoyageInfo { Name = name, Kind = kind, Return = r, Notified = v?.Return == r && v.Notified };
        return true;
    }

    // ───────────── Сбросы ─────────────

    private static DateTime NextWeekly(DayOfWeek day, int hourUtc, DateTime nowUtc)
    {
        var d = nowUtc.Date.AddHours(hourUtc);
        while (d.DayOfWeek != day || d <= nowUtc) d = d.AddDays(1);
        return d;
    }

    private static DateTime NextDaily(int hourUtc, DateTime nowUtc)
    {
        var d = nowUtc.Date.AddHours(hourUtc);
        return d > nowUtc ? d : d.AddDays(1);
    }

    /// <summary>Розыгрыш Jumbo Cactpot зависит от региона датацентра.</summary>
    private static (DayOfWeek Day, int Hour, string Region) Jumbo()
    {
        var region = Plugin.PlayerState.CurrentWorld.ValueNullable?.DataCenter.ValueNullable?.Region.RowId ?? 3u;
        return region switch
        {
            1 => (DayOfWeek.Saturday, 12, "JP"),
            2 => (DayOfWeek.Sunday, 2, "NA"),
            4 => (DayOfWeek.Saturday, 9, "OCE"),
            _ => (DayOfWeek.Saturday, 19, "EU"),
        };
    }

    private static string In(DateTime utc)
    {
        var d = utc - DateTime.UtcNow;
        var s = d.TotalDays >= 1 ? $"{(int)d.TotalDays} д {d.Hours} ч" : d.TotalHours >= 1 ? $"{(int)d.TotalHours} ч {d.Minutes} мин" : $"{Math.Max(0, d.Minutes)} мин";
        return $"через {s} ({utc.ToLocalTime():ddd HH:mm})";
    }

    private static void Row(FontAwesomeIcon icon, string name, string value, Vector4 color)
    {
        Theme.FaIcon(icon, Theme.GoldDim);
        ImGui.SameLine();
        ImGui.TextColored(Theme.Text, name);
        ImGui.SameLine(Math.Max(240 * Theme.S, ImGui.GetCursorPosX() + 8 * Theme.S));
        ImGui.TextColored(color, value);
    }

    // ───────────── Окно ─────────────

    public void Draw()
    {
        Theme.Header("Таймеры", "Вылазки ретейнеров, субмарины и дирижабли, сбросы и события недели");
        if (!ImGui.BeginChild("##timers", Vector2.Zero)) { ImGui.EndChild(); return; }
        var cfg = Plugin.Config;
        var now = EorzeaWeather.Now;
        var panels = new List<System.Action>();

        panels.Add(() => Theme.Panel("tret", () =>
        {
            Theme.SubHeader("Ретейнеры");
            var list = cfg.Voyages.Values.Where(v => v.Kind == "retainer").OrderBy(v => v.Return == 0 ? long.MaxValue : v.Return).ToList();
            if (list.Count == 0) ImGui.TextColored(Theme.TextDim, "Открой колокольчик ретейнеров один раз — запомню их вылазки.");
            foreach (var v in list)
                Row(FontAwesomeIcon.UserTie, v.Name,
                    v.Return == 0 ? "не на вылазке" : v.Return <= now ? "вернулся ✓" : $"через {EorzeaWeather.Until(v.Return)} ({EorzeaWeather.Local(v.Return)})",
                    v.Return == 0 ? Theme.Gray : v.Return <= now ? Theme.Green : Theme.Text);
            var n = cfg.NotifyRetainers;
            if (ImGui.Checkbox("Сообщать в чат о возвращении##nr", ref n)) { cfg.NotifyRetainers = n; cfg.Save(); }
        }));

        panels.Add(() => Theme.Panel("tsub", () =>
        {
            Theme.SubHeader("Субмарины и дирижабли");
            var list = cfg.Voyages.Values.Where(v => v.Kind != "retainer").OrderBy(v => v.Return == 0 ? long.MaxValue : v.Return).ToList();
            if (list.Count == 0) ImGui.TextColored(Theme.TextDim, "Зайди в мастерскую компании — запомню время возвращения.");
            foreach (var v in list)
                Row(v.Kind == "sub" ? FontAwesomeIcon.Ship : FontAwesomeIcon.Plane, v.Name,
                    v.Return == 0 ? "в доке" : v.Return <= now ? "вернулся ✓" : $"через {EorzeaWeather.Until(v.Return)} ({EorzeaWeather.Local(v.Return)})",
                    v.Return == 0 ? Theme.Gray : v.Return <= now ? Theme.Green : Theme.Text);
            var n = cfg.NotifyVoyages;
            if (ImGui.Checkbox("Сообщать в чат о возвращении##nv", ref n)) { cfg.NotifyVoyages = n; cfg.Save(); }
        }));

        panels.Add(() => Theme.Panel("treset", () =>
        {
            Theme.SubHeader("Сбросы");
            var utc = DateTime.UtcNow;
            Row(FontAwesomeIcon.Sun, "Ежедневный сброс", In(NextDaily(15, utc)), Theme.Text);
            Row(FontAwesomeIcon.CalendarAlt, "Еженедельный сброс", In(NextWeekly(DayOfWeek.Tuesday, 8, utc)), Theme.Text);
            Row(FontAwesomeIcon.Flag, "Grand Company (supply)", In(NextDaily(20, utc)), Theme.Text);
            try
            {
                var leve = QuestManager.GetNextLeveAllowancesDateTime();
                Row(FontAwesomeIcon.Scroll, "Новые разрешения на левы", In(leve.ToUniversalTime()), Theme.Text);
            }
            catch { /* нет данных */ }
            try
            {
                var map = UIState.Instance()->GetNextMapAllowanceDateTime();
                Row(FontAwesomeIcon.Map, "Карта сокровищ", map <= DateTime.Now ? "можно собрать ✓" : In(map.ToUniversalTime()), map <= DateTime.Now ? Theme.Green : Theme.Text);
            }
            catch { /* нет данных */ }
        }));

        panels.Add(() => Theme.Panel("tgs", () =>
        {
            Theme.SubHeader("Gold Saucer и события");
            var utc = DateTime.UtcNow;
            var (day, hour, region) = Jumbo();
            Row(FontAwesomeIcon.Ticket, $"Jumbo Cactpot ({region})", In(NextWeekly(day, hour, utc)), Theme.Text);
            Row(FontAwesomeIcon.Th, "Mini Cactpot (3 билета)", In(NextDaily(15, utc)), Theme.Text);
            // Fashion Report: пятница 08:00 UTC — вторник 08:00 UTC
            var fri = NextWeekly(DayOfWeek.Friday, 8, utc);
            var tue = NextWeekly(DayOfWeek.Tuesday, 8, utc);
            var open = tue < fri;
            Row(FontAwesomeIcon.Tshirt, "Fashion Report", open ? $"открыт, закроется {In(tue)}" : $"откроется {In(fri)}", open ? Theme.Green : Theme.Text);
            Row(FontAwesomeIcon.Users, "Wondrous Tails", In(NextWeekly(DayOfWeek.Tuesday, 8, utc)), Theme.Text);
        }));

        Theme.Grid("tgrid", panels.Count, 380f, i => panels[i]());
        ImGui.EndChild();
    }
}
