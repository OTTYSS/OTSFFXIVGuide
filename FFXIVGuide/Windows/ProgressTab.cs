using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Завершённость аккаунта в процентах по всем категориям, со ссылками на вкладки.</summary>
internal sealed unsafe class ProgressTab
{
    private sealed record Line(string Group, string Name, FontAwesomeIcon Icon, int Done, int Total, MainTab? Tab, string Note = "");

    private readonly GuideData data;
    private readonly Action<MainTab> go;
    private List<Line> lines = new();
    private DateTime next;

    public ProgressTab(GuideData data, Action<MainTab> go)
    {
        this.data = data;
        this.go = go;
    }

    private void Compute()
    {
        var res = new List<Line>();
        var ps = PlayerState.Instance();

        // Сюжет и квесты
        var msq = data.Quests.Values.Where(q => q.IsMsq).GroupBy(q => q.Name).ToList();
        res.Add(new Line("Сюжет", "Основной сюжет (MSQ)", FontAwesomeIcon.Book, msq.Count(g => g.Any(q => PlayerInfo.IsQuestDone(q.Id))), msq.Count, MainTab.Story));
        var unlockQ = data.Quests.Values.Where(q => q.Unlocks.Length > 0).ToList();
        res.Add(new Line("Сюжет", "Квесты, открывающие контент", FontAwesomeIcon.Unlock, unlockQ.Count(q => PlayerInfo.IsQuestDone(q.Id)), unlockQ.Count, MainTab.Quests));

        // Бои по типам
        foreach (var g in data.Duties.GroupBy(d => d.Type).OrderByDescending(g => g.Count()))
            if (g.Key.Length > 0 && g.Count() >= 3)
                res.Add(new Line("Бои", g.Key, FontAwesomeIcon.Dungeon, g.Count(PlayerInfo.IsDutyCompleted), g.Count(), MainTab.Duties));

        // Коллекции
        var u = Plugin.Unlocks;
        var mounts = Plugin.Data.GetExcelSheet<Mount>().Where(m => m.Icon != 0 && m.Singular.ExtractText().Length > 0).ToList();
        res.Add(new Line("Коллекции", "Маунты", FontAwesomeIcon.Horse, mounts.Count(u.IsMountUnlocked), mounts.Count, MainTab.MountFarm));
        var minions = Plugin.Data.GetExcelSheet<Companion>().Where(m => m.Icon != 0 && m.Singular.ExtractText().Length > 0).ToList();
        res.Add(new Line("Коллекции", "Миньоны", FontAwesomeIcon.Paw, minions.Count(u.IsCompanionUnlocked), minions.Count, MainTab.Collections));
        var ach = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement.Instance();
        if (ach != null && ach->IsLoaded())
        {
            var all = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Achievement>().Where(a => a.RowId > 0 && a.Name.ExtractText().Length > 0).ToList();
            res.Add(new Line("Коллекции", "Достижения", FontAwesomeIcon.Trophy, all.Count(a => ach->IsComplete((int)a.RowId)), all.Count, MainTab.Achievements));
        }
        else res.Add(new Line("Коллекции", "Достижения", FontAwesomeIcon.Trophy, 0, 0, MainTab.Achievements, "открой окно достижений в игре один раз"));

        // Мир
        if (ps != null)
        {
            var zones = Plugin.Data.GetExcelSheet<AetherCurrentCompFlgSet>().Where(z => z.Territory.RowId != 0 && z.AetherCurrents.Any(a => a.RowId != 0)).ToList();
            res.Add(new Line("Мир", "Зоны с открытым полётом", FontAwesomeIcon.Wind, zones.Count(z => ps->IsAetherCurrentZoneComplete(z.RowId)), zones.Count, MainTab.Wiki));
            var books = Plugin.Data.GetExcelSheet<SecretRecipeBook>().Where(b => b.Name.ExtractText().Length > 0).ToList();
            res.Add(new Line("Ремесло", "Книги мастер-рецептов", FontAwesomeIcon.BookOpen, books.Count(b => ps->IsSecretRecipeBookUnlocked(b.RowId)), books.Count, MainTab.Craft));
        }

        // Профессии
        var jobs = Plugin.Data.GetExcelSheet<ClassJob>().Where(c => c.JobIndex > 0 && c.ExpArrayIndex >= 0 && c.Name.ExtractText().Length > 0).ToList();
        const int max = 100;
        res.Add(new Line("Профессии", $"Профессии на {max} уровне", FontAwesomeIcon.UserGraduate, jobs.Count(j => Plugin.PlayerState.GetClassJobLevel(j) >= max), jobs.Count, MainTab.Leveling));
        var combat = jobs.Where(j => j.ClassJobCategory.RowId is 30 or 31).ToList();
        res.Add(new Line("Профессии", "Боевые профессии, открытые хотя бы на 1 уровне", FontAwesomeIcon.ShieldAlt, combat.Count(j => Plugin.PlayerState.GetClassJobLevel(j) > 0), combat.Count, MainTab.Jobs));
        lines = res;
    }

    public void Draw()
    {
        Theme.Header("Прогресс аккаунта", "Насколько закрыт аккаунт: сюжет, бои, коллекции, мир и профессии");
        if (!PlayerInfo.Ready) { Theme.Panel("pgnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем.")); return; }
        if (DateTime.Now >= next)
        {
            next = DateTime.Now.AddSeconds(10);
            try { Compute(); }
            catch (Exception e) { Plugin.Log.Warning(e, "Прогресс: ошибка подсчёта"); }
        }

        var counted = lines.Where(l => l.Total > 0).ToList();
        var overall = counted.Count == 0 ? 0 : counted.Average(l => (double)l.Done / l.Total);
        Theme.Panel("pgtop", () =>
        {
            Theme.LargeText(Theme.Gold, $"{overall * 100:0.0}%");
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextDim, "— среднее по всем категориям ниже (каждая категория весит одинаково)");
        });

        if (!ImGui.BeginChild("##progress", Vector2.Zero)) { ImGui.EndChild(); return; }
        foreach (var g in lines.GroupBy(l => l.Group))
            Theme.Panel($"pg{g.Key}", () =>
            {
                Theme.SubHeader(g.Key);
                foreach (var l in g)
                {
                    Theme.FaIcon(l.Icon, Theme.GoldDim);
                    ImGui.SameLine();
                    if (l.Tab is { } tab)
                    {
                        if (ImGui.SmallButton($"→##pgo{l.Name}")) go(tab);
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Открыть вкладку");
                        ImGui.SameLine();
                    }
                    if (l.Total == 0)
                    {
                        ImGui.TextColored(Theme.TextDim, $"{l.Name}: {l.Note}");
                    }
                    else
                    {
                        var pct = (float)l.Done / l.Total;
                        Theme.Progress($"{l.Name} · {pct * 100:0}%", l.Done, l.Total, pct >= 1 ? Theme.Green : pct >= 0.5f ? Theme.Gold : Theme.Blue);
                    }
                }
            });
        ImGui.EndChild();
    }
}
