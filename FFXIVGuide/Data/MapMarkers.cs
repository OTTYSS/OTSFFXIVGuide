using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

/// <summary>
/// Свои метки на карте зоны (окно карты игры): вистас, течения эфира, точки сбора, места рыбалки, FATE.
/// Игра сбрасывает метки при смене карты — добавляем их заново. Только показывает.
/// </summary>
public static unsafe class MapMarkers
{
    public static readonly (string Key, string Name, uint Icon)[] Kinds =
    {
        ("vista", "Вистас (не пройденные)", 60581),
        ("current", "Течения эфира (не собранные)", 60760),
        ("gather", "Точки сбора", 60346),
        ("fish", "Места рыбалки", 60333),
        ("fate", "FATE", 60501),
    };

    private static uint lastMap;
    private static int countAfterAdd = -1;
    private static DateTime nextTick;
    private static readonly Dictionary<uint, List<(Vector3 Pos, uint Icon)>> cache = new();
    private static string cacheKey = "";

    public static void Tick()
    {
        if (DateTime.Now < nextTick) return;
        nextTick = DateTime.Now.AddMilliseconds(500);
        var kinds = Plugin.Config.MapMarkerKinds;
        if (kinds.Count == 0 || !PlayerInfo.Ready) return;
        var map = Plugin.GameGui.GetAddonByName("AreaMap", 1);
        if (map.IsNull) { lastMap = 0; return; }
        var agent = AgentMap.Instance();
        if (agent == null) return;
        var mapId = agent->SelectedMapId;
        if (mapId == 0) return;
        // Игра пересоздала метки (сменилась карта или обновилась) — добавляем наши снова
        if (mapId == lastMap && agent->MapMarkerCount >= countAfterAdd) return;

        var key = string.Join(",", kinds.OrderBy(k => k));
        if (key != cacheKey) { cache.Clear(); cacheKey = key; }
        if (!cache.TryGetValue(mapId, out var markers)) cache[mapId] = markers = Build(mapId, kinds);

        foreach (var (pos, icon) in markers.Take(80))
            agent->AddMapMarker(pos, icon, 0);
        lastMap = mapId;
        countAfterAdd = agent->MapMarkerCount;
    }

    /// <summary>Сбросить кеш (после изменения настроек или сбора течения).</summary>
    public static void Invalidate()
    {
        cache.Clear();
        lastMap = 0;
    }

    private static Vector3 FromMapCoords(Map map, float x, float y)
    {
        // Обратная формула к Dalamud MapUtil: map = 0.02·offset + 2048/scale + 0.02·world + 1
        float scale = map.SizeFactor;
        float W(float v, short offset) => (v - 1f - 2048f / scale - 0.02f * offset) / 0.02f;
        return new Vector3(W(x, map.OffsetX), 0, W(y, map.OffsetY));
    }

    private static List<(Vector3, uint)> Build(uint mapId, ICollection<string> kinds)
    {
        var list = new List<(Vector3, uint)>();
        var mapRow = Plugin.Data.GetExcelSheet<Map>().GetRowOrDefault(mapId);
        if (mapRow is null) return list;
        var map = mapRow.Value;
        var terr = map.TerritoryType.RowId;
        var icon = Kinds.ToDictionary(k => k.Key, k => k.Icon);

        if (kinds.Contains("vista"))
            foreach (var a in Plugin.Data.GetExcelSheet<Adventure>())
                if (a.Level.ValueNullable is { } lv && lv.Territory.RowId == terr && lv.Map.RowId == mapId && !Plugin.Unlocks.IsAdventureComplete(a))
                    list.Add((new Vector3(lv.X, lv.Y, lv.Z), icon["vista"]));

        if (kinds.Contains("current"))
        {
            var set = Plugin.Data.GetExcelSheet<AetherCurrentCompFlgSet>().FirstOrDefault(s => s.Territory.RowId == terr);
            if (set.RowId != 0)
            {
                var missing = set.AetherCurrents.Where(c => c.RowId != 0 && c.ValueNullable is { } v && v.Quest.RowId == 0 && !Plugin.Unlocks.IsAetherCurrentUnlocked(v))
                    .Select(c => c.RowId).ToHashSet();
                if (missing.Count > 0)
                {
                    var eobjs = Plugin.Data.GetExcelSheet<EObj>().Where(e => missing.Contains(e.Data.RowId)).Select(e => e.RowId).ToHashSet();
                    foreach (var lv in Plugin.Data.GetExcelSheet<Level>())
                        if (lv.Territory.RowId == terr && eobjs.Contains(lv.Object.RowId))
                            list.Add((new Vector3(lv.X, lv.Y, lv.Z), icon["current"]));
                }
            }
        }

        if (kinds.Contains("gather"))
            foreach (var s in GatherIndex.All.Values.SelectMany(v => v).Where(s => s.Territory == terr && s.Map == mapId)
                         .GroupBy(s => ((int)s.X / 10, (int)s.Y / 10)).Select(g => g.First()))
                list.Add((new Vector3(s.X, 0, s.Y), icon["gather"]));

        if (kinds.Contains("fish") && OfflineDb.Instance.Index is { } ix)
            foreach (var spot in ix.FishingSpots.Values.Where(f => f.Map == mapId && f.X > 0))
                list.Add((FromMapCoords(map, spot.X, spot.Y), icon["fish"]));

        if (kinds.Contains("fate") && WikiService.Instance.FateIndex() is { } fates)
        {
            var place = map.PlaceName.RowId;
            foreach (var f in fates.Where(f => f.Zone == place && f.X > 0))
                list.Add((FromMapCoords(map, f.X, f.Y), icon["fate"]));
        }
        return list;
    }
}
