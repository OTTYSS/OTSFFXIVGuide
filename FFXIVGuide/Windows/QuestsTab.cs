using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

internal sealed class QuestsTab
{
    private readonly GuideData data;
    private string search = "";
    private int categoryIdx;
    private bool unlocksOnly = true;
    private int statusFilter; // 0 все, 1 не выполненные, 2 доступны сейчас
    private QuestEntry? selected;
    private string[]? categories;
    private List<QuestEntry>? sorted;
    private bool scrollToSelected;

    private static readonly string[] StatusNames = { "Все", "Не выполненные", "Доступны сейчас" };

    public bool RequestFocus { get; private set; }

    private readonly QuestDetailsView details;

    public QuestsTab(GuideData data)
    {
        this.data = data;
        details = new QuestDetailsView(data);
    }

    /// <summary>Открыть невыполненные квесты-анлоки (из «Обзора»).</summary>
    public void ShowRemainingUnlocks()
    {
        search = "";
        categoryIdx = 0;
        unlocksOnly = true;
        statusFilter = 1;
        selected = null;
        scrollToSelected = false;
        RequestFocus = true;
    }

    public void Show(QuestEntry q)
    {
        search = "";
        categoryIdx = 0;
        unlocksOnly = q.Unlocks.Length > 0 && unlocksOnly;
        statusFilter = 0;
        selected = q;
        scrollToSelected = true;
        RequestFocus = true;
    }

    public void EndFrame() => RequestFocus = false;

    public void Draw()
    {
        categories ??= new[] { "Все разделы" }
            .Concat(data.Quests.Values.Select(q => q.Category).Where(c => c != "").Distinct().OrderBy(c => c))
            .ToArray();
        sorted ??= data.Quests.Values.OrderBy(q => q.Level).ThenBy(q => q.Id).ToList();

        Theme.Header("Квесты", "Что открывает каждый квест и какой шаг цепочки делать сейчас");
        Theme.Search("##qsearch", "Название квеста, место, награда или раздел…", ref search);

        Theme.Chips("qstatus", StatusNames, ref statusFilter);
        ImGui.SameLine(0, 16 * Theme.S);
        ImGui.Checkbox("Только с анлоками", ref unlocksOnly);
        Theme.Tooltip("Квесты, которые открывают данжи, бои, классы, навыки или эмоции");
        ImGui.SameLine(0, 16 * Theme.S);
        ImGui.SetNextItemWidth(Math.Max(160 * Theme.S, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("##qcat", categories[categoryIdx]))
        {
            for (var i = 0; i < categories.Length; i++)
                if (ImGui.Selectable(categories[i], categoryIdx == i)) categoryIdx = i;
            ImGui.EndCombo();
        }

        var rows = Filter().ToList();
        if (selected is not null && scrollToSelected && !rows.Contains(selected))
            rows.Insert(0, selected);

        ImGui.TextColored(Theme.TextDim, $"Найдено: {rows.Count}");

        var avail = ImGui.GetContentRegionAvail();
        var rowH = 46 * Theme.S;
        if (ImGui.BeginChild("##qlist", new Vector2(Theme.SplitWidth("quests", avail.X, 0.54f), 0)))
        {
            if (scrollToSelected && selected is not null)
            {
                var idx = rows.IndexOf(selected);
                if (idx >= 0) ImGui.SetScrollY(idx * (rowH + ImGui.GetStyle().ItemSpacing.Y));
                scrollToSelected = false;
            }

            Theme.VirtualList(rows.Count, rowH, i =>
            {
                var q = rows[i];
                var (badge, color) = StateBadge(q);
                var sub = $"Ур. {q.Level} · {q.Place}" + (q.Unlocks.Length > 0 ? $" · {q.Unlocks}" : "");
                if (Theme.Card($"##q{q.Id}", q.Icon, q.Name, sub, badge, color, selected == q, rowH))
                    selected = selected == q ? null : q;
            });
        }
        ImGui.EndChild();

        Theme.Splitter("quests", avail.X, 0.54f);
        if (ImGui.BeginChild("##qdetails", Vector2.Zero))
        {
            if (selected is null)
                Placeholder();
            else
                DrawDetails(selected);
        }
        ImGui.EndChild();
    }

    public static (string?, Vector4) StateBadge(QuestEntry q)
    {
        if (!PlayerInfo.Ready) return (null, Theme.Gray);
        return PlayerInfo.GetQuestState(q) switch
        {
            QuestState.Done => ("выполнен", Theme.Green),
            QuestState.Accepted => ("в журнале", Theme.Blue),
            QuestState.Available => ("доступен", Theme.Yellow),
            _ => ("закрыт", Theme.Gray),
        };
    }

    private static void Placeholder()
    {
        Theme.Panel("qph", () =>
        {
            Theme.FaIcon(FontAwesomeIcon.Scroll, Theme.GoldDim);
            ImGui.TextColored(Theme.TextDim, "Выбери квест слева, чтобы увидеть, что он требует, что открывает и какой шаг сделать сейчас.");
        });
    }

    private IEnumerable<QuestEntry> Filter()
    {
        var category = categoryIdx == 0 ? "" : categories![categoryIdx];
        foreach (var q in sorted!)
        {
            if (unlocksOnly && q.Unlocks.Length == 0) continue;
            if (category != "" && q.Category != category) continue;
            if (search.Length > 0
                && !NameTranslations.Matches(q.Name, search)
                && !NameTranslations.Matches(q.Place, search)
                && !NameTranslations.Matches(q.Unlocks, search)
                && !NameTranslations.Matches(q.Genre, search))
                continue;

            if (statusFilter != 0 && PlayerInfo.Ready)
            {
                var s = PlayerInfo.GetQuestState(q);
                if (statusFilter == 1 && s == QuestState.Done) continue;
                if (statusFilter == 2 && s != QuestState.Available && s != QuestState.Accepted) continue;
            }

            yield return q;
        }
    }

    private void DrawDetails(QuestEntry q) => details.Draw(q, Show);
}
