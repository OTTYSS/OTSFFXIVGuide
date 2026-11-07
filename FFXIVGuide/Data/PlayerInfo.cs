using System.Collections.Generic;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

public enum QuestState { Done, Accepted, Available, Locked }

/// <summary>Живое состояние персонажа. Только чтение.</summary>
public static unsafe class PlayerInfo
{
    public static bool Ready => Plugin.ClientState.IsLoggedIn && Plugin.PlayerState.IsLoaded;

    public static bool IsQuestDone(uint questId) => Ready && QuestManager.IsQuestComplete(questId);

    public static bool IsQuestAccepted(uint questId)
    {
        if (!Ready) return false;
        var qm = QuestManager.Instance();
        return qm != null && qm->IsQuestAccepted(questId);
    }

    public static QuestState GetQuestState(QuestEntry q)
    {
        if (IsQuestDone(q.Id)) return QuestState.Done;
        if (IsQuestAccepted(q.Id)) return QuestState.Accepted;
        foreach (var p in q.Previous)
            if (!IsQuestDone(p))
                return QuestState.Locked;
        return QuestState.Available;
    }

    /// <summary>
    /// Идёт по цепочке предыдущих квестов назад и возвращает самый ранний невыполненный —
    /// то, что нужно сделать прямо сейчас, чтобы продвинуться к цели.
    /// </summary>
    public static QuestEntry? NextStepTowards(GuideData data, uint targetQuestId)
    {
        if (!data.Quests.TryGetValue(targetQuestId, out var current))
            return null;
        if (IsQuestDone(current.Id))
            return null;

        var visited = new HashSet<uint>();
        while (visited.Add(current.Id))
        {
            QuestEntry? prevOpen = null;
            foreach (var p in current.Previous)
            {
                if (!IsQuestDone(p) && data.Quests.TryGetValue(p, out var prev))
                {
                    prevOpen = prev;
                    break;
                }
            }

            if (prevOpen is null)
                return current;
            current = prevOpen;
        }

        return current;
    }

    public static bool IsDutyUnlocked(DutyEntry d) =>
        Ready && d.InstanceContentId != 0 && UIState.IsInstanceContentUnlocked(d.InstanceContentId);

    public static bool IsDutyCompleted(DutyEntry d) =>
        Ready && d.InstanceContentId != 0 && UIState.IsInstanceContentCompleted(d.InstanceContentId);

    public static int ItemCount(uint itemId)
    {
        if (!Ready) return 0;
        var im = InventoryManager.Instance();
        if (im == null) return 0;
        return im->GetInventoryItemCount(itemId, false, true, true, 0)
             + im->GetInventoryItemCount(itemId, true, true, true, 0);
    }

    /// <summary>Надетый предмет в слоте экипировки (индекс как в игре).</summary>
    public static uint EquippedItem(int slotIndex)
    {
        if (!Ready) return 0;
        var im = InventoryManager.Instance();
        if (im == null) return 0;
        var slot = im->GetInventorySlot(InventoryType.EquippedItems, slotIndex);
        return slot == null ? 0 : slot->GetBaseItemId();
    }

    /// <summary>Ставит флаг на карте у NPC, выдающего квест.</summary>
    public static bool FlagQuestGiver(QuestEntry q)
    {
        if (q.IssuerLevelId == 0) return false;
        var level = Plugin.Data.GetExcelSheet<Level>().GetRowOrDefault(q.IssuerLevelId);
        if (level is null || level.Value.Territory.RowId == 0) return false;

        var link = new MapLinkPayload(
            level.Value.Territory.RowId,
            level.Value.Map.RowId,
            (int)(level.Value.X * 1000f),
            (int)(level.Value.Z * 1000f));
        return Plugin.GameGui.OpenMapWithMapLink(link);
    }
}
