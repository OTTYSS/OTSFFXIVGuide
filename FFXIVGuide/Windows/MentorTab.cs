using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;
using AchievementState = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement;

namespace FFXIVGuide.Windows;

/// <summary>
/// Что осталось до короны ментора. Требования (достижения) — по Console Games Wiki, страница
/// «Mentor System and Novice Network»; номера достижений сверены с файлами игры.
/// </summary>
internal sealed unsafe class MentorTab
{
    /// <summary>Требование: достижение, как считать прогресс без сервера (уровень профессии / квест / похвалы).</summary>
    private sealed record Req(uint Achievement, string Hint, uint Job = 0, string? Quest = null, bool Commendations = false);
    private sealed record Group(string Title, bool All, Req[] Reqs);

    private static readonly Group[] Battle =
    {
        new("Все обязательны", true, new[]
        {
            new Req(746, "Пройти 1000 данжей, триалов или рейдов"),
            new Req(1035, "Получить 1500 похвал от игроков", Commendations: true),
            new Req(3499, "Ролевой квест танков", Quest: "Dreams of a New Day"),
            new Req(3500, "Ролевой квест хилеров", Quest: "An Antidote for Anarchy"),
        }),
        new("Любой один", false, new[]
        {
            new Req(3501, "Ролевой квест мили-DPS", Quest: "A Hunter True"),
            new Req(3502, "Ролевой квест физ. рейндж-DPS", Quest: "The Mightiest Shield"),
            new Req(3503, "Ролевой квест маг-DPS", Quest: "Heroes and Pretenders"),
        }),
    };

    private static readonly Group[] Trade =
    {
        new("Все обязательны", true, new[]
        {
            new Req(1292, "Собрать или поймать 300 коллекционных предметов"),
            new Req(1289, "Скрафтить 100 коллекционных предметов"),
        }),
        new("Любой один собиратель 100 ур.", false, new[]
        {
            new Req(3460, "Miner 100", Job: 16), new Req(3459, "Botanist 100", Job: 17), new Req(3461, "Fisher 100", Job: 18),
        }),
        new("Любой один ремесленник 100 ур.", false, new[]
        {
            new Req(3451, "Carpenter 100", Job: 8), new Req(3452, "Blacksmith 100", Job: 9), new Req(3453, "Armorer 100", Job: 10),
            new Req(3454, "Goldsmith 100", Job: 11), new Req(3455, "Leatherworker 100", Job: 12), new Req(3456, "Weaver 100", Job: 13),
            new Req(3457, "Alchemist 100", Job: 14), new Req(3458, "Culinarian 100", Job: 15),
        }),
    };

    private readonly Dictionary<uint, (uint Cur, uint Max)> exact = new();
    private Dictionary<string, uint>? questIds;
    private uint requested;

    private uint QuestId(string name)
    {
        questIds ??= Plugin.Data.GetExcelSheet<Quest>(Dalamud.Game.ClientLanguage.English)
            .Where(q => !q.Name.IsEmpty)
            .GroupBy(q => q.Name.ExtractText().Replace('’', '\''))
            .ToDictionary(g => g.Key, g => g.First().RowId);
        return questIds.GetValueOrDefault(name);
    }

    private static string AchName(uint id) =>
        Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Achievement>().GetRowOrDefault(id)?.Name.ExtractText() ?? $"#{id}";

    public void Draw()
    {
        Theme.Header("Ментор", "Что осталось до короны ментора: боевой и ремесленный");
        var ach = AchievementState.Instance();
        var ps = PlayerState.Instance();
        if (!PlayerInfo.Ready || ach == null || ps == null) { Theme.Panel("mnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем.")); return; }

        if (requested != 0 && ach->ProgressAchievementId == requested && ach->ProgressMax > 0)
        {
            exact[requested] = (ach->ProgressCurrent, ach->ProgressMax);
            requested = 0;
        }
        var loaded = ach->IsLoaded();

