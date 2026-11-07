using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

/// <summary>Точка сбора предмета.</summary>
public sealed record GatherSpot(string Type, int Level, uint Territory, uint Map, string Zone, string Place, float X, float Y, bool Timed);

/// <summary>
/// Где собирать предметы: тип сбора (Mining/Logging/…), уровень, зона и координаты точки.
/// Строится из файлов игры при первом обращении.
/// </summary>
public static class GatherIndex
{
    private static Dictionary<uint, List<GatherSpot>>? byItem;

    public static IReadOnlyDictionary<uint, List<GatherSpot>> All => byItem ??= Build();

    public static IReadOnlyList<GatherSpot> For(uint itemId) =>
        All.TryGetValue(itemId, out var l) ? l : (IReadOnlyList<GatherSpot>)System.Array.Empty<GatherSpot>();

    public static bool Flag(GatherSpot s)
    {
        if (s.Territory == 0) return false;
        var link = new Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload(s.Territory, s.Map, (int)(s.X * 1000f), (int)(s.Y * 1000f));
        return Plugin.GameGui.OpenMapWithMapLink(link);
    }

    private static Dictionary<uint, List<GatherSpot>> Build()
    {
        var result = new Dictionary<uint, List<GatherSpot>>();
        try
        {
            var gItems = Plugin.Data.GetExcelSheet<GatheringItem>();
            var bases = Plugin.Data.GetExcelSheet<GatheringPointBase>();
            var exported = Plugin.Data.GetExcelSheet<ExportedGatheringPoint>();
            var seen = new HashSet<(uint, uint)>();

            foreach (var gp in Plugin.Data.GetExcelSheet<GatheringPoint>())
            {
                var baseRow = bases.GetRowOrDefault(gp.GatheringPointBase.RowId);
                var terr = gp.TerritoryType.ValueNullable;
                if (baseRow is null || terr is null || gp.TerritoryType.RowId == 0) continue;

                var type = baseRow.Value.GatheringType.ValueNullable?.Name.ExtractText() ?? "";
                var zone = terr.Value.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
                if (zone.Length == 0) continue;
                var place = gp.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
                var ex = exported.GetRowOrDefault(baseRow.Value.RowId);
                var timed = gp.Type is 2 or 3; // unspoiled/legendary и т.п. — по таймеру

                foreach (var gi in baseRow.Value.Item)
                {
                    var giRow = gItems.GetRowOrDefault(gi.RowId);
                    if (giRow is null) continue;
                    var itemId = giRow.Value.Item.RowId;
                    if (itemId == 0 || !seen.Add((itemId, gp.TerritoryType.RowId))) continue;

                    if (!result.TryGetValue(itemId, out var list))
                        result[itemId] = list = new List<GatherSpot>();
                    list.Add(new GatherSpot(type, baseRow.Value.GatheringLevel, gp.TerritoryType.RowId, terr.Value.Map.RowId,
                        zone, place, ex?.X ?? 0, ex?.Y ?? 0, timed));
                }
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.Warning(e, "Не удалось построить точки сбора");
        }

        foreach (var l in result.Values)
            l.Sort((a, b) => a.Level.CompareTo(b.Level));
        return result;
    }
}
