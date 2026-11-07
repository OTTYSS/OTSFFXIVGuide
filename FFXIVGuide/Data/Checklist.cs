using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

public enum Period { Daily, Weekly }

/// <summary>Пункт чек-листа. Auto — состояние берётся из игры; иначе отмечается вручную.</summary>
public sealed record CheckItem(string Key, string Name, Period Period, string Hint, Func<CheckState>? Auto);

/// <summary>Состояние: выполнено ли и строка прогресса (например, «5 / 9»).</summary>
public sealed record CheckState(bool Done, string Progress);

/// <summary>Дейлики и викли. Только чтение состояния из игры + ручные отметки.</summary>
public static unsafe class Checklist
{
    private static List<CheckItem>? items;
    private static DateTime remindAt = DateTime.MaxValue;

    // Сброс: дейлики — 15:00 UTC, викли — вторник 08:00 UTC
    public static DateTime LastDailyReset(DateTime utcNow)
    {
        var r = utcNow.Date.AddHours(15);
        return utcNow >= r ? r : r.AddDays(-1);
    }

    public static DateTime LastWeeklyReset(DateTime utcNow)
    {
        var r = utcNow.Date.AddHours(8);
        while (r.DayOfWeek != DayOfWeek.Tuesday || r > utcNow) r = r.AddDays(-1);
        return r;
    }

    public static DateTime NextReset(Period p, DateTime utcNow) =>
        p == Period.Daily ? LastDailyReset(utcNow).AddDays(1) : LastWeeklyReset(utcNow).AddDays(7);

    public static IReadOnlyList<CheckItem> Items => items ??= Build();

    public static CheckState StateOf(CheckItem item)
    {
        if (item.Auto is not null && PlayerInfo.Ready)
        {
            try { return item.Auto(); }
            catch { /* упадём на ручную отметку */ }
        }

        var done = Plugin.Config.ChecklistDone.TryGetValue(item.Key, out var at)
                   && at >= (item.Period == Period.Daily ? LastDailyReset(DateTime.UtcNow) : LastWeeklyReset(DateTime.UtcNow));
        return new CheckState(done, "");
    }

    public static void SetManual(CheckItem item, bool done)
    {
        if (done) Plugin.Config.ChecklistDone[item.Key] = DateTime.UtcNow;
        else Plugin.Config.ChecklistDone.Remove(item.Key);
        Plugin.Config.Save();
    }

    /// <summary>Напоминание в чат через несколько секунд после входа (когда игра всё загрузит).</summary>
    public static void RemindInChat() => remindAt = DateTime.Now.AddSeconds(8);

    public static void Tick()
    {
        if (DateTime.Now < remindAt) return;
        remindAt = DateTime.MaxValue;
        if (!PlayerInfo.Ready) return;

        var open = Items.Where(i => !StateOf(i).Done).ToList();
        if (open.Count == 0) return;
        var daily = open.Where(i => i.Period == Period.Daily).Select(i => i.Name).ToList();
        var weekly = open.Where(i => i.Period == Period.Weekly).Select(i => i.Name).ToList();
        var msg = "Не сделано";
        if (daily.Count > 0) msg += $" за день: {string.Join(", ", daily)}.";
        if (weekly.Count > 0) msg += $" За неделю: {string.Join(", ", weekly)}.";
        Plugin.Chat.Print(msg + " (/fguide daily)", "FFXIV Guide");
    }

    private static List<CheckItem> Build()
    {
        var list = new List<CheckItem>();

        // Рулетки (ежедневный бонус)
        var roulettes = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.ContentRoulette>();
        foreach (var id in new byte[] { 1, 2, 3, 5, 6, 8, 9, 15, 17 })
        {
            var row = roulettes.GetRowOrDefault(id);
            var name = row?.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name)) continue;
            var rid = id;
            list.Add(new CheckItem($"roulette{id}", name, Period.Daily, "Бонус опыта и томестонов за первый проход в день",
                () => new CheckState(UIState.Instance()->InstanceContent.IsRouletteComplete(rid), "")));
        }

        list.Add(new CheckItem("tribe", "Квесты союзных племён", Period.Daily, "Ежедневные квесты beast tribes",
            () =>
            {
                var left = QuestManager.Instance()->GetBeastTribeAllowance();
                return new CheckState(left == 0, $"осталось {left}");
            }));

        list.Add(new CheckItem("minicactpot", "Mini Cactpot", Period.Daily, "3 билета в Gold Saucer", null));
        list.Add(new CheckItem("gcturnin", "Поставки Grand Company", Period.Daily, "Supply/Provisioning — много опыта крафтерам и собирателям", null));

        list.Add(new CheckItem("wondrous", "Wondrous Tails", Period.Weekly, "Журнал у Khloe в Idyllshire, 9 печатей",
            () =>
            {
                var ps = PlayerState.Instance();
                if (!ps->HasWeeklyBingoJournal || ps->IsWeeklyBingoExpired()) return new CheckState(false, "журнал не взят");
                var n = ps->WeeklyBingoNumPlacedStickers;
                return new CheckState(n >= 9, $"{n} / 9");
            }));

        list.Add(new CheckItem("custom", "Custom Deliveries", Period.Weekly, "Еженедельные доставки (12 в неделю)",
            () =>
            {
                var left = SatisfactionSupplyManager.Instance()->GetRemainingAllowances();
                return new CheckState(left == 0, $"осталось {left}");
            }));

        list.Add(new CheckItem("tomes", "Лимит томестонов", Period.Weekly, "Недельный лимит текущих томестонов",
            () =>
            {
                var im = InventoryManager.Instance();
                var got = im->GetWeeklyAcquiredTomestoneCount();
                var max = InventoryManager.GetLimitedTomestoneWeeklyLimit();
                return new CheckState(max > 0 && got >= max, $"{got} / {max}");
            }));

        list.Add(new CheckItem("jumbo", "Jumbo Cactpot", Period.Weekly, "Билеты в Gold Saucer, розыгрыш в субботу", null));
        list.Add(new CheckItem("fashion", "Fashion Report", Period.Weekly, "Пятница–вторник, 60 000 MGP за участие", null));
        list.Add(new CheckItem("challenge", "Challenge Log", Period.Weekly, "Недельные задания в журнале", null));

        return list;
    }
}
