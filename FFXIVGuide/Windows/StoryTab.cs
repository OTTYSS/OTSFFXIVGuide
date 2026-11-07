using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Lumina.Excel.Sheets;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

/// <summary>Сюжет (MSQ): прогресс по главам, текущий квест, что впереди.</summary>
internal sealed class StoryTab
{
    private sealed record Chapter(uint Id, string Name, List<QuestEntry> Quests, int Steps);

    private sealed record Snapshot(
        List<(Chapter Chapter, int Done, bool Complete)> Progress,
        List<QuestEntry> Current,
        List<QuestEntry> Upcoming,
        List<(DutyEntry Duty, QuestEntry Unlock)> Duties,
        Chapter? CurrentChapter,
        int LeftInChapter);

    private readonly GuideData data;
    private readonly QuestsTab quests;
    private readonly DutiesTab duties;
    private List<Chapter>? chapters;
    private Snapshot? snap;
    private DateTime nextRefresh = DateTime.MinValue;
    private readonly QuestDetailsView details;
    private QuestEntry? selected;
    private Chapter? openChapter;
    private bool chapterHideDone;

    public StoryTab(GuideData data, QuestsTab quests, DutiesTab duties)
    {
        this.data = data;
        this.quests = quests;
        this.duties = duties;
        details = new QuestDetailsView(data);
    }

    private void Select(QuestEntry q) => selected = selected == q ? null : q;

    public void Draw()
    {
        Theme.Header("Сюжет", "Главный сценарий: где ты сейчас и что впереди");

        if (!PlayerInfo.Ready)
        {
            Theme.Panel("snone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем — здесь появится прогресс по сюжету."));
            return;
        }

        chapters ??= BuildChapters();
        if (snap is null || DateTime.Now >= nextRefresh)
        {
            snap = BuildSnapshot();
            nextRefresh = DateTime.Now.AddSeconds(3);
        }

        if (!ImGui.BeginChild("##story", Vector2.Zero))
        {
            ImGui.EndChild();
            return;
        }

        var rowH = 46 * Theme.S;

