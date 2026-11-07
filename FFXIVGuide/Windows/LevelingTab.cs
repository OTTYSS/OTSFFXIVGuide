using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Прокачка (что делать на твоём уровне) и гайд по навыкам профессии.</summary>
internal sealed class LevelingTab
{
    private sealed record Skill(string Name, uint Icon, int Level, string Description, bool Trait, uint RowId, uint QuestId);

    private static readonly string[] Parts = { "Что делать сейчас", "Ротация", "Навыки профессии" };
    private static readonly string[] Langs = { "English", "Русский" };

    private readonly GuideData data;
    private readonly QuestsTab quests;
    private readonly DutiesTab duties;
    private int part;
    private int lang = Plugin.Config.QuestTextRussian ? 1 : 0;
    private (uint Job, List<Skill> Skills)? skillCache;

    public LevelingTab(GuideData data, QuestsTab quests, DutiesTab duties)
    {
        this.data = data;
        this.quests = quests;
        this.duties = duties;
    }

    public void Draw()
    {
        Theme.Header("Прокачка и профессия", "Что делать на твоём уровне и какие навыки откроются дальше");

        if (!PlayerInfo.Ready || Plugin.PlayerState.ClassJob.ValueNullable is not { } job)
        {
            Theme.Panel("lnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем."));
            return;
        }

        Theme.Chips("lpart", Parts, ref part);
        if (!ImGui.BeginChild("##lev", Vector2.Zero))
        {
            ImGui.EndChild();
            return;
        }

        if (part == 0) DrawRoute(job);
        else if (part == 1) DrawRotation(job);
        else DrawSkills(job);