        Theme.Panel("mtop", () =>
        {
            ImGui.PushTextWrapPos(0);
            if (ps->IsBattleMentor()) ImGui.TextColored(Theme.Green, "✓ Ты боевой ментор.");
            if (ps->IsTradeMentor()) ImGui.TextColored(Theme.Green, "✓ Ты ремесленный ментор.");
            ImGui.TextColored(Theme.Gray, "Когда все условия выполнены, корону выдаёт NPC The Smith (New Gridania, Limsa Lominsa Upper Decks, Ul'dah — Steps of Nald и др.).");
            if (!loaded)
                ImGui.TextColored(Theme.Gold, "Открой окно достижений в игре один раз — без него плагин не знает, какие достижения получены. Уровни, квесты и похвалы видны и так.");
            ImGui.TextColored(Theme.TextDim, "Условия — по Console Games Wiki (актуальны для Dawntrail); номера достижений сверены с файлами игры.");
            ImGui.PopTextWrapPos();
        });

        if (!ImGui.BeginChild("##mentor", Vector2.Zero)) { ImGui.EndChild(); return; }
        DrawSet("Боевой ментор", Battle, ach, ps, loaded);
        DrawSet("Ремесленный ментор", Trade, ach, ps, loaded);
        ImGui.EndChild();
    }

    private void DrawSet(string title, Group[] groups, AchievementState* ach, PlayerState* ps, bool loaded)
    {
        var groupOk = groups.Select(g => g.All ? g.Reqs.All(r => Done(r, ach, ps, loaded)) : g.Reqs.Any(r => Done(r, ach, ps, loaded))).ToArray();
        Theme.Panel($"mset{title}", () =>
        {
            var ok = groupOk.All(x => x);
            Theme.LargeText(ok ? Theme.Green : Theme.Gold, (ok ? "✓ " : "") + title);
            Theme.Progress("Групп условий выполнено", groupOk.Count(x => x), groups.Length, ok ? Theme.Green : Theme.Gold);
            for (var gi = 0; gi < groups.Length; gi++)
            {
                var g = groups[gi];
                Theme.SubHeader((groupOk[gi] ? "✓ " : "") + g.Title);
                foreach (var r in g.Reqs) DrawReq(r, ach, ps, loaded);
            }
        });
    }

    private bool Done(Req r, AchievementState* ach, PlayerState* ps, bool loaded)
    {
        if (loaded && ach->IsComplete((int)r.Achievement)) return true;
        var p = Progress(r, ps);
        return p is { } v && v.Max > 0 && v.Cur >= v.Max;
    }

    private (uint Cur, uint Max)? Progress(Req r, PlayerState* ps)
    {
        if (exact.TryGetValue(r.Achievement, out var ex)) return ex;
        if (r.Job != 0 && Plugin.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(r.Job) is { } job)
            return ((uint)Math.Max(0, (int)Plugin.PlayerState.GetClassJobLevel(job)), 100);
        if (r.Quest is not null && QuestId(r.Quest) is var q and not 0)
            return (PlayerInfo.IsQuestDone(q) ? 1u : 0u, 1);
        if (r.Commendations) return ((uint)Math.Max(0, (int)ps->PlayerCommendations), 1500);
        return null;
    }

    private void DrawReq(Req r, AchievementState* ach, PlayerState* ps, bool loaded)
    {
        var done = Done(r, ach, ps, loaded);
        Theme.FaIcon(done ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.Circle, done ? Theme.Green : Theme.Gray);
        ImGui.SameLine();
        ImGui.TextColored(done ? Theme.Green : Theme.Text, NameTranslations.Show(AchName(r.Achievement)));
        ImGui.SameLine();
        var q = r.Quest is not null ? $" — «{NameTranslations.Show(r.Quest)}»" : "";
        ImGui.TextColored(Theme.TextDim, r.Hint + q);
        if (done) return;

        var p = Progress(r, ps);
        if (p is { Max: > 1 } v)
            Theme.Progress("", (int)Math.Min(v.Cur, v.Max), (int)v.Max, Theme.Gold);
        else if (p is null)
        {
            ImGui.Indent(22 * Theme.S);
            if (ImGui.SmallButton($"Узнать прогресс##mp{r.Achievement}"))
            {
                requested = r.Achievement;
                ach->RequestAchievementProgress(r.Achievement);
            }
            ImGui.SameLine();
            ImGui.TextColored(Theme.Gray, "(как клик по достижению в игре)");
            ImGui.Unindent(22 * Theme.S);
        }
    }
}
