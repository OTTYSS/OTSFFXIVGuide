using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

public sealed record CastEvent(DateTime Time, string Ability, string Caster, MechanicEntry? Mechanic);
public sealed record DeathEvent(DateTime Time, string Player, List<CastEvent> RecentCasts);

/// <summary>Запись одного захода в данж/рейд.</summary>
public sealed class DutyRun
{
    public string Duty { get; init; } = "";
    public DateTime Start { get; init; }
    public DateTime? End { get; set; }
    public List<CastEvent> Casts { get; } = new();
    public List<DeathEvent> Deaths { get; } = new();
    public TimeSpan Duration => (End ?? DateTime.Now) - Start;
}

/// <summary>
/// Записывает касты врагов и смерти группы, пока ты в данже. Только чтение видимых объектов.
/// После выхода — краткая сводка в чат и подробный разбор в разделе «Разбор боёв».
/// </summary>
public static class DutyRecorder
{
    private const int KeepRuns = 15;

    private static DutyRun? current;
    private static DutyMechanics? mechanics;
    private static readonly Dictionary<ulong, uint> activeCasts = new();
    private static readonly Dictionary<string, bool> wasDead = new();
    private static DateTime nextTick = DateTime.MinValue;

    public static List<DutyRun> Runs { get; } = new();
    public static DutyRun? Current => current;

    public static void OnTerritoryChanged(uint territory)
    {
        Finish();

        string? duty = null;
        foreach (var cfc in Plugin.Data.GetExcelSheet<ContentFinderCondition>())
            if (cfc.TerritoryType.RowId == territory && !cfc.Name.IsEmpty)
            {
                duty = GuideData.Capitalize(cfc.Name.ExtractText());
                break;
            }
        if (duty is null) return;

        current = new DutyRun { Duty = duty, Start = DateTime.Now };
        mechanics = MechanicsDb.Find(duty);
        activeCasts.Clear();
        wasDead.Clear();
    }

    private static void Finish()
    {
        if (current is null) return;
        current.End = DateTime.Now;
        if (current.Casts.Count > 0 || current.Deaths.Count > 0)
        {
            Runs.Insert(0, current);
            if (Runs.Count > KeepRuns) Runs.RemoveAt(Runs.Count - 1);
            if (current.Deaths.Count > 0)
            {
                var top = current.Deaths
                    .Select(d => d.RecentCasts.LastOrDefault()?.Ability)
                    .Where(a => a is not null)
                    .GroupBy(a => a).OrderByDescending(g => g.Count()).FirstOrDefault();
                Plugin.Chat.Print($"{current.Duty}: смертей {current.Deaths.Count}" +
                                  (top is null ? "" : $", чаще всего после «{top.Key}»") + ". Подробно: /fguide recap", "FFXIV Guide");
            }
        }
        current = null;
        mechanics = null;
    }

    public static void Tick()
    {
        if (current is null || DateTime.Now < nextTick || !PlayerInfo.Ready) return;
        nextTick = DateTime.Now.AddMilliseconds(250);

        // Касты врагов
        var actions = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        foreach (var obj in Plugin.Objects)
        {
            if (obj is not IBattleNpc npc) continue;
            if (!npc.IsCasting)
            {
                activeCasts.Remove(npc.GameObjectId);
                continue;
            }
            if (activeCasts.TryGetValue(npc.GameObjectId, out var id) && id == npc.CastActionId) continue;
            activeCasts[npc.GameObjectId] = npc.CastActionId;

            var name = actions.GetRowOrDefault(npc.CastActionId)?.Name.ExtractText();
            if (string.IsNullOrEmpty(name)) continue;
            var mech = mechanics?.Bosses.SelectMany(b => b.Mechanics).FirstOrDefault(m => Same(m.Ability, name));
            current.Casts.Add(new CastEvent(DateTime.Now, name, npc.Name.TextValue, mech));
        }

        // Смерти группы (или только себя, если играешь соло)
        var members = Plugin.Party.Length > 0
            ? Plugin.Party.Select(p => (p.Name.TextValue, p.CurrentHP)).ToList()
            : Plugin.Objects.LocalPlayer is { } me ? new List<(string, uint)> { (me.Name.TextValue, me.CurrentHp) } : new();

        foreach (var (name, hp) in members)
        {
            var dead = hp == 0;
            if (dead && !wasDead.GetValueOrDefault(name))
            {
                var recent = current.Casts.Where(c => DateTime.Now - c.Time < TimeSpan.FromSeconds(8)).TakeLast(3).ToList();
                current.Deaths.Add(new DeathEvent(DateTime.Now, name, recent));
            }
            wasDead[name] = dead;
        }
    }

    private static bool Same(string a, string b)
    {
        static string N(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var na = N(a);
        var nb = N(b);
        return na.Length > 2 && (na == nb || (na.Contains(nb) && nb.Length > 4));
    }
}