        ImGui.EndChild();
    }

    // ───────────── Маршрут прокачки ─────────────

    private void DrawRoute(ClassJob job)
    {
        var level = (int)Plugin.PlayerState.Level;
        var rowH = 44 * Theme.S;
        var jobName = GuideData.Capitalize(job.Name.ExtractText());

        Theme.Panel("lroul", () =>
        {
            Theme.SubHeader("Рулетки сегодня (бонус опыта)");
            var any = false;
            foreach (var item in Checklist.Items.Where(i => i.Key.StartsWith("roulette")))
            {
                var st = Checklist.StateOf(item);
                Theme.FaIcon(st.Done ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.ArrowRight, st.Done ? Theme.Green : Theme.Gold);
                ImGui.SameLine();
                ImGui.TextColored(st.Done ? Theme.TextDim : Theme.Text, item.Name);
                any |= !st.Done;
            }
            ImGui.TextColored(Theme.Gray, any
                ? "Leveling и Main Scenario дают больше всего опыта при прокачке."
                : "Все рулетки на сегодня пройдены.");
        });

        var dungeons = data.Duties
            .Where(d => d.InstanceContentId != 0 && d.Level <= level && d.Level >= level - 4 && !d.HighEnd)
            .Where(d => d.Type.Contains("Dungeon", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.Level)
            .Take(5)
            .ToList();

        Theme.Panel("ldung", () =>
        {
            Theme.SubHeader("Данжи для опыта на твоём уровне");
            if (dungeons.Count == 0)
                ImGui.TextColored(Theme.TextDim, "Нет данжей в диапазоне уровня.");
            foreach (var d in dungeons)
            {
                var (badge, color) = DutiesTab.StatusBadge(d);
                if (Theme.Card($"##ld{d.CfcId}", d.Icon, d.Name, $"Ур. {d.Level} · {d.Type}", badge, color, false, rowH))
                    duties.Show(d);
            }
            ImGui.TextColored(Theme.Gray, "Лучше всего — самый высокий открытый данж своего уровня, повторять его.");
        });

        var parentName = job.ClassJobParent.ValueNullable?.Name.ExtractText() ?? "";
        var jobQuests = data.Quests.Values
            .Where(q => q.Genre.Contains(job.Name.ExtractText(), StringComparison.OrdinalIgnoreCase)
                        || (parentName.Length > 0 && q.Genre.Contains(parentName, StringComparison.OrdinalIgnoreCase)))
            .Where(q => !PlayerInfo.IsQuestDone(q.Id))
            .OrderBy(q => q.Level)
            .Take(6)
            .ToList();

        Theme.Panel("ljobq", () =>
        {
            Theme.SubHeader($"Квесты профессии ({jobName})");
            if (jobQuests.Count == 0)
                ImGui.TextColored(Theme.TextDim, "Все квесты профессии выполнены или не найдены.");
            foreach (var q in jobQuests)
            {
                var (badge, color) = QuestsTab.StateBadge(q);
                if (Theme.Card($"##lq{q.Id}", q.Icon, q.Name, $"Ур. {q.Level} · {q.Place}" + (q.Level <= level ? "" : " · ещё рано"), badge, color, false, rowH))
                    quests.Show(q);
            }
            ImGui.TextColored(Theme.Gray, "Квесты профессии открывают новые навыки — не откладывай их.");
        });

        Theme.Panel("ltips", () =>
        {
            Theme.SubHeader("Ещё источники опыта");
            Tip("Сюжетные и побочные квесты своего уровня — вкладка «Квесты», фильтр «Доступны сейчас».");
            Tip("FATE в зонах своего уровня — раздел «FATE и атэриты».");
            Tip("Hunt Log (журнал охоты) и Grand Company Hunt Log — заметный опыт до 50.");
            Tip("Отдохнувший опыт (Rested) копится в городах и гостиницах — не трать его впустую.");
            Tip("Еда с бонусом опыта (+3%) и кольца/серьги Brand New дают дополнительный опыт.");
        });
    }

    private static void DrawRotation(ClassJob job)
    {
        var abbr = GuideData.JobAbbrEnglish(job.RowId);
        var parent = job.ClassJobParent.RowId == 0 ? "" : GuideData.JobAbbrEnglish(job.ClassJobParent.RowId);
        var sections = JobGuides.For(abbr, parent, job.Role);
        if (sections.Length == 0)
        {
            Theme.Panel("rotnone", () => ImGui.TextColored(Theme.TextDim, "Для этой профессии гайда пока нет."));
            return;
        }

        foreach (var sec in sections)
        {
            Theme.Panel($"rot{sec.Title}", () =>
            {
                Theme.SubHeader(sec.Title);
                foreach (var p in sec.Points)
                {
                    Theme.FaIcon(FontAwesomeIcon.ArrowRight, Theme.Gold);
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.Text, p);
                }
            });
        }
        ImGui.TextColored(Theme.Gray, "Названия навыков — как в клиенте игры. Точные уровни открытия — во вкладке «Навыки профессии».");
    }

    private static void Tip(string text)
    {
        Theme.FaIcon(FontAwesomeIcon.Star, Theme.GoldDim);
        ImGui.SameLine();
        ImGui.TextColored(Theme.Text, text);
    }

    // ───────────── Навыки профессии ─────────────

    private void DrawSkills(ClassJob job)
    {
        if (skillCache?.Job != job.RowId)
            skillCache = (job.RowId, BuildSkills(job));
        var skills = skillCache.Value.Skills;
        var level = (int)Plugin.PlayerState.Level;

        Theme.Chips("llang", Langs, ref lang);
        ImGui.SameLine(0, 12 * Theme.S);
        ImGui.TextColored(Theme.TextDim, $"{GuideData.Capitalize(job.Name.ExtractText())}: навыков и трейтов — {skills.Count}");

        var next = skills.FirstOrDefault(s => s.Level > level);
        if (next is not null)
            Theme.Panel("lnext", () =>
            {
                Theme.SubHeader("Следующее на очереди");
                SkillRow(next, level);
            });

        foreach (var group in skills.GroupBy(s => s.Level <= level ? "Уже доступно" : "Откроется позже"))
        {
            Theme.Panel($"lg{group.Key}", () =>
            {
                Theme.SubHeader(group.Key);
                foreach (var s in group)
                    SkillRow(s, level);
            });
        }
    }

    private void SkillRow(Skill s, int level)
    {
        ImGui.PushID((int)s.RowId + (s.Trait ? 100000 : 0));
        var h = ImGui.GetTextLineHeight() * 2.2f;
        var pos = ImGui.GetCursorScreenPos();
        Theme.GameIcon(s.Icon, pos, h, 4f);
        ImGui.SetCursorScreenPos(pos + new Vector2(h + 8 * Theme.S, 0));
        ImGui.BeginGroup();
        ImGui.TextColored(s.Level <= level ? Theme.Text : Theme.TextDim, s.Name);
        ImGui.SameLine();
        Theme.InlineBadge($"ур. {s.Level}", s.Level <= level ? Theme.Green : Theme.Gray);
        if (s.Trait) { ImGui.SameLine(); Theme.InlineBadge("трейт", Theme.Blue); }
        if (s.QuestId != 0 && data.Quests.TryGetValue(s.QuestId, out var q))
        {
            ImGui.SameLine();
            var done = PlayerInfo.IsQuestDone(q.Id);
            if (ImGui.SmallButton(done ? $"квест ✓##q{s.RowId}" : $"квест: {q.Name}##q{s.RowId}"))
                quests.Show(q);
        }
        var desc = s.Description;
        if (lang == 1 && desc.Length > 0)
        {
            Translator.Instance.Request([desc]);
            desc = Translator.Instance.Get(desc) ?? desc;
        }
        if (desc.Length > 0)
            ImGui.TextColored(Theme.TextDim, desc);
        ImGui.EndGroup();
        ImGui.SetCursorScreenPos(new Vector2(pos.X, Math.Max(ImGui.GetCursorScreenPos().Y, pos.Y + h) + 4 * Theme.S));
        ImGui.Dummy(Vector2.Zero);
        ImGui.PopID();
    }

    private static List<Skill> BuildSkills(ClassJob job)
    {
        var ids = new HashSet<uint> { job.RowId };
        if (job.ClassJobParent.RowId != 0) ids.Add(job.ClassJobParent.RowId);

        var list = new List<Skill>();
        var actionT = Plugin.Data.GetExcelSheet<ActionTransient>();
        foreach (var a in Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>())
        {
            if (!a.IsPlayerAction || a.IsPvP || a.ClassJobLevel == 0 || !ids.Contains(a.ClassJob.RowId)) continue;
            var name = a.Name.ExtractText();
            if (name.Length == 0) continue;
            var desc = Clean(actionT.GetRowOrDefault(a.RowId)?.Description.ExtractText() ?? "");
            list.Add(new Skill(name, a.Icon, a.ClassJobLevel, desc, false, a.RowId, a.UnlockLink.RowId));
        }

        var traitT = Plugin.Data.GetExcelSheet<TraitTransient>();
        foreach (var t in Plugin.Data.GetExcelSheet<Trait>())
        {
            if (t.Level == 0 || !ids.Contains(t.ClassJob.RowId)) continue;
            var name = t.Name.ExtractText();
            if (name.Length == 0) continue;
            var desc = Clean(traitT.GetRowOrDefault(t.RowId)?.Description.ExtractText() ?? "");
            list.Add(new Skill(name, (uint)Math.Max(0, t.Icon), t.Level, desc, true, t.RowId, t.Quest.RowId));
        }

        return list.OrderBy(s => s.Level).ThenBy(s => s.Trait).ThenBy(s => s.Name).ToList();
    }

    /// <summary>Описания из игры бывают длинными — берём первые предложения.</summary>
    private static string Clean(string s)
    {
        s = s.Replace("\n", " ").Trim();
        return s.Length > 260 ? s[..260] + "…" : s;
    }
}