        Theme.Panel("scur", () =>
        {
            Theme.SubHeader("Текущий сюжетный квест");
            if (snap.Current.Count == 0)
            {
                Theme.FaIcon(FontAwesomeIcon.CheckCircle, Theme.Green);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Green, "Весь доступный сюжет пройден. Жди следующего патча!");
                return;
            }

            if (snap.CurrentChapter is not null)
                ImGui.TextColored(Theme.TextDim, $"{snap.CurrentChapter.Name} · осталось квестов в главе: {snap.LeftInChapter}");

            foreach (var q in snap.Current)
            {
                var (badge, color) = QuestsTab.StateBadge(q);
                if (Theme.Card($"##sc{q.Id}", q.Icon, q.Name, $"Ур. {q.Level} · {q.Place}", badge, color, selected == q, rowH))
                    Select(q);
                if (Theme.IconButton($"scf{q.Id}", FontAwesomeIcon.Flag, "Флаг на карте"))
                    PlayerInfo.FlagQuestGiver(q);
                Theme.Tooltip("Флаг у NPC, который выдаёт квест");
            }
        });

        var avail = ImGui.GetContentRegionAvail().X;
        var colW = (avail - 10 * Theme.S) / 2;

        ImGui.BeginGroup();
        ImGui.PushItemWidth(colW);
        if (ImGui.BeginChild("##sleft", new Vector2(colW, 0)))
        {
            Theme.Panel("snext", () =>
            {
                Theme.SubHeader("Следующие квесты");
                if (snap.Upcoming.Count == 0)
                    ImGui.TextColored(Theme.TextDim, "Дальше сюжета пока нет.");
                foreach (var q in snap.Upcoming)
                    if (Theme.Card($"##su{q.Id}", q.Icon, q.Name, $"Ур. {q.Level} · {q.Place}", null, Theme.Gray, selected == q, rowH))
                        Select(q);
            });

            Theme.Panel("sduties", () =>
            {
                Theme.SubHeader("Сюжетные данжи и бои впереди");
                if (snap.Duties.Count == 0)
                    ImGui.TextColored(Theme.TextDim, "Все сюжетные данжи открыты.");
                foreach (var (d, unlock) in snap.Duties)
                    if (Theme.Card($"##sd{d.CfcId}", d.Icon, d.Name, $"Ур. {d.Level} · открывает «{unlock.Name}»", d.Type, Theme.Blue, false, rowH))
                        duties.Show(d);
            });
        }
        ImGui.EndChild();
        ImGui.PopItemWidth();
        ImGui.EndGroup();

        ImGui.SameLine(0, 10 * Theme.S);
        if (ImGui.BeginChild("##sright", Vector2.Zero))
        {
            if (selected is not null)
            {
                if (Theme.IconButton("sclose", FontAwesomeIcon.ArrowLeft, openChapter is null ? "К прогрессу" : $"К главе «{openChapter.Name}»"))
                    selected = null;
                ImGui.SameLine();
                if (selected is not null && Theme.IconButton("sopen", FontAwesomeIcon.Scroll, "Во вкладке «Квесты»"))
                    quests.Show(selected);
                if (selected is not null)
                    details.Draw(selected, q => selected = q);
            }
            else if (openChapter is not null)
            {
                DrawChapter(openChapter, rowH);
            }
            else
            {
                Theme.Panel("sprog", () =>
                {
                    Theme.SubHeader("Прогресс по главам");
                    ImGui.TextColored(Theme.TextDim, "Нажми на главу, чтобы увидеть все её квесты.");
                    foreach (var (ch, done, complete) in snap.Progress)
                    {
                        var color = complete ? Theme.Green : done > 0 ? Theme.Gold : Theme.Gray;
                        if (Theme.ProgressButton($"ch{ch.Id}", ch.Name, complete ? ch.Steps : done, ch.Steps, color))
                            openChapter = ch;
                    }
                });
            }
        }
        ImGui.EndChild();

        ImGui.EndChild();
    }

    private void DrawChapter(Chapter ch, float rowH)
    {
        if (Theme.IconButton("chback", FontAwesomeIcon.ArrowLeft, "Все главы"))
        {
            openChapter = null;
            return;
        }
        ImGui.SameLine(0, 16 * Theme.S);
        ImGui.Checkbox("Скрыть выполненные", ref chapterHideDone);

        // Варианты одного шага (разные города / Grand Company) показываем одной строкой:
        // берём выполненный вариант, иначе доступный, иначе первый.
        var steps = ch.Quests
            .GroupBy(q => q.Name)
            .Select(g => g.OrderBy(q => PlayerInfo.IsQuestDone(q.Id) ? 0 : PlayerInfo.GetQuestState(q) is QuestState.Accepted or QuestState.Available ? 1 : 2).First())
            .OrderBy(q => q.SortKey)
            .ToList();
        var doneCount = steps.Count(q => PlayerInfo.IsQuestDone(q.Id));

        Theme.Panel($"chap{ch.Id}", () =>
        {
            Theme.LargeText(Theme.Gold, ch.Name);
            Theme.Progress("Пройдено", doneCount, steps.Count, doneCount == steps.Count ? Theme.Green : Theme.Gold);

            var n = 0;
            foreach (var q in steps)
            {
                n++;
                if (chapterHideDone && PlayerInfo.IsQuestDone(q.Id)) continue;
                var (badge, color) = QuestsTab.StateBadge(q);
                if (Theme.Card($"##chq{q.Id}", q.Icon, $"{n}. {q.Name}", $"Ур. {q.Level} · {q.Place}", badge, color, false, rowH))
                    selected = q;
            }
        });
    }

    private List<Chapter> BuildChapters()
    {
        var cats = Plugin.Data.GetExcelSheet<JournalCategory>();
        return data.Quests.Values
            .Where(q => q.IsMsq)
            .GroupBy(q => q.CategoryId)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var name = cats.GetRowOrDefault(g.Key)?.Name.ExtractText() ?? $"Глава {g.Key}";
                name = name.Replace(" Main Scenario Quests", "");
                var list = g.OrderBy(q => q.SortKey).ThenBy(q => q.Id).ToList();
                // варианты для разных городов/компаний считаем одним шагом
                var steps = list.Select(q => q.Name).Distinct().Count();
                return new Chapter(g.Key, name, list, steps);
            })
            .ToList();
    }

    private Snapshot BuildSnapshot()
    {
        var progress = new List<(Chapter, int, bool)>();
        var current = new List<QuestEntry>();
        Chapter? currentChapter = null;
        var left = 0;
        var upcoming = new List<QuestEntry>();
        var upcomingNames = new HashSet<string>();

        foreach (var ch in chapters!)
        {
            var doneNames = ch.Quests.Where(q => PlayerInfo.IsQuestDone(q.Id)).Select(q => q.Name).ToHashSet();
            var maxDoneSort = ch.Quests.Where(q => PlayerInfo.IsQuestDone(q.Id)).Select(q => (int)q.SortKey).DefaultIfEmpty(-1).Max();
            var last = ch.Quests[^1];
            var complete = PlayerInfo.IsQuestDone(last.Id) || doneNames.Count >= ch.Steps;
            progress.Add((ch, doneNames.Count, complete));

            if (complete)
                continue;

            // Кандидаты: не выполнены, не раньше уже пройденного места главы, без пропущенных вариантов.
            var remaining = ch.Quests
                .Where(q => !doneNames.Contains(q.Name) && q.SortKey >= maxDoneSort)
                .ToList();

            if (currentChapter is null)
            {
                var active = remaining
                    .Select(q => (q, s: PlayerInfo.GetQuestState(q)))
                    .Where(x => x.s is QuestState.Accepted or QuestState.Available)
                    .OrderBy(x => x.s == QuestState.Accepted ? 0 : 1)
                    .ThenBy(x => x.q.SortKey)
                    .Select(x => x.q)
                    .DistinctBy(q => q.Name)
                    .Take(3)
                    .ToList();

                if (active.Count > 0)
                {
                    current = active;
                    currentChapter = ch;
                    left = remaining.Select(q => q.Name).Distinct().Count();
                }
            }

            foreach (var q in remaining)
            {
                if (upcoming.Count >= 10) break;
                if (current.Any(c => c.Name == q.Name) || !upcomingNames.Add(q.Name)) continue;
                upcoming.Add(q);
            }
        }

        var msqDuties = data.Duties
            .Where(d => d.UnlockQuestId != 0 && d.InstanceContentId != 0)
            .Select(d => (d, q: data.Quests.TryGetValue(d.UnlockQuestId, out var q) ? q : null))
            .Where(x => x.q is { IsMsq: true } && !PlayerInfo.IsQuestDone(x.q.Id))
            .OrderBy(x => x.q!.CategoryId)
            .ThenBy(x => x.q!.SortKey)
            .Take(6)
            .Select(x => (x.d, x.q!))
            .ToList();

        return new Snapshot(progress, current, upcoming, msqDuties, currentChapter, left);
    }
}
