using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Masked Carnivale: стадии, недельные задания, враги и их уязвимости (подсказка, какие навыки синего мага брать).</summary>
internal sealed class CarnivaleTab
{
    private sealed record Enemy(string Name, List<string> Vulns, bool Hidden);
    private sealed record Stage(uint Id, int Order, string Name, int Ideal, int Standard, List<List<Enemy>> Acts);

    private List<Stage>? stages;
    private Task<List<Stage>>? building;
    private uint selected;

    private static List<Stage> Build()
    {
        // Названия стадий берём из Duty Finder: «Stage N: …»
        var names = new Dictionary<int, string>();
        foreach (var c in Plugin.Data.GetExcelSheet<ContentFinderCondition>())
        {
            var n = c.Name.ExtractText();
            var m = Regex.Match(n, @"^Stage (\d+):\s*(.+)$");
            if (m.Success) names.TryAdd(int.Parse(m.Groups[1].Value), m.Groups[2].Value);
        }
        var arr = Plugin.Data.GetSubrowExcelSheet<AOZArrangement>();
        var boss = Plugin.Data.GetExcelSheet<AOZBoss>();
        var npcs = Plugin.Data.GetExcelSheet<AOZContentBriefingBNpc>();
        Enemy ToEnemy(AOZContentBriefingBNpc b)
        {
            var v = new List<string>();
            if (b.SlowVuln) v.Add("Замедление");
            if (b.PetrificationVuln) v.Add("Окаменение");
            if (b.ParalysisVuln) v.Add("Паралич");
            if (b.InterruptionVuln) v.Add("Прерывание");
            if (b.BlindVuln) v.Add("Слепота");
            if (b.StunVuln) v.Add("Оглушение");
            if (b.SleepVuln) v.Add("Сон");
            if (b.BindVuln) v.Add("Обездвиживание");
            if (b.HeavyVuln) v.Add("Тяжесть");
            if (b.FlatOrDeathVuln) v.Add("Мгновенная смерть");
            return new Enemy(GuideData.Capitalize(b.BNpcName.ValueNullable?.Singular.ExtractText() ?? "?"), v, b.HideStats);
        }
        List<Enemy> Act(uint id, byte type)
        {
            var res = new List<Enemy>();
            if (id == 0) return res;
            if (type != 1 && boss.GetRowOrDefault(id) is { } bs && bs.Boss.ValueNullable is { } bn) res.Add(ToEnemy(bn));
            else if (arr.HasRow(id))
                foreach (var a in arr.GetRow(id))
                    if (a.AOZContentBriefingBNpc.ValueNullable is { } n) res.Add(ToEnemy(n));
            // Повторяющихся врагов показываем один раз
            return res.GroupBy(e => e.Name).Select(g => g.First()).ToList();
        }
        var list = new List<Stage>();
        foreach (var c in Plugin.Data.GetExcelSheet<AOZContent>())
        {
            if (c.RowId == 0 || c.Order == 0) continue;
            var acts = new List<List<Enemy>> { Act(c.Act1.RowId, c.Act1FightType), Act(c.Act2.RowId, c.Act2FightType), Act(c.Act3.RowId, c.Act3FightType) }
                .Where(a => a.Count > 0).ToList();
            list.Add(new Stage(c.RowId, c.Order, names.GetValueOrDefault(c.Order, $"Стадия {c.Order}"), c.IdealFinishTime, c.StandardFinishTime, acts));
        }
        return list.OrderBy(s => s.Order).ToList();
    }

    private static unsafe bool Completed(uint id)
    {
        var ps = PlayerState.Instance();
        if (ps == null) return false;
        var span = ps->CompletedMaskedCarnivale;
        var i = (int)id;
        return i >> 3 < span.Length && (span[i >> 3] & (1 << (i & 7))) != 0;
    }

    private static unsafe uint[] Weekly()
    {
        var am = AgentModule.Instance();
        if (am == null) return Array.Empty<uint>();
        var ag = am->GetAgentAozContentBriefing();
        if (ag == null) return Array.Empty<uint>();
        return ag->WeeklyAozContentIds.ToArray().Select(b => (uint)b).ToArray();
    }

