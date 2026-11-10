using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Энциклопедия профессий: способности по уровням, трейты, ролевые и PvP-навыки, джоб-квесты.</summary>
internal sealed class JobsTab
{
    private sealed record Skill(uint Id, int Level, string Name, uint Icon, string Desc, string Meta);
    private sealed record JobData(List<Skill> Actions, List<Skill> Role, List<Skill> Traits, List<Skill> Pvp, List<(uint Id, int Level, string Name)> Quests);

    private static readonly string[] Sections = { "Способности", "Трейты", "Ролевые", "PvP", "Джоб-квесты" };
    private readonly GuideData data;
    private List<(uint Id, string Abbr, string Name)>? jobs;
    private uint job;
    private int section;
    private string search = "";
    private readonly Dictionary<uint, JobData> cache = new();

    public JobsTab(GuideData data) => this.data = data;

    private void BuildJobs()
    {
        jobs = Plugin.Data.GetExcelSheet<ClassJob>()
            .Where(c => c.JobIndex > 0 && c.Name.ExtractText().Length > 0)
            .OrderBy(c => c.ClassJobCategory.RowId == 30 || c.ClassJobCategory.RowId == 31 ? 0 : 1).ThenBy(c => c.Role).ThenBy(c => c.RowId)
            .Select(c => (c.RowId, GuideData.JobAbbrEnglish(c.RowId), GuideData.Capitalize(c.Name.ExtractText())))
            .ToList();
        job = Plugin.PlayerState.ClassJob.RowId;
        if (jobs.All(j => j.Id != job)) job = jobs.FirstOrDefault().Id;
    }

    private JobData Build(uint jobId)
    {
        if (cache.TryGetValue(jobId, out var d)) return d;
        var abbr = GuideData.JobAbbrEnglish(jobId);
        var cj = Plugin.Data.GetExcelSheet<ClassJob>().GetRow(jobId);
        var parent = cj.ClassJobParent.RowId;
        var transient = Plugin.Data.GetExcelSheet<ActionTransient>();
        var traitT = Plugin.Data.GetExcelSheet<TraitTransient>();
        string Desc(uint id) => transient.GetRowOrDefault(id)?.Description.ExtractText() ?? "";

        string Meta(Lumina.Excel.Sheets.Action a)
        {
            var parts = new List<string>();
            parts.Add(a.Cast100ms == 0 ? "мгновенно" : $"каст {a.Cast100ms / 10.0:0.##} с");
            if (a.Recast100ms > 0) parts.Add($"откат {a.Recast100ms / 10.0:0.##} с");
            if (a.MaxCharges > 1) parts.Add($"{a.MaxCharges} заряда");
            if (a.Range > 0) parts.Add($"дальность {a.Range} м");
            if (a.EffectRange > 0) parts.Add($"радиус {a.EffectRange} м");
            return string.Join(" · ", parts);
        }

        var actions = new List<Skill>();
        var role = new List<Skill>();
        var pvp = new List<Skill>();
        foreach (var a in Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>())
        {
            var name = a.Name.ExtractText();
            if (name.Length == 0 || a.Icon == 0) continue;
            var mine = a.ClassJob.RowId == jobId || (parent != 0 && a.ClassJob.RowId == parent)
                       || (a.ClassJobCategory.RowId != 0 && data.JobInCategory(a.ClassJobCategory.RowId, abbr));
            if (!mine) continue;
            if (a.IsPvP)
            {
                if (a.ClassJobCategory.RowId != 0 && pvp.All(x => x.Name != name))
                    pvp.Add(new Skill(a.RowId, 0, name, a.Icon, Desc(a.RowId), Meta(a)));
                continue;
            }
            if (!a.IsPlayerAction || a.ClassJobLevel == 0) continue;
            var s = new Skill(a.RowId, a.ClassJobLevel, name, a.Icon, Desc(a.RowId), Meta(a));
            if (a.IsRoleAction) { if (role.All(x => x.Name != name)) role.Add(s); }
            else if (actions.All(x => x.Name != name)) actions.Add(s);
        }

        var traits = Plugin.Data.GetExcelSheet<Trait>()
            .Where(t => t.Name.ExtractText().Length > 0 && t.Level > 0
                        && (t.ClassJob.RowId == jobId || (parent != 0 && t.ClassJob.RowId == parent)
                            || (t.ClassJobCategory.RowId != 0 && data.JobInCategory(t.ClassJobCategory.RowId, abbr))))
            .Select(t => new Skill(t.RowId, t.Level, t.Name.ExtractText(), (uint)t.Icon, traitT.GetRowOrDefault(t.RowId)?.Description.ExtractText() ?? "", "трейт"))
            .GroupBy(t => t.Name).Select(g => g.OrderByDescending(x => x.Level).First())
            .OrderBy(t => t.Level).ToList();

        // Джоб-квесты: жанр журнала назван по классу или профессии
        var en = Plugin.Data.GetExcelSheet<ClassJob>(Dalamud.Game.ClientLanguage.English);
        var names = new[] { jobId, parent }.Where(x => x != 0).Select(x => en.GetRowOrDefault(x)?.Name.ExtractText() ?? "")
            .Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var genres = Plugin.Data.GetExcelSheet<JournalGenre>(Dalamud.Game.ClientLanguage.English)
            .Where(g => names.Any(n => g.Name.ExtractText().Equals(n, StringComparison.OrdinalIgnoreCase)
                                       || g.Name.ExtractText().Equals(n + " Quests", StringComparison.OrdinalIgnoreCase)))
            .Select(g => g.RowId).ToHashSet();
        var quests = Plugin.Data.GetExcelSheet<Quest>()
            .Where(q => genres.Contains(q.JournalGenre.RowId) && q.Name.ExtractText().Length > 0)
            .Select(q => (q.RowId, (int)(q.ClassJobLevel.Count > 0 ? q.ClassJobLevel[0] : 0), q.Name.ExtractText()))
            .OrderBy(q => q.Item2).ToList();

        return cache[jobId] = new JobData(actions.OrderBy(a => a.Level).ToList(), role.OrderBy(a => a.Level).ToList(), traits, pvp, quests);
    }

