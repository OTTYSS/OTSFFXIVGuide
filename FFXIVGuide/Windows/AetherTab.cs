using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Кристаллы телепорта, городские кристаллы и течения эфира: что не открыто в каждой зоне, с флагами на карте.</summary>
internal sealed class AetherTab
{
    private sealed record Crystal(uint Id, string Name, bool Main, uint LevelId);
    private sealed record Current(uint Id, string Name, uint QuestId, uint LevelId);
    private sealed record Zone(uint Territory, string Name, string Expansion, int ExOrder, List<Crystal> Crystals, uint CompSet, List<Current> Currents);

    private List<Zone>? zones;
    private bool building;
    private uint selected;
    private bool onlyMissing = true;
    private string search = "";

    private void Build()
    {
        if (building) return;
        building = true;
        Task.Run(() =>
        {
            try
            {
                var terr = Plugin.Data.GetExcelSheet<TerritoryType>();
                // Позиции объектов (течений) по таблице Level
                var levelByObject = new Dictionary<uint, uint>();
                foreach (var l in Plugin.Data.GetExcelSheet<Level>())
                    if (l.Object.RowId != 0) levelByObject.TryAdd(l.Object.RowId, l.RowId);
                var eobjByData = new Dictionary<uint, uint>();
                foreach (var e in Plugin.Data.GetExcelSheet<EObj>())
                    if (e.Data.RowId != 0) eobjByData.TryAdd(e.Data.RowId, e.RowId);

                var res = new Dictionary<uint, Zone>();
                Zone Get(uint t)
                {
                    if (res.TryGetValue(t, out var z)) return z;
                    var tr = terr.GetRowOrDefault(t);
                    var ex = tr?.ExVersion.ValueNullable;
                    z = new Zone(t, tr?.PlaceName.ValueNullable?.Name.ExtractText() ?? $"#{t}", ex?.Name.ExtractText() ?? "", (int)(ex?.RowId ?? 0), new(), 0, new());
                    res[t] = z;
                    return z;
                }

                foreach (var a in Plugin.Data.GetExcelSheet<Aetheryte>())
                {
                    if (a.Territory.RowId == 0 || a.Invisible) continue;
                    var name = a.IsAetheryte ? a.PlaceName.ValueNullable?.Name.ExtractText() : a.AethernetName.ValueNullable?.Name.ExtractText();
                    if (string.IsNullOrEmpty(name)) continue;
                    var lvl = a.Level.Count > 0 ? a.Level[0].RowId : 0;
                    Get(a.Territory.RowId).Crystals.Add(new Crystal(a.RowId, name, a.IsAetheryte, lvl));
                }

                var quests = Plugin.Data.GetExcelSheet<Quest>();
                foreach (var set in Plugin.Data.GetExcelSheet<AetherCurrentCompFlgSet>())
                {
                    if (set.Territory.RowId == 0) continue;
                    var z = Get(set.Territory.RowId);
                    z = z with { CompSet = set.RowId };
                    res[set.Territory.RowId] = z;
                    var i = 0;
                    foreach (var c in set.AetherCurrents)
                    {
                        if (c.RowId == 0) continue;
                        i++;
                        var q = c.ValueNullable?.Quest.RowId ?? 0;
                        if (q != 0)
                            z.Currents.Add(new Current(c.RowId, quests.GetRowOrDefault(q)?.Name.ExtractText() ?? $"Квест #{q}", q, quests.GetRowOrDefault(q)?.IssuerLocation.RowId ?? 0));
                        else
                        {
                            var lvl = eobjByData.TryGetValue(c.RowId, out var eo) && levelByObject.TryGetValue(eo, out var lv) ? lv : 0;
                            z.Currents.Add(new Current(c.RowId, $"Течение в поле #{i}", 0, lvl));
                        }
                    }
                }
                zones = res.Values.Where(z => z.Crystals.Count > 0 || z.Currents.Count > 0)
                    .OrderBy(z => z.ExOrder).ThenBy(z => z.Name).ToList();
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e, "Кристаллы и течения: не удалось собрать данные");
                zones = new();
            }
            finally { building = false; }
        });
    }

    private static unsafe bool CrystalOpen(uint id)
    {
        var ui = UIState.Instance();
        return ui != null && ui->IsAetheryteUnlocked(id);
    }

    private static bool CurrentOpen(uint id) =>
        Plugin.Data.GetExcelSheet<AetherCurrent>().GetRowOrDefault(id) is { } row && Plugin.Unlocks.IsAetherCurrentUnlocked(row);

    private static bool Flying(uint set) =>
        set != 0 && Plugin.Data.GetExcelSheet<AetherCurrentCompFlgSet>().GetRowOrDefault(set) is { } row && Plugin.Unlocks.IsAetherCurrentCompFlgSetUnlocked(row);

    private static (int Done, int Total) Count(Zone z) =>
        (z.Crystals.Count(c => CrystalOpen(c.Id)) + z.Currents.Count(c => CurrentOpen(c.Id)), z.Crystals.Count + z.Currents.Count);

    public void Draw()
    {
        Theme.Header("Кристаллы и течения", "Какие кристаллы телепорта, городские кристаллы и течения эфира ещё не открыты — по зонам, с флагами");
        if (!PlayerInfo.Ready) { Theme.NeedLogin("Зайди в игру персонажем: что открыто, берётся из игры."); return; }
        if (zones is null) { Build(); Theme.Skeleton("aeload", 6, "Собираю зоны, кристаллы и течения…"); return; }

        var totals = zones.Select(Count).ToList();
        var flyDone = zones.Count(z => z.CompSet != 0 && Flying(z.CompSet));
        var flyTotal = zones.Count(z => z.CompSet != 0);
        Theme.Panel("aesum", () =>
        {
            var cAll = zones.Sum(z => z.Crystals.Count(c => c.Main));
            var cOpen = zones.Sum(z => z.Crystals.Count(c => c.Main && CrystalOpen(c.Id)));
            var sAll = zones.Sum(z => z.Crystals.Count(c => !c.Main));
            var sOpen = zones.Sum(z => z.Crystals.Count(c => !c.Main && CrystalOpen(c.Id)));
            var aAll = zones.Sum(z => z.Currents.Count);
            var aOpen = zones.Sum(z => z.Currents.Count(c => CurrentOpen(c.Id)));
            Theme.Progress("Кристаллы телепорта", cOpen, cAll, Theme.Gold);
            Theme.Progress("Городские кристаллы", sOpen, sAll, Theme.Blue);
            Theme.Progress("Течения эфира", aOpen, aAll, Theme.Green);
            Theme.Progress("Полёт открыт в зонах", flyDone, flyTotal, Theme.Yellow);
        });

        ImGui.Checkbox("Только зоны, где что-то не открыто", ref onlyMissing);
        ImGui.SameLine();
        Theme.SearchBox("##aesearch", "Зона…", ref search, 260 * Theme.S, 64);

        var list = zones.Select((z, i) => (z, c: totals[i]))
            .Where(x => (!onlyMissing || x.c.Done < x.c.Total) && (search.Length == 0 || x.z.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var idx = list.FindIndex(x => x.z.Territory == selected);
        Theme.ListKeys(ref idx, list.Count);
        if (idx >= 0) selected = list[idx].z.Territory;
        var sel = list.FirstOrDefault(x => x.z.Territory == selected).z;

        Theme.MasterDetail("ae", sel is not null, () =>
        {
            var lastEx = "";
            foreach (var (z, c) in list)
            {
                if (z.Expansion != lastEx) { Theme.SubHeader(z.Expansion.Length > 0 ? z.Expansion : "Прочее"); lastEx = z.Expansion; }
                var fly = z.CompSet != 0 && Flying(z.CompSet);
                if (Theme.Card($"##aez{z.Territory}", 0, z.Name, $"открыто {c.Done} из {c.Total}" + (z.CompSet != 0 ? fly ? " · полёт ✓" : " · без полёта" : ""),
                        c.Done == c.Total ? "✓" : $"{c.Total - c.Done}", c.Done == c.Total ? Theme.Green : Theme.Yellow, selected == z.Territory, 44 * Theme.S))
                    selected = selected == z.Territory ? 0 : z.Territory;
            }
            if (list.Count == 0) Theme.EmptyState(FontAwesomeIcon.Check, "Всё открыто", "Во всех зонах кристаллы и течения собраны.");
        }, () => DrawZone(sel!), () => selected = 0, 0.45f);
    }

    private static void DrawZone(Zone z)
    {
        Theme.SubHeader(z.Name);
        if (z.CompSet != 0)
            ImGui.TextColored(Flying(z.CompSet) ? Theme.Green : Theme.Yellow, Flying(z.CompSet) ? "Полёт в зоне открыт ✓" : "Полёт ещё не открыт: собери все течения ниже");
        if (z.Crystals.Count > 0)
            Theme.Panel("aecr", () =>
            {
                ImGui.TextColored(Theme.GoldDim, "КРИСТАЛЛЫ");
                foreach (var c in z.Crystals.OrderByDescending(c => c.Main))
                {
                    var open = CrystalOpen(c.Id);
                    Theme.FaIcon(open ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.Circle, open ? Theme.Green : Theme.Gray);
                    ImGui.SameLine();
                    ImGui.TextColored(open ? Theme.TextDim : Theme.Text, c.Main ? c.Name : $"{c.Name} (городской)");
                    if (!open && c.LevelId != 0)
                    {
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"Флаг##aecf{c.Id}")) ItemSources.FlagLevel(c.LevelId);
                    }
                }
            });
        if (z.Currents.Count > 0)
            Theme.Panel("aecu", () =>
            {
                ImGui.TextColored(Theme.GoldDim, "ТЕЧЕНИЯ ЭФИРА");
                foreach (var c in z.Currents)
                {
                    var open = CurrentOpen(c.Id);
                    Theme.FaIcon(open ? FontAwesomeIcon.CheckCircle : c.QuestId != 0 ? FontAwesomeIcon.Scroll : FontAwesomeIcon.Wind, open ? Theme.Green : Theme.Gray);
                    ImGui.SameLine();
                    ImGui.TextColored(open ? Theme.TextDim : Theme.Text, c.QuestId != 0 ? $"Квест: {c.Name}" : c.Name);
                    if (!open && c.LevelId != 0)
                    {
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"Флаг##aeuf{c.Id}")) ItemSources.FlagLevel(c.LevelId);
                        if (c.QuestId != 0 && ImGui.IsItemHovered()) ImGui.SetTooltip("Флаг на NPC, который выдаёт квест");
                    }
                }
            });
    }
}
