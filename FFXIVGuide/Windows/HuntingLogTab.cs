using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Hunting Log: журнал охотника по классам и Grand Company — кого убить, сколько и где.</summary>
internal sealed unsafe class HuntingLogTab
{
    private sealed record Target(string Mob, uint NameId, int Need, string Places);
    private sealed record Entry(uint RowId, int Rank, int Index, List<Target> Targets);

    /// <summary>Журналы в том порядке, в каком игра хранит прогресс: ключ строки MonsterNote и название.</summary>
    private static readonly (uint Key, string Name)[] Logs =
    {
        (1, "Gladiator"), (2, "Pugilist"), (3, "Marauder"), (4, "Lancer"), (5, "Archer"), (6, "Conjurer"), (7, "Thaumaturge"),
        (26, "Arcanist"), (29, "Rogue"), (100, "Maelstrom"), (200, "Order of the Twin Adder"), (300, "Immortal Flames"),
    };

    private readonly Dictionary<uint, List<Entry>> logs = new();
    private int pick;
    private string search = "";

    private List<Entry> Build(uint key)
    {
        if (logs.TryGetValue(key, out var l)) return l;
        var res = new List<Entry>();
        foreach (var m in Plugin.Data.GetExcelSheet<MonsterNote>().Where(m => m.RowId / 10000 == key && m.RowId % 10000 != 0))
        {
            var n = (int)(m.RowId % 10000) - 1;
            var targets = new List<Target>();
            for (var i = 0; i < m.MonsterNoteTarget.Count; i++)
            {
                if (m.MonsterNoteTarget[i].RowId == 0 || m.MonsterNoteTarget[i].ValueNullable is not { } t) continue;
                var mob = GuideData.Capitalize(t.BNpcName.ValueNullable?.Singular.ExtractText() ?? "");
                var places = string.Join("; ", t.PlaceNameZone.Select((z, k) =>
                {
                    var zone = z.ValueNullable?.Name.ExtractText() ?? "";
                    var loc = k < t.PlaceNameLocation.Count ? t.PlaceNameLocation[k].ValueNullable?.Name.ExtractText() ?? "" : "";
                    return zone.Length == 0 ? "" : loc.Length > 0 ? $"{zone} ({loc})" : zone;
                }).Where(x => x.Length > 0).Distinct());
                targets.Add(new Target(mob, t.BNpcName.RowId, i < m.Count.Count ? m.Count[i] : 1, places));
            }
            if (targets.Count > 0) res.Add(new Entry(m.RowId, n / 10, n % 10, targets));
        }
        return logs[key] = res;
    }

    public void Focus(string mob)
    {
        search = mob;
        for (var i = 0; i < Logs.Length; i++)
            if (Build(Logs[i].Key).Any(e => e.Targets.Any(t => t.Mob.Equals(mob, StringComparison.OrdinalIgnoreCase)))) { pick = i; break; }
    }

    public IEnumerable<SearchHit> Search(string term, Action<MainTab> go)
    {
        foreach (var (key, name) in Logs)
            foreach (var e in Build(key))
                foreach (var t in e.Targets.Where(t => t.Mob.Contains(term, StringComparison.OrdinalIgnoreCase)))
                    yield return new SearchHit("Hunting Log", t.Mob, $"{name}, ранг {e.Rank + 1} · ×{t.Need} · {t.Places}", 0, () => { Focus(t.Mob); go(MainTab.HuntingLog); });
    }

    public void Draw()
    {
        Theme.Header("Hunting Log", "Журнал охотника: кого убить, сколько и где — по классам и Grand Company");
        if (!PlayerInfo.Ready) { Theme.Panel("hlnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем.")); return; }

        var mgr = MonsterNoteManager.Instance();
        var labels = Logs.Select((l, i) =>
        {
            var rank = mgr != null && i < mgr->RankData.Length ? mgr->RankData[i].Rank : -1;
            return $"{NameTranslations.Show(l.Name)}{(rank >= 0 ? $" · ранг {Math.Min(rank + 1, 5)}" : "")}";
        }).ToArray();
        pick = Math.Clamp(pick, 0, Logs.Length - 1);
        ImGui.SetNextItemWidth(300 * Theme.S);
        if (ImGui.BeginCombo("##hllog", labels[pick]))
        {
            for (var i = 0; i < labels.Length; i++)
                if (ImGui.Selectable($"{labels[i]}##hl{i}", i == pick)) pick = i;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Math.Max(160 * Theme.S, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##hlsearch", "Моб или зона…", ref search, 64);

        List<Entry> entries;
        try { entries = Build(Logs[pick].Key); }
        catch (Exception e) { Plugin.Log.Warning(e, "Hunting Log: ошибка разбора"); return; }

        var info = mgr != null && pick < mgr->RankData.Length ? mgr->RankData[pick] : default;
        var curRank = mgr != null ? info.Rank : -1;
        var gc = pick >= 9;
        Theme.Panel("hltop", () =>
        {
            ImGui.PushTextWrapPos(0);
            ImGui.TextColored(Theme.Gray, gc
                ? "Журнал Grand Company общий для всех классов. Ранг открывается рангом в компании."
                : "Журнал класса: убивай мобов этим классом (или его профессией). За каждую строку — опыт, за ранг целиком — ещё больше.");
            ImGui.PopTextWrapPos();
            if (curRank >= 0) ImGui.TextColored(Theme.Text, $"Текущий ранг: {Math.Min(curRank + 1, 5)} из 5. Прогресс по мобам игра отдаёт только для текущего ранга.");
        });

        bool Match(Target t) => search.Length == 0 || t.Mob.Contains(search, StringComparison.OrdinalIgnoreCase) || t.Places.Contains(search, StringComparison.OrdinalIgnoreCase);

        if (!ImGui.BeginChild("##hunting", Vector2.Zero)) { ImGui.EndChild(); return; }
        foreach (var g in entries.GroupBy(e => e.Rank).OrderBy(g => g.Key))
        {
            var rankDone = curRank > g.Key;
            var visible = g.Where(e => e.Targets.Any(Match)).ToList();
            if (visible.Count == 0) continue;
            Theme.SubHeader($"Ранг {g.Key + 1}" + (rankDone ? " — пройден ✓" : g.Key == curRank ? " — текущий" : ""));
            foreach (var e in visible)
                Theme.Panel($"hl{e.RowId}", () =>
                {
                    foreach (var (t, k) in e.Targets.Select((t, k) => (t, k)))
                    {
                        if (!Match(t)) continue;
                        int have;
                        if (rankDone) have = t.Need;
                        else if (g.Key == curRank && e.Index < info.RankData.Length && k < info.RankData[e.Index].Counts.Length) have = info.RankData[e.Index].Counts[k];
                        else have = 0;
                        var ok = have >= t.Need;
                        Theme.FaIcon(ok ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.Crosshairs, ok ? Theme.Green : Theme.Gold);
                        ImGui.SameLine();
                        ImGui.TextColored(ok ? Theme.Green : Theme.Text, $"{NameTranslations.Show(t.Mob)} — {Math.Min(have, t.Need)}/{t.Need}");
                        if (t.Places.Length > 0)
                        {
                            ImGui.SameLine();
                            ImGui.TextColored(Theme.TextDim, NameTranslations.Show(t.Places));
                        }
                    }
                });
        }
        ImGui.EndChild();
    }
}
