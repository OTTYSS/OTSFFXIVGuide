using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>
/// Побочные сюжетные цепочки: Chronicles of a New Era, Hildibrand, реликвии, союзные племена, Studium,
/// ролевые квесты и т.д. Группировка — по разделам журнала квестов игры (JournalGenre / JournalCategory).
/// </summary>
internal sealed class SideStoryTab
{
    private sealed record Q(uint Id, string Name, uint Level, uint Issuer, uint[] Prev);
    private sealed record Chain(uint Genre, string Name, string Section, uint Icon, List<Q> Quests);

    private List<Chain>? chains;
    private int filter = 1;
    private static readonly string[] Filters = { "Все", "Не завершённые", "Завершённые" };
    private string search = "";
    private uint open;

    /// <summary>Разделы журнала, которые считаем побочными сюжетами (без MSQ, квестов зон, классов и событий).</summary>
    private static bool SideCategory(uint cat) =>
        cat is >= 16 and <= 58 || cat is 90 or 91 or 92 or 95 or 96 or 98;

    private void Build()
    {
        var genres = Plugin.Data.GetExcelSheet<JournalGenre>();
        var byGenre = new Dictionary<uint, List<Quest>>();
        foreach (var q in Plugin.Data.GetExcelSheet<Quest>())
        {
            if (q.Name.IsEmpty || q.JournalGenre.RowId == 0) continue;
            if (!byGenre.TryGetValue(q.JournalGenre.RowId, out var l)) byGenre[q.JournalGenre.RowId] = l = new List<Quest>();
            l.Add(q);
        }
        var res = new List<Chain>();
        foreach (var (gid, qs) in byGenre)
        {
            var g = genres.GetRowOrDefault(gid);
            if (g is null) continue;
            var cat = g.Value.JournalCategory.RowId;
            if (!SideCategory(cat)) continue;
            var gname = g.Value.Name.ExtractText();
            if (gname.StartsWith("Daily", StringComparison.OrdinalIgnoreCase)) continue; // дейлики племён — не сюжет
            var section = g.Value.JournalCategory.ValueNullable?.Name.ExtractText() ?? "";
            // У племён все цепочки зовутся «Main Quests» — подставляем название раздела
            var name = gname is "Main Quests" ? section : gname;
            var list = qs.Select(q => new Q(q.RowId, q.Name.ExtractText(), q.ClassJobLevel[0], q.IssuerLocation.RowId,
                    q.PreviousQuest.Select(p => p.RowId).Where(p => p != 0).ToArray()))
                .OrderBy(q => q.Level).ThenBy(q => q.Id).ToList();
            res.Add(new Chain(gid, name, section, (uint)g.Value.Icon, list));
        }
        chains = res.OrderBy(c => genres.GetRowOrDefault(c.Genre)?.JournalCategory.RowId ?? 0).ThenBy(c => c.Genre).ToList();
    }

    private static Q? Next(Chain c) =>
        c.Quests.FirstOrDefault(q => !PlayerInfo.IsQuestDone(q.Id) && q.Prev.All(PlayerInfo.IsQuestDone))
        ?? c.Quests.FirstOrDefault(q => !PlayerInfo.IsQuestDone(q.Id));

    public IEnumerable<SearchHit> Search(string term, Action<MainTab> go)
    {
        if (chains is null) Build();
        return chains!.Where(c => NameTranslations.Matches(c.Name, term))
            .Select(c => new SearchHit("Побочные сюжеты", NameTranslations.Show(c.Name), $"{c.Quests.Count} квестов", c.Icon, () => { open = c.Genre; search = ""; filter = 0; go(MainTab.SideStories); }));
    }

    public void Draw()
    {
        Theme.Header("Побочные сюжеты", "Прогресс по каждой побочной цепочке и следующий квест");
        if (chains is null)
        {
            try { Build(); }
            catch (Exception e) { Plugin.Log.Warning(e, "Побочные сюжеты: ошибка разбора"); chains = new(); }
        }
        if (!PlayerInfo.Ready) { Theme.Panel("ssnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем.")); return; }

        var stats = chains!.Select(c => (C: c, Done: c.Quests.Count(q => PlayerInfo.IsQuestDone(q.Id)))).ToList();
        Theme.Panel("sstop", () =>
        {
            var full = stats.Count(s => s.Done == s.C.Quests.Count);
            Theme.Progress("Цепочек завершено", full, stats.Count, Theme.Gold);
            ImGui.TextColored(Theme.Gray, "Разделы — как в журнале квестов игры. «Следующий» — первый невыполненный квест, у которого выполнены все предыдущие.");
        });
        Theme.Chips("ssf", Filters, ref filter);
        Theme.Search("##sss", "Цепочка…", ref search);

        var rows = stats.Where(s => filter == 0 || (filter == 1 ? s.Done < s.C.Quests.Count : s.Done == s.C.Quests.Count))
            .Where(s => search.Trim().Length < 2 || NameTranslations.Matches(s.C.Name, search.Trim()) || NameTranslations.Matches(s.C.Section, search.Trim()))
            .ToList();

        if (!ImGui.BeginChild("##ss", Vector2.Zero)) { ImGui.EndChild(); return; }
        foreach (var sec in rows.GroupBy(r => r.C.Section))
        {
            Theme.SubHeader(NameTranslations.Show(sec.Key));
            foreach (var (c, done) in sec)
                Theme.Panel($"ss{c.Genre}", () =>
                {
                    var complete = done == c.Quests.Count;
                    if (Theme.ProgressButton($"ssb{c.Genre}", NameTranslations.Show(c.Name), done, c.Quests.Count, complete ? Theme.Green : open == c.Genre ? Theme.Gold : Theme.Blue))
                        open = open == c.Genre ? 0 : c.Genre;
                    if (!complete && Next(c) is { } n)
                    {
                        var avail = n.Prev.All(PlayerInfo.IsQuestDone);
                        ImGui.TextColored(Theme.TextDim, avail ? "Следующий:" : "Дальше (ещё не открыт):");
                        ImGui.SameLine();
                        ImGui.TextColored(Theme.Gold, $"{NameTranslations.Show(n.Name)} · ур. {n.Level}");
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Нажми — открыть карточку квеста");
                        if (ImGui.IsItemClicked()) WikiNav.Open(WikiKind.Quest, n.Id, n.Name);
                        if (n.Issuer != 0 && avail)
                        {
                            ImGui.SameLine();
                            if (Theme.IconButton($"ssflag{c.Genre}", FontAwesomeIcon.MapMarkerAlt, "Флаг на карте")) ItemSources.FlagLevel(n.Issuer);
                        }
                    }
                    if (open != c.Genre) return;
                    foreach (var q in c.Quests)
                    {
                        var ok = PlayerInfo.IsQuestDone(q.Id);
                        Theme.FaIcon(ok ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.Circle, ok ? Theme.Green : Theme.Gray);
                        ImGui.SameLine();
                        if (ImGui.Selectable($"{NameTranslations.Show(q.Name)}##ssq{q.Id}"))
                            WikiNav.Open(WikiKind.Quest, q.Id, q.Name);
                        ImGui.SameLine();
                        ImGui.TextColored(Theme.TextDim, $"ур. {q.Level}");
                    }
                });
        }
        ImGui.EndChild();
    }
}
