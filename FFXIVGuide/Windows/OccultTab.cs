using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Occult Crescent: фантомные профессии (способности и трейты по уровням), критические события и Forked Tower.</summary>
internal sealed class OccultTab
{
    private sealed record Skill(int Level, string Name, uint Icon, string Desc, bool Trait);
    private sealed record PhantomJob(uint Id, string Name, string Desc, int LevelMax, List<Skill> Skills);
    private sealed record Ce(uint Id, string Name, string Desc, int Players, bool Tower);

    private static readonly string[] Sections = { "Фантомные профессии", "Критические события", "Как играть" };
    private int section;
    private int jobIndex;
    private string search = "";
    private List<PhantomJob>? jobs;
    private List<Ce>? ces;

    /// <summary>Строки DynamicEvent до этого номера — Bozja и Zadnor, дальше — Occult Crescent.</summary>
    private const uint FirstOccultEvent = 45;

    private void Build()
    {
        var actionT = Plugin.Data.GetExcelSheet<ActionTransient>();
        var traits = Plugin.Data.GetExcelSheet<MKDTrait>().ToList();
        jobs = new List<PhantomJob>();
        foreach (var j in Plugin.Data.GetExcelSheet<MKDSupportJob>())
        {
            var name = j.Name.ExtractText();
            if (name.Length == 0) continue;
            var skills = new List<Skill>();
            foreach (var x in j.Actions)
            {
                if (x.Action.RowId == 0 || x.Action.ValueNullable is not { } a) continue;
                skills.Add(new Skill(x.LevelUnlock, a.Name.ExtractText(), a.Icon, actionT.GetRowOrDefault(a.RowId)?.Description.ExtractText() ?? "", false));
            }
            foreach (var t in traits.Where(t => t.MKDSupportJob.RowId == j.RowId && t.Description.ExtractText().Length > 0))
                skills.Add(new Skill(t.LevelUnlock, "Трейт", (uint)t.Icon, t.Description.ExtractText(), true));
            jobs.Add(new PhantomJob(j.RowId, name, j.Description.ExtractText(), j.LevelMax, skills.OrderBy(s => s.Level).ThenBy(s => s.Trait).ToList()));
        }

        ces = Plugin.Data.GetExcelSheet<DynamicEvent>()
            .Where(e => e.RowId >= FirstOccultEvent && e.Name.ExtractText().Length > 0)
            .Select(e =>
            {
                var n = e.Name.ExtractText();
                return new Ce(e.RowId, n, e.Description.ExtractText(), e.MaxParticipants, n.Contains("Forked Tower", StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
    }

    public void Draw()
    {
        Theme.Header("Occult Crescent", "Фантомные профессии, критические события и Forked Tower");
        if (jobs is null)
        {
            try { Build(); }
            catch (Exception e) { Plugin.Log.Warning(e, "Occult Crescent: не удалось разобрать таблицы"); jobs = new(); ces = new(); }
        }
        Theme.Chips("ocsec", Sections, ref section);
        if (!ImGui.BeginChild("##occult", Vector2.Zero)) { ImGui.EndChild(); return; }
        switch (section)
        {
            case 0: DrawJobs(); break;
            case 1: DrawCes(); break;
            default: DrawGuide(); break;
        }
        ImGui.EndChild();
    }

    private void DrawJobs()
    {
        if (jobs!.Count == 0) { Theme.EmptyState(FontAwesomeIcon.HatWizard, "Нет данных", "Фантомные профессии не найдены в файлах игры."); return; }
        jobIndex = Math.Clamp(jobIndex, 0, jobs.Count - 1);
        Theme.Panel("ocjobs", () =>
        {
            ImGui.TextColored(Theme.Gray, "Фантомная профессия надевается поверх обычной и даёт свои способности. Уровень растёт за опыт в Occult Crescent; уровни игра плагину не сообщает.");
            for (var i = 0; i < jobs.Count; i++)
            {
                if (i > 0) ImGui.SameLine();
                if (ImGui.GetContentRegionAvail().X < 120 * Theme.S) ImGui.NewLine();
                var sel = i == jobIndex;
                if (sel) ImGui.PushStyleColor(ImGuiCol.Button, Theme.GoldDim);
                if (ImGui.SmallButton($"{NameTranslations.Show(jobs[i].Name).Replace("Phantom ", "")}##ocj{i}")) jobIndex = i;
                if (sel) ImGui.PopStyleColor();
            }
        });

        var j = jobs[jobIndex];
        Theme.Panel("ocjob", () =>
        {
            Theme.LargeText(Theme.Gold, NameTranslations.Show(j.Name));
            ImGui.TextColored(Theme.TextDim, $"Максимальный уровень: {j.LevelMax}");
            if (j.Desc.Length > 0) ImGui.TextWrapped(j.Desc);
        });
        Theme.Panel("ocskills", () =>
        {
            Theme.SubHeader("Способности и трейты по уровням");
            foreach (var s in j.Skills)
            {
                var rowH = 40 * Theme.S;
                Theme.Card($"##ocs{j.Id}{s.Name}{s.Level}", s.Icon, s.Trait ? $"Ур. {s.Level} · трейт" : $"Ур. {s.Level} · {s.Name}",
                    s.Desc.Replace("\n", " "), null, Theme.Gold, false, rowH);
                if (ImGui.IsItemHovered() && s.Desc.Length > 0)
                {
                    ImGui.BeginTooltip();
                    ImGui.PushTextWrapPos(420 * Theme.S);
                    ImGui.TextUnformatted(s.Desc);
                    ImGui.PopTextWrapPos();
                    ImGui.EndTooltip();
                }
            }
        });
    }

    private void DrawCes()
    {
        ImGui.SetNextItemWidth(Math.Min(360 * Theme.S, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##ocsearch", "Поиск по названию…", ref search, 64);
        var list = ces!.Where(c => search.Length == 0 || c.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                                   || NameTranslations.Show(c.Name).Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        if (list.Count == 0) { Theme.EmptyState(FontAwesomeIcon.Bolt, "Ничего не найдено", "Попробуй другое название."); return; }
        ImGui.TextColored(Theme.TextDim, $"Событий: {list.Count}. Критические события (CE) появляются на карте; набор участников — у отметки, пока идёт таймер.");
        foreach (var c in list)
            Theme.Panel($"occe{c.Id}", () =>
            {
                Theme.FaIcon(c.Tower ? FontAwesomeIcon.Chess : FontAwesomeIcon.Bolt, c.Tower ? Theme.Red : Theme.Gold);
                ImGui.SameLine();
                ImGui.TextColored(c.Tower ? Theme.Red : Theme.Gold, NameTranslations.Show(c.Name));
                ImGui.SameLine();
                ImGui.TextColored(Theme.Gray, c.Tower ? $"рейд на {c.Players} игроков" : c.Players > 0 ? $"до {c.Players} игроков" : "");
                if (c.Desc.Length > 0) ImGui.TextWrapped(c.Desc);
            });
    }

    private static void DrawGuide()
    {
        Theme.Panel("ocg1", () =>
        {
            Theme.SubHeader("С чего начать");
            ImGui.TextWrapped("Occult Crescent открывается после сюжета Dawntrail на 100 уровне. Внутри зоны свой уровень знаний (Knowledge Level) и уровни фантомных профессий; обычная профессия не меняется.");
            ImGui.TextWrapped("Knowledge Level растёт за опыт в зоне и повышает силу персонажа внутри неё — его стоит поднимать в первую очередь.");
        });
        Theme.Panel("ocg2", () =>
        {
            Theme.SubHeader("Что фармить");
            ImGui.TextWrapped("Критические события (CE) дают больше всего опыта и лучшие награды — следи за их отметками на карте. Между ними — FATE и мобы.");
            ImGui.TextWrapped("Forked Tower — рейд на 48 игроков внутри зоны, главные награды — оттуда. Описание каждого события — во вкладке «Критические события».");
        });
    }
}