    private static readonly string[] Difficulty = { "Novice (лёгкое)", "Moderate (среднее)", "Advanced (сложное)" };

    public void Draw()
    {
        Theme.Header("Masked Carnivale", "Стадии синего мага: что пройдено, недельные задания и враги с уязвимостями — по ним подбирай навыки");
        if (stages is null)
        {
            building ??= Task.Run(Build);
            if (!building.IsCompleted) { Theme.Skeleton("mcload", 6, "Собираю стадии…"); return; }
            stages = building.Result;
        }
        if (!PlayerInfo.Ready) { Theme.NeedLogin("Зайди в игру персонажем: прогресс Carnivale берётся из игры."); return; }

        var weekly = Weekly();
        var done = stages.Count(s => Completed(s.Id));
        Theme.Panel("mcsum", () =>
        {
            Theme.Progress("Пройдено стадий", done, stages.Count, Theme.Blue);
            Theme.SubHeader("Задания недели");
            if (weekly.All(w => w == 0))
                ImGui.TextColored(Theme.TextDim, "Открой в игре окно Masked Carnivale (Ul'dah, Arcanists' Guild) один раз — плагин увидит задания недели.");
            for (var i = 0; i < weekly.Length && i < 3; i++)
            {
                var st = stages.FirstOrDefault(s => s.Id == weekly[i]);
                if (st is null) continue;
                ImGui.TextColored(Theme.Gold, Difficulty[i] + ":");
                ImGui.SameLine();
                if (ImGui.Selectable($"Стадия {st.Order}: {st.Name}##mcw{i}", selected == st.Id)) selected = st.Id;
            }
        });

        var idx = stages.FindIndex(s => s.Id == selected);
        Theme.ListKeys(ref idx, stages.Count);
        if (idx >= 0) selected = stages[idx].Id;
        var sel = stages.FirstOrDefault(s => s.Id == selected);

        Theme.MasterDetail("mc", sel is not null, () =>
        {
            foreach (var s in stages)
            {
                var ok = Completed(s.Id);
                var wk = Array.IndexOf(weekly, s.Id);
                if (Theme.Card($"##mcs{s.Id}", 0, $"{s.Order}. {s.Name}", $"идеально ≤ {s.Ideal / 60}:{s.Ideal % 60:00}",
                        wk >= 0 ? "неделя" : ok ? "✓" : null, wk >= 0 ? Theme.Gold : Theme.Green, selected == s.Id, 42 * Theme.S))
                    selected = selected == s.Id ? 0 : s.Id;
            }
        }, () => DrawStage(sel!), () => selected = 0, 0.45f);
    }

    private static void DrawStage(Stage s)
    {
        Theme.SubHeader($"Стадия {s.Order}: {s.Name}");
        ImGui.TextColored(Completed(s.Id) ? Theme.Green : Theme.TextDim, Completed(s.Id) ? "Пройдена ✓" : "Ещё не пройдена");
        ImGui.TextColored(Theme.TextDim, $"Время: идеально ≤ {s.Ideal / 60}:{s.Ideal % 60:00}, норма ≤ {s.Standard / 60}:{s.Standard % 60:00}");
        for (var a = 0; a < s.Acts.Count; a++)
        {
            var act = s.Acts[a];
            Theme.Panel($"mca{a}", () =>
            {
                if (s.Acts.Count > 1) ImGui.TextColored(Theme.GoldDim, $"АКТ {a + 1}");
                foreach (var e in act)
                {
                    Theme.FaIcon(FontAwesomeIcon.Skull, Theme.Red);
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.Text, e.Name);
                    if (e.Hidden) { ImGui.SameLine(); ImGui.TextColored(Theme.Gray, "(характеристики скрыты)"); }
                    else if (e.Vulns.Count > 0)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(Theme.Green, "уязвим: " + string.Join(", ", e.Vulns));
                    }
                }
            });
        }
        ImGui.TextColored(Theme.Gray, "Уязвимости из данных игры: под них бери навыки синего мага с нужным эффектом (сон, оглушение, мгновенная смерть и т.п.).");
    }
}