    public void Draw()
    {
        Theme.Header("Профессии", "Любая профессия: способности по уровням, трейты, ролевые и PvP-навыки, джоб-квесты");
        if (jobs is null) BuildJobs();
        if (jobs!.Count == 0) return;

        var cur = jobs.FirstOrDefault(j => j.Id == job);
        ImGui.SetNextItemWidth(240 * Theme.S);
        if (ImGui.BeginCombo("##jobsel", $"{cur.Name} ({cur.Abbr})"))
        {
            foreach (var j in jobs)
                if (ImGui.Selectable($"{j.Name} ({j.Abbr})##js{j.Id}", j.Id == job)) job = j.Id;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Моя профессия##jsme") && PlayerInfo.Ready) job = Plugin.PlayerState.ClassJob.RowId;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Math.Max(160 * Theme.S, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##jssearch", "Поиск по названию или описанию…", ref search, 64);
        Theme.Chips("jssec", Sections, ref section);

        JobData d;
        try { d = Build(job); }
        catch (Exception e) { Plugin.Log.Warning(e, "Профессии: ошибка разбора"); return; }

        if (!ImGui.BeginChild("##jobs", Vector2.Zero)) { ImGui.EndChild(); return; }
        if (section == 4) DrawQuests(d.Quests);
        else
        {
            var list = (section switch { 0 => d.Actions, 1 => d.Traits, 2 => d.Role, _ => d.Pvp })
                .Where(s => search.Length == 0 || s.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || s.Desc.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (list.Count == 0) Theme.EmptyState(FontAwesomeIcon.Search, "Пусто", "Для этой профессии в этом разделе ничего нет.");
            var rowH = 44 * Theme.S;
            Theme.VirtualList(list.Count, rowH, i =>
            {
                var s = list[i];
                var title = s.Level > 0 ? $"Ур. {s.Level} · {NameTranslations.Show(s.Name)}" : NameTranslations.Show(s.Name);
                Theme.Card($"##sk{section}_{s.Id}", s.Icon, title, s.Meta + (s.Desc.Length > 0 ? " — " + s.Desc.Replace("\n", " ") : ""), null, Theme.Gold, false, rowH);
                if (ImGui.IsItemHovered() && s.Desc.Length > 0)
                {
                    ImGui.BeginTooltip();
                    ImGui.PushTextWrapPos(460 * Theme.S);
                    ImGui.TextColored(Theme.Gold, s.Name);
                    if (s.Meta.Length > 0) ImGui.TextColored(Theme.TextDim, s.Meta);
                    ImGui.TextUnformatted(s.Desc);
                    ImGui.PopTextWrapPos();
                    ImGui.EndTooltip();
                }
            });
        }
        ImGui.EndChild();
    }

    private void DrawQuests(List<(uint Id, int Level, string Name)> quests)
    {
        if (quests.Count == 0) { Theme.EmptyState(FontAwesomeIcon.Scroll, "Квестов не найдено", "У этой профессии нет отдельной цепочки в журнале."); return; }
        var done = quests.Count(q => PlayerInfo.IsQuestDone(q.Id));
        Theme.Panel("jsq", () => Theme.Progress("Пройдено джоб-квестов", done, quests.Count, done == quests.Count ? Theme.Green : Theme.Gold));
        foreach (var (id, level, name) in quests)
        {
            var ok = PlayerInfo.IsQuestDone(id);
            var label = $"{(ok ? "✓ " : "")}Ур. {level} · {NameTranslations.Show(name)}##jq{id}";
            ImGui.PushStyleColor(ImGuiCol.Text, ok ? Theme.Green : Theme.Text);
            if (ImGui.Selectable(label)) WikiNav.Open(WikiKind.Quest, id, name);
            ImGui.PopStyleColor();
        }
    }
}
