using System.Linq;

namespace FFXIVGuide.Data;

public static class StoryHelper
{
    /// <summary>Текущий сюжетный квест: взятый в журнал, иначе первый доступный после последнего выполненного.</summary>
    public static QuestEntry? CurrentMsq(GuideData data)
    {
        var msq = data.Quests.Values.Where(q => q.IsMsq).ToList();
        var accepted = msq.Where(q => PlayerInfo.IsQuestAccepted(q.Id))
            .OrderBy(q => q.CategoryId).ThenBy(q => q.SortKey).FirstOrDefault();
        if (accepted is not null) return accepted;

        var lastDone = msq.Where(q => PlayerInfo.IsQuestDone(q.Id))
            .OrderByDescending(q => q.CategoryId).ThenByDescending(q => q.SortKey).FirstOrDefault();
        return msq
            .Where(q => lastDone is null || q.CategoryId > lastDone.CategoryId || (q.CategoryId == lastDone.CategoryId && q.SortKey >= lastDone.SortKey))
            .Where(q => PlayerInfo.GetQuestState(q) == QuestState.Available)
            .OrderBy(q => q.CategoryId).ThenBy(q => q.SortKey)
            .FirstOrDefault();
    }

    /// <summary>Название главы сюжета (без «Main Scenario Quests»).</summary>
    public static string ChapterName(uint categoryId) =>
        (Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.JournalCategory>().GetRowOrDefault(categoryId)?.Name.ExtractText() ?? "")
        .Replace(" Main Scenario Quests", "");
}
