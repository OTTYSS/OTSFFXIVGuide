using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

/// <summary>Снимок прогресса за день.</summary>
[Serializable]
public sealed class DiarySnapshot
{
    public DateTime Date { get; set; }
    public Dictionary<string, int> Jobs { get; set; } = new();
    public int MsqDone { get; set; }
    public int DutiesDone { get; set; }
    public int UnlocksDone { get; set; }
    public int AvgIlvl { get; set; }
    public long Gil { get; set; }
    public int Achievements { get; set; } = -1;
    public int Mounts { get; set; }
    public int Minions { get; set; }

    public int JobLevelsTotal => Jobs.Values.Sum();
}

/// <summary>
/// Раз в 10 минут (и после входа) записывает снимок прогресса персонажа за сегодня.
/// Хранится в настройках, по каждому персонажу отдельно, до 180 дней.
/// </summary>
public static unsafe class ProgressDiary
{
    private const int KeepDays = 180;
    private static GuideData? data;
    private static DateTime nextSnapshot = DateTime.Now.AddSeconds(20);

    public static void Init(GuideData guideData) => data = guideData;

    public static string CharacterKey => Plugin.PlayerState.ContentId.ToString();

    public static List<DiarySnapshot> History =>
        Plugin.Config.Diary.TryGetValue(CharacterKey, out var l) ? l : new List<DiarySnapshot>();

    public static void OnLogin() => nextSnapshot = DateTime.Now.AddSeconds(20);

    public static void Tick()
    {
        if (DateTime.Now < nextSnapshot || !PlayerInfo.Ready || data is null) return;
        nextSnapshot = DateTime.Now.AddMinutes(10);
        try
        {
            var snap = Take(data);
            if (!Plugin.Config.Diary.TryGetValue(CharacterKey, out var list))
                Plugin.Config.Diary[CharacterKey] = list = new List<DiarySnapshot>();
            list.RemoveAll(s => s.Date.Date == snap.Date.Date);
            list.Add(snap);
            list.Sort((a, b) => a.Date.CompareTo(b.Date));
            if (list.Count > KeepDays) list.RemoveRange(0, list.Count - KeepDays);
            Plugin.Config.Save();
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Дневник: не удалось записать снимок");
        }
    }

    public static DiarySnapshot Take(GuideData d)
    {
        var snap = new DiarySnapshot { Date = DateTime.Now };

        // Класс и его джоба делят уровень (CNJ/WHM) — считаем одну запись на «ячейку опыта»
        var byExp = new Dictionary<int, (string Abbr, int Level, bool IsJob)>();
        foreach (var job in Plugin.Data.GetExcelSheet<ClassJob>())
        {
            if (job.RowId == 0 || job.ExpArrayIndex < 0) continue;
            var lvl = Plugin.PlayerState.GetClassJobLevel(job);
            if (lvl <= 0) continue;
            var abbr = job.Abbreviation.ExtractText();
            var isJob = job.JobIndex > 0;
            if (!byExp.TryGetValue(job.ExpArrayIndex, out var cur) || (isJob && !cur.IsJob))
                byExp[job.ExpArrayIndex] = (abbr, lvl, isJob);
        }
        foreach (var (abbr, lvl, _) in byExp.Values)
            if (abbr.Length > 0) snap.Jobs[abbr] = lvl;

        // Варианты одного шага (разные города/компании) считаем одним квестом
        snap.MsqDone = d.Quests.Values.Where(q => q.IsMsq && PlayerInfo.IsQuestDone(q.Id)).Select(q => q.Name).Distinct().Count();
        snap.UnlocksDone = d.Quests.Values.Count(q => q.Unlocks.Length > 0 && PlayerInfo.IsQuestDone(q.Id));
        snap.DutiesDone = d.Duties.Count(PlayerInfo.IsDutyCompleted);

        var im = InventoryManager.Instance();
        if (im != null) snap.Gil = im->GetGil();

        var ach = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement.Instance();
        if (ach != null && ach->IsLoaded())
            snap.Achievements = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Achievement>().Count(a => a.RowId > 0 && ach->IsComplete((int)a.RowId));

        var u = Plugin.Unlocks;
        snap.Mounts = Plugin.Data.GetExcelSheet<Mount>().Count(m => m.Icon != 0 && u.IsMountUnlocked(m));
        snap.Minions = Plugin.Data.GetExcelSheet<Companion>().Count(m => m.Icon != 0 && u.IsCompanionUnlocked(m));
        snap.AvgIlvl = AverageItemLevel();
        return snap;
    }

    public static int AverageItemLevel()
    {
        var im = InventoryManager.Instance();
        if (im == null) return 0;
        var eq = im->GetInventoryContainer(InventoryType.EquippedItems);
        if (eq == null) return 0;
        var items = Plugin.Data.GetExcelSheet<Item>();
        int sum = 0, count = 0;
        uint mainIlvl = 0;
        var twoHanded = false;
        for (var i = 0; i < eq->Size; i++)
        {
            if (i == 5 || i == 13) continue;
            var s = eq->GetInventorySlot(i);
            var row = s == null || s->GetBaseItemId() == 0 ? null : items.GetRowOrDefault(s->GetBaseItemId());
            if (i == 0 && row is not null)
            {
                mainIlvl = row.Value.LevelItem.RowId;
                twoHanded = row.Value.EquipSlotCategory.ValueNullable?.OffHand == -1;
            }
            if (i == 1 && twoHanded) { sum += (int)mainIlvl; count++; continue; }
            sum += (int)(row?.LevelItem.RowId ?? 0);
            count++;
        }
        return count == 0 ? 0 : sum / count;
    }
}
