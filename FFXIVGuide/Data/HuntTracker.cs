using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

public sealed record HuntSighting(string Name, string Rank, string Zone, uint Territory, uint Map, Vector2 MapPos, DateTime Seen, ulong ObjectId)
{
    public string Coords => $"X {MapPos.X:0.0}, Y {MapPos.Y:0.0}";
}

/// <summary>
/// Замечает hunt-марок (S/A/B ранга) среди видимых объектов и сообщает в чат со ссылкой на карту.
/// Только чтение списка объектов — ничего не делает за игрока.
/// </summary>
public sealed class HuntTracker
{
    private Dictionary<uint, (string Rank, string Name)>? marks;
    private readonly Dictionary<ulong, DateTime> alerted = new();
    private DateTime nextScan = DateTime.MinValue;

    public List<HuntSighting> Recent { get; } = new();

    public void OnUpdate(IFramework framework)
    {
        Checklist.Tick();
        RetainerTracker.Tick();
        DutyRecorder.Tick();
        ProgressDiary.Tick();
        Translator.Instance.Tick();
        NameTranslations.AutoResume();
        FFXIVGuide.Windows.WeatherTab.Tick();
        FFXIVGuide.Windows.TimersTab.Tick();
        FFXIVGuide.Windows.GlamourTab.Tick();
        FFXIVGuide.Windows.WeeklyTab.Tick();

        if (!Plugin.Config.HuntAlerts || DateTime.Now < nextScan || !PlayerInfo.Ready)
            return;
        nextScan = DateTime.Now.AddSeconds(1);

        marks ??= Build();
        foreach (var obj in Plugin.Objects)
        {
            if (obj.ObjectKind != ObjectKind.BattleNpc) continue;
            if (!marks.TryGetValue(obj.BaseId, out var mark)) continue;
            if (!RankEnabled(mark.Rank)) continue;
            if (alerted.TryGetValue(obj.GameObjectId, out var at) && DateTime.Now - at < TimeSpan.FromMinutes(10)) continue;
            alerted[obj.GameObjectId] = DateTime.Now;
            Alert(obj.GameObjectId, mark.Rank, obj.Name.TextValue, obj.Position);
        }
    }

    private static bool RankEnabled(string rank) => rank switch
    {
        "S" or "SS" => Plugin.Config.HuntRankS,
        "A" => Plugin.Config.HuntRankA,
        "B" => Plugin.Config.HuntRankB,
        _ => false,
    };

    private void Alert(ulong id, string rank, string name, Vector3 pos)
    {
        var territory = Plugin.ClientState.TerritoryType;
        var mapId = Plugin.ClientState.MapId;
        var map = Plugin.Data.GetExcelSheet<Map>().GetRowOrDefault(mapId);
        var zone = Plugin.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territory)?.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
        var mapPos = map is null ? Vector2.Zero : MapUtil.WorldToMap(new Vector2(pos.X, pos.Z), map.Value);

        var s = new HuntSighting(name, rank, zone, territory, mapId, mapPos, DateTime.Now, id);
        Recent.Insert(0, s);
        if (Recent.Count > 30) Recent.RemoveAt(Recent.Count - 1);

        var msg = new SeStringBuilder()
            .AddText($"[{rank}-ранг] {name} рядом! ")
            .AddMapLink(territory, mapId, mapPos.X, mapPos.Y)
            .Build();
        Plugin.Chat.Print(msg, "FFXIV Guide");

        if (Plugin.Config.HuntSound)
            UIGlobals.PlayChatSoundEffect(rank is "S" or "SS" ? 6u : 2u);
    }

    public static void Flag(HuntSighting s)
    {
        var link = new MapLinkPayload(s.Territory, s.Map, s.MapPos.X, s.MapPos.Y, 0.05f);
        Plugin.GameGui.OpenMapWithMapLink(link);
    }

    private static Dictionary<uint, (string, string)> Build()
    {
        var dict = new Dictionary<uint, (string, string)>();
        foreach (var nm in Plugin.Data.GetExcelSheet<NotoriousMonster>())
        {
            var baseId = nm.BNpcBase.RowId;
            if (baseId == 0) continue;
            var rank = nm.Rank switch { 1 => "B", 2 => "A", 3 => "S", 4 => "SS", _ => "" };
            if (rank.Length == 0) continue;
            dict[baseId] = (rank, nm.BNpcName.ValueNullable?.Singular.ExtractText() ?? "");
        }
        return dict;
    }
}
