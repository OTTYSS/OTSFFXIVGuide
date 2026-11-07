using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Достижения: прогресс по категориям, что осталось, награды.</summary>
internal sealed unsafe class AchievementsTab
{
    private sealed record Entry(uint Id, string Name, string Description, uint Icon, int Points, string Reward, uint CategoryId);
    private sealed record Category(uint Id, string Name, string Kind, List<Entry> Entries);

    private static readonly string[] ShowNames = { "Не выполненные", "Выполненные", "Все", "Почти выполнены" };
    private readonly Dictionary<uint, (uint Cur, uint Max)> exact = new();
    private uint requested;

    private List<Category>? categories;
    private Category? selected;
    private int show;
    private bool rewardsOnly;
    private string search = "";

    public void Draw()
    {
        Theme.Header("Достижения", "Что выполнено, что осталось и какие награды дают");

        var ach = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement.Instance();
        if (!PlayerInfo.Ready || ach == null || !ach->IsLoaded())
        {
            Theme.Panel("anone", () =>
            {
                Theme.FaIcon(FontAwesomeIcon.Trophy, Theme.GoldDim);
                ImGui.TextColored(Theme.TextDim, "Открой в игре окно достижений (Achievements) один раз — игра загрузит данные, и раздел заполнится.");
                ImGui.TextColored(Theme.Gray, "Плагин сам ничего не запрашивает у сервера, только читает то, что игра уже загрузила.");
            });
            return;
        }

        categories ??= Build();
        bool Done(Entry e) => ach->IsComplete((int)e.Id);

        var all = categories.SelectMany(c => c.Entries).ToList();
        var doneAll = all.Count(Done);

        Theme.Panel("asum", () =>
        {
            Theme.Progress("Всего достижений", doneAll, all.Count, Theme.Gold);
            var points = all.Where(Done).Sum(e => e.Points);
            ImGui.TextColored(Theme.TextDim, $"Очков: {points:N0} из {all.Sum(e => e.Points):N0}");
        });

        var avail = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##acats", new Vector2(avail.X * 0.35f, 0)))
        {
            foreach (var kind in categories.GroupBy(c => c.Kind))
            {
                Theme.SubHeader(kind.Key);
                foreach (var c in kind)
                {
                    var d = c.Entries.Count(Done);
                    if (Theme.ProgressButton($"ac{c.Id}", c.Name, d, c.Entries.Count, selected == c ? Theme.Gold : d == c.Entries.Count ? Theme.Green : Theme.Blue))
                        selected = c;
                }
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("##alist", Vector2.Zero))
        {
            Theme.Chips("ashow", ShowNames, ref show);
            ImGui.SameLine(0, 12 * Theme.S);
            ImGui.Checkbox("Только с наградой", ref rewardsOnly);
            Theme.Search("##asearch", "Поиск по всем достижениям…", ref search);

            if (show == 3)
            {
                DrawNear(all, Done);
                ImGui.EndChild();
                return;
            }
            var source = search.Trim().Length >= 2 ? all : selected?.Entries ?? new List<Entry>();
            var rows = source
                .Where(e => search.Trim().Length < 2 || e.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) || e.Description.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(e => show == 2 || Done(e) == (show == 1))
                .Where(e => !rewardsOnly || e.Reward.Length > 0)
                .ToList();

            if (selected is null && search.Trim().Length < 2)
                ImGui.TextColored(Theme.TextDim, "Выбери категорию слева или введи поиск.");
            else
                ImGui.TextColored(Theme.TextDim, $"Показано: {rows.Count}");

            var rowH = 48 * Theme.S;
            Theme.VirtualList(rows.Count, rowH, i =>
            {
                var e = rows[i];
                var done = Done(e);
                var sub = e.Description + (e.Reward.Length > 0 ? $" · награда: {e.Reward}" : "");
                Theme.Card($"##ae{e.Id}", e.Icon, (done ? "✓ " : "") + e.Name, sub, $"{e.Points} очк.", done ? Theme.Green : Theme.Gray, false, rowH);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(e.Description + (e.Reward.Length > 0 ? $"\nНаграда: {e.Reward}" : ""));
            });
        }
        ImGui.EndChild();
    }

    /// <summary>Прогресс, который можно посчитать без сервера: цепочки достижений, уровень профессии, квесты.</summary>
    private (uint Cur, uint Max)? LocalProgress(Entry e, Func<Entry, bool> done)
    {
        if (exact.TryGetValue(e.Id, out var ex)) return ex;
        var a = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Achievement>().GetRowOrDefault(e.Id);
        if (a is null) return null;
        var data = a.Value.Data.Select(d => d.RowId).Where(d => d != 0).ToList();
        var ach = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement.Instance();
        switch (a.Value.Type)
        {
            case 2 when data.Count > 0:
                return ((uint)data.Count(id => ach->IsComplete((int)id)), (uint)data.Count);
            case 3 when data.Count >= 2:
            {
                var job = Plugin.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(data[0]);
                if (job is null) return null;
                var lvl = (uint)Math.Max(0, (int)Plugin.PlayerState.GetClassJobLevel(job.Value));
                return (Math.Min(lvl, data[1]), data[1]);
            }
            case 6 or 9 when data.Count > 0:
                return ((uint)data.Count(q => PlayerInfo.IsQuestDone(q)), (uint)data.Count);
        }
        return null;
    }

    private void DrawNear(List<Entry> all, Func<Entry, bool> done)
    {
        var ach = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement.Instance();
        // Ответ сервера на запрос точного прогресса (по кнопке)
        if (requested != 0 && ach->ProgressAchievementId == requested && ach->ProgressMax > 0)
        {
            exact[requested] = (ach->ProgressCurrent, ach->ProgressMax);
            requested = 0;
        }

        var rows = all.Where(e => !done(e))
            .Select(e => (e, p: LocalProgress(e, done)))
            .Where(x => x.p is { Max: > 0 } p && p.Cur > 0)
            .Select(x => (x.e, cur: x.p!.Value.Cur, max: x.p!.Value.Max, frac: (double)x.p!.Value.Cur / x.p!.Value.Max))
            .OrderByDescending(x => x.frac).Take(60).ToList();
        ImGui.TextColored(Theme.TextDim, "Считаю сам: цепочки достижений, уровни профессий, квесты. Для остальных — кнопка «Точный прогресс» (как клик по достижению в игре).");
        Theme.Search("##anear", "Найти достижение, чтобы узнать точный прогресс…", ref search);
        if (search.Trim().Length >= 2)
            foreach (var e in all.Where(e => !done(e) && NameTranslations.Matches(e.Name, search.Trim())).Take(8))
            {
                ImGui.TextColored(Theme.Text, NameTranslations.Show(e.Name));
                ImGui.SameLine();
                if (exact.TryGetValue(e.Id, out var ex)) ImGui.TextColored(Theme.Gold, $"{ex.Cur}/{ex.Max}");
                else if (ImGui.SmallButton($"Точный прогресс##ap{e.Id}"))
                {
                    requested = e.Id;
                    ach->RequestAchievementProgress(e.Id);
                }
            }
        ImGui.Dummy(new Vector2(0, 4 * Theme.S));
        if (rows.Count == 0) { ImGui.TextColored(Theme.TextDim, "Нечего показать — нет начатых достижений, прогресс которых можно посчитать."); return; }
        foreach (var (e, cur, max, frac) in rows)
        {
            Theme.Progress($"{NameTranslations.Show(e.Name)} · {e.Points} очк.", (int)cur, (int)max, frac >= 0.8 ? Theme.Green : Theme.Gold);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(e.Description + (e.Reward.Length > 0 ? $"\nНаграда: {e.Reward}" : ""));
        }
    }

    private static List<Category> Build()
    {
        var cats = Plugin.Data.GetExcelSheet<AchievementCategory>();
        var result = new Dictionary<uint, Category>();
        foreach (var a in Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Achievement>())
        {
            if (a.RowId == 0 || a.Name.IsEmpty || a.AchievementCategory.RowId == 0) continue;
            var cat = cats.GetRowOrDefault(a.AchievementCategory.RowId);
            if (cat is null || cat.Value.HideCategory || cat.Value.Name.IsEmpty) continue;

            if (!result.TryGetValue(cat.Value.RowId, out var c))
            {
                var kind = cat.Value.AchievementKind.ValueNullable?.Name.ExtractText() ?? "";
                result[cat.Value.RowId] = c = new Category(cat.Value.RowId, cat.Value.Name.ExtractText(), kind.Length > 0 ? kind : "Прочее", new List<Entry>());
            }

            var reward = new List<string>();
            if (a.Title.RowId != 0 && a.Title.ValueNullable is { } t && !t.Masculine.IsEmpty)
                reward.Add($"титул «{t.Masculine.ExtractText()}»");
            if (a.Item.RowId != 0 && a.Item.ValueNullable is { } it && !it.Name.IsEmpty)
                reward.Add(it.Name.ExtractText());

            c.Entries.Add(new Entry(a.RowId, a.Name.ExtractText(), a.Description.ExtractText(), a.Icon, a.Points, string.Join(", ", reward), cat.Value.RowId));
        }

        foreach (var c in result.Values)
            c.Entries.Sort((x, y) => x.Id.CompareTo(y.Id));
        return result.Values
            .OrderBy(c => cats.GetRowOrDefault(c.Id)?.AchievementKind.ValueNullable?.Order ?? 0)
            .ThenBy(c => cats.GetRowOrDefault(c.Id)?.Order ?? 0)
            .ToList();
    }
}
