using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Карты сокровищ: какие есть у тебя и все возможные точки раскопок по зонам с флагом.</summary>
internal sealed unsafe class TreasureTab
{
    private sealed record Rank(uint Id, string Name, uint ItemId, uint KeyItemId, uint Icon);
    private sealed record Spot(uint LevelId, uint Territory, string Zone);

    private List<Rank>? ranks;
    private Rank? selected;
    private bool onlyHere = true;

    public void Draw()
    {
        ranks ??= Plugin.Data.GetExcelSheet<TreasureHuntRank>()
            .Where(r => r.ItemName.RowId != 0 && r.ItemName.ValueNullable is { } it && !it.Name.IsEmpty)
            .Select(r => new Rank(r.RowId, r.ItemName.Value.Name.ExtractText(), r.ItemName.RowId, r.KeyItemName.RowId, r.ItemName.Value.Icon))
            .ToList();

        Theme.Header("Карты сокровищ", "Какие карты у тебя есть и где копать: все возможные точки по зонам с флагом");

        var avail = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##trl", new Vector2(avail.X * 0.4f, 0)))
        {
            foreach (var r in ranks.OrderByDescending(r => Owned(r)).ThenByDescending(r => r.Id))
            {
                var owned = Owned(r);
                if (Theme.Card($"##tr{r.Id}", r.Icon, r.Name, owned > 0 ? "есть у тебя" : "", owned > 0 ? $"×{owned}" : null, Theme.Green, selected == r, 40 * Theme.S))
                    selected = r;
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("##trd", Vector2.Zero))
        {
            if (selected is null)
                Theme.Panel("trh", () => ImGui.TextColored(Theme.TextDim,
                    "Выбери карту слева. Расшифруй её в игре (Decipher) — на карте будет картинка места; найди совпадающую точку здесь и поставь флаг."));
            else
                DrawRank(selected);
        }
        ImGui.EndChild();
    }

    private static int Owned(Rank r)
    {
        if (!PlayerInfo.Ready) return 0;
        var n = PlayerInfo.ItemCount(r.ItemId);
        var im = InventoryManager.Instance();
        if (im != null && r.KeyItemId != 0)
            n += im->GetItemCountInContainer(r.KeyItemId, InventoryType.KeyItems, false, 0);
        return n;
    }

    private void DrawRank(Rank r)
    {
        var spots = new List<Spot>();
        var sheet = Plugin.Data.GetSubrowExcelSheet<TreasureSpot>();
        if (sheet.TryGetRow(r.Id, out var rows))
        {
            foreach (var s in rows)
            {
                var lv = s.Location.ValueNullable;
                if (lv is null || lv.Value.Territory.RowId == 0) continue;
                var zone = lv.Value.Territory.ValueNullable?.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
                spots.Add(new Spot(s.Location.RowId, lv.Value.Territory.RowId, zone));
            }
        }

        var here = Plugin.ClientState.TerritoryType;
        Theme.Panel($"trr{r.Id}", () =>
        {
            Theme.LargeText(Theme.Gold, r.Name);
            ImGui.TextColored(Theme.TextDim, $"Точек раскопок: {spots.Count} в {spots.Select(s => s.Zone).Distinct().Count()} зонах");
            ImGui.Checkbox("Только в моей зоне", ref onlyHere);
        });

        var shown = onlyHere && spots.Any(s => s.Territory == here) ? spots.Where(s => s.Territory == here) : spots;
        foreach (var zone in shown.GroupBy(s => s.Zone))
        {
            Theme.Panel($"trz{r.Id}{zone.Key}", () =>
            {
                Theme.SubHeader(zone.Key + (zone.First().Territory == here ? " (ты здесь)" : ""));
                var i = 0;
                foreach (var s in zone)
                {
                    i++;
                    if (i > 1) ImGui.SameLine();
                    if (ImGui.GetContentRegionAvail().X < 90 * Theme.S) ImGui.NewLine();
                    if (Theme.IconButton($"tf{s.LevelId}", FontAwesomeIcon.Flag, $"Точка {i}"))
                        Flag(s.LevelId);
                }
            });
        }
        if (onlyHere && !spots.Any(s => s.Territory == here))
            ImGui.TextColored(Theme.Gray, "В текущей зоне таких точек нет — показаны все зоны.");
    }

    private static void Flag(uint levelId)
    {
        var lv = Plugin.Data.GetExcelSheet<Level>().GetRowOrDefault(levelId);
        if (lv is null) return;
        Plugin.GameGui.OpenMapWithMapLink(new MapLinkPayload(lv.Value.Territory.RowId, lv.Value.Map.RowId, (int)(lv.Value.X * 1000f), (int)(lv.Value.Z * 1000f)));
    }
}
