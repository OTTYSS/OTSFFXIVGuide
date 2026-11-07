using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

public enum StepState { Done, Active, Future }

public sealed record QuestStep(string Text, byte CompleteSeq);

/// <summary>Тексты квеста из игры: журнал по шагам, задачи (TODO) и награды.</summary>
public sealed record QuestDetails(
    string[] Journal,
    QuestStep[] Steps,
    uint Gil,
    List<string> Rewards,
    List<string> OptionalRewards);

public static unsafe class QuestText
{
    private static readonly Dictionary<uint, QuestDetails> Cache = new();

    public static QuestDetails Load(uint questId)
    {
        if (Cache.TryGetValue(questId, out var cached))
            return cached;

        var details = Build(questId);
        Cache[questId] = details;
        return details;
    }

    /// <summary>Текущий шаг квеста в журнале (0 — не взят, 255 — последний шаг «сдать»).</summary>
    public static byte CurrentSequence(uint questId)
    {
        if (!PlayerInfo.Ready || !PlayerInfo.IsQuestAccepted(questId)) return 0;
        return QuestManager.GetQuestSequence(questId);
    }

    public static StepState StateOf(QuestStep step, uint questId, byte seq)
    {
        if (PlayerInfo.IsQuestDone(questId)) return StepState.Done;
        if (seq == 0) return StepState.Future;
        if (step.CompleteSeq == seq) return StepState.Active;
        // 255 — финальный шаг, он идёт после всех остальных
        if (step.CompleteSeq != 255 && (seq == 255 || seq > step.CompleteSeq)) return StepState.Done;
        return StepState.Future;
    }

    private static QuestDetails Build(uint questId)
    {
        var quest = Plugin.Data.GetExcelSheet<Quest>().GetRowOrDefault(questId);
        if (quest is null)
            return new QuestDetails([], [], 0, [], []);
        var q = quest.Value;

        var journal = new SortedDictionary<int, string>();
        var todos = new SortedDictionary<int, string>();

        var id = q.Id.ExtractText();
        var num = id.Split('_')[^1];
        if (num.Length >= 3)
        {
            try
            {
                var sheet = Plugin.Data.Excel.GetSheet<RawRow>(name: $"quest/{num[..3]}/{id}");
                foreach (var row in sheet)
                {
                    var key = row.ReadStringColumn(0).ExtractText();
                    var value = row.ReadStringColumn(1).ExtractText().Trim();
                    if (value.Length == 0) continue;

                    var seqIdx = key.LastIndexOf("_SEQ_");
                    var todoIdx = key.LastIndexOf("_TODO_");
                    if (seqIdx >= 0 && int.TryParse(key[(seqIdx + 5)..], out var s))
                        journal[s] = value;
                    else if (todoIdx >= 0 && int.TryParse(key[(todoIdx + 6)..], out var t))
                        todos[t] = value;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.Warning(e, $"Не удалось прочитать текст квеста {id}");
            }
        }

        var steps = todos
            .Select(kv => new QuestStep(kv.Value, kv.Key < q.TodoParams.Count ? q.TodoParams[kv.Key].ToDoCompleteSeq : (byte)255))
            .ToArray();

        var items = Plugin.Data.GetExcelSheet<Item>();
        var rewards = new List<string>();
        for (var i = 0; i < q.Reward.Count; i++)
        {
            var rid = q.Reward[i].RowId;
            if (rid == 0) continue;
            var name = items.GetRowOrDefault(rid)?.Name.ExtractText();
            if (string.IsNullOrEmpty(name)) continue;
            var count = i < q.ItemCountReward.Count ? q.ItemCountReward[i] : (byte)1;
            rewards.Add(count > 1 ? $"{name} ×{count}" : name);
        }

        if (q.CurrencyReward.RowId != 0 && q.CurrencyRewardCount > 0)
        {
            var name = items.GetRowOrDefault(q.CurrencyReward.RowId)?.Name.ExtractText();
            if (!string.IsNullOrEmpty(name))
                rewards.Add($"{name} ×{q.CurrencyRewardCount}");
        }

        var optional = new List<string>();
        for (var i = 0; i < q.OptionalItemReward.Count; i++)
        {
            var name = q.OptionalItemReward[i].ValueNullable?.Name.ExtractText();
            if (string.IsNullOrEmpty(name)) continue;
            var count = i < q.OptionalItemCountReward.Count ? q.OptionalItemCountReward[i] : (byte)1;
            optional.Add(count > 1 ? $"{name} ×{count}" : name);
        }

        return new QuestDetails(journal.Values.ToArray(), steps, q.GilReward, rewards, optional);
    }
}
