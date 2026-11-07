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

/// <summary>
/// Гламур: что можно убрать в Armoire, какие наборы для Glamour Dresser почти собраны.
/// Содержимое Glamour Dresser игра отдаёт, только когда он открыт, — запоминаем.
/// </summary>
internal sealed unsafe class GlamourTab
{
    private static readonly string[] Sections = { "Armoire", "Наборы (outfits)" };
    private int section;
    private bool onlyIncomplete = true;

    private static readonly InventoryType[] Bags =
    {
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
        InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead, InventoryType.ArmoryBody,
        InventoryType.ArmoryHands, InventoryType.ArmoryLegs, InventoryType.ArmoryFeets, InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck, InventoryType.ArmoryWrist, InventoryType.ArmoryRings,
        InventoryType.SaddleBag1, InventoryType.SaddleBag2, InventoryType.PremiumSaddleBag1, InventoryType.PremiumSaddleBag2,
    };

    private static string Where(InventoryType t) => t switch
    {
        >= InventoryType.Inventory1 and <= InventoryType.Inventory4 => "сумка",
        InventoryType.SaddleBag1 or InventoryType.SaddleBag2 or InventoryType.PremiumSaddleBag1 or InventoryType.PremiumSaddleBag2 => "седельная сумка",
        _ => "броне-шкаф",
    };

    // ───────────── Снимок Glamour Dresser ─────────────

    private static DateTime nextTick;

    public static void Tick()
    {
        if (DateTime.Now < nextTick || !PlayerInfo.Ready) return;
        nextTick = DateTime.Now.AddSeconds(3);
        try
        {
            var mm = MirageManager.Instance();
            if (mm == null) return;
            var ids = new List<uint>();
            foreach (var raw in mm->PrismBoxItemIds)
            {
                if (raw == 0) continue;
                var id = raw % 1_000_000;
                if (id > 500_000) id -= 500_000;
                ids.Add(id);
            }
            if (ids.Count == 0) return;
            ids.Sort();
            if (ids.SequenceEqual(Plugin.Config.DresserItems)) return;
            Plugin.Config.DresserItems = ids;
            Plugin.Config.Save();
        }
        catch { /* окно не открыто */ }
    }

    // ───────────── Что есть у персонажа ─────────────

    private static Dictionary<uint, string> Owned()
    {
        var map = new Dictionary<uint, string>();
        var im = InventoryManager.Instance();
        if (im != null)
            foreach (var bag in Bags)
            {
                var c = im->GetInventoryContainer(bag);
                if (c == null) continue;
                for (var i = 0; i < c->Size; i++)
                {
                    var s = c->GetInventorySlot(i);
                    if (s == null || s->GetBaseItemId() == 0) continue;
                    map.TryAdd(s->GetBaseItemId(), Where(bag));
                }
            }
        foreach (var r in RetainerTracker.All)
            foreach (var it in r.Items) map.TryAdd(it.ItemId, $"ретейнер {r.Name}");
        foreach (var id in Plugin.Config.DresserItems) map.TryAdd(id, "Glamour Dresser");
        return map;
    }

    private HashSet<uint>? cabinet;
    private List<(uint Set, uint[] Pieces)>? sets;

    public void Draw()
    {
        Theme.Header("Гламур", "Что убрать в Armoire, какие наборы для Glamour Dresser почти собраны");
        if (!PlayerInfo.Ready) { Theme.Panel("glnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем.")); return; }
        Theme.Chips("glsec", Sections, ref section);
        if (Plugin.Config.DresserItems.Count == 0)
            ImGui.TextColored(Theme.Yellow, "Открой Glamour Dresser один раз — запомню, что в нём лежит.");
        else
            ImGui.TextColored(Theme.TextDim, $"В Glamour Dresser (последний снимок): {Plugin.Config.DresserItems.Count} вещей.");

        var owned = Owned();
        if (section == 0) DrawArmoire(owned); else DrawSets(owned);
    }

    private void DrawArmoire(Dictionary<uint, string> owned)
    {
        cabinet ??= Plugin.Data.GetExcelSheet<Cabinet>().Where(c => c.Item.RowId != 0).Select(c => c.Item.RowId).ToHashSet();
        var list = owned.Where(kv => cabinet.Contains(kv.Key)).OrderBy(kv => kv.Value).ThenBy(kv => WikiView.ItemInfo(kv.Key).Name).ToList();
        if (!ImGui.BeginChild("##armoire", Vector2.Zero)) { ImGui.EndChild(); return; }
        Theme.Panel("arm", () =>
        {
            Theme.SubHeader($"Можно убрать в Armoire · {list.Count}");
            ImGui.TextColored(Theme.TextDim, "Armoire (в доме или в гостинице) хранит эти вещи бесплатно — место в сумках и Glamour Dresser освободится. Наведи — вещь подсветится в сумке.");
            if (list.Count == 0) ImGui.TextColored(Theme.Green, "Таких вещей нет ✓");
            foreach (var (id, where) in list) WikiView.ItemLine(id, 0, where, "arm");
        });
        ImGui.EndChild();
    }

    private void DrawSets(Dictionary<uint, string> owned)
    {
        sets ??= Plugin.Data.GetExcelSheet<MirageStoreSetItem>()
            .Select(s => (s.RowId, new[] { s.MainHand, s.OffHand, s.Head, s.Body, s.Hands, s.Legs, s.Feet, s.Earrings, s.Necklace, s.Bracelets, s.Ring }
                .Select(r => r.RowId).Where(x => x != 0).ToArray()))
            .Where(s => s.Item2.Length > 1 && WikiView.ItemInfo(s.RowId).Name.Length > 0)
            .ToList();

        ImGui.Checkbox("Только неполные наборы", ref onlyIncomplete);
        var rows = sets
            .Select(s => (s.Set, s.Pieces, have: s.Pieces.Count(owned.ContainsKey), setOwned: owned.ContainsKey(s.Set)))
            .Where(x => x.have > 0 && !x.setOwned && (!onlyIncomplete || x.have < x.Pieces.Length))
            .OrderByDescending(x => (double)x.have / x.Pieces.Length)
            .ToList();
        var complete = sets.Count(s => !owned.ContainsKey(s.Set) && s.Pieces.All(owned.ContainsKey));

        if (!ImGui.BeginChild("##sets", Vector2.Zero)) { ImGui.EndChild(); return; }
        if (complete > 0)
            ImGui.TextColored(Theme.Green, $"Полностью собрано наборов: {complete} — их можно объединить в Glamour Dresser в один слот.");
        if (rows.Count == 0)
            ImGui.TextColored(Theme.TextDim, "Нет начатых наборов.");
        Theme.Grid("setgrid", rows.Count, 380f, i =>
        {
            var (set, pieces, have, _) = rows[i];
            Theme.Panel($"set{set}", () =>
            {
                var name = WikiView.ItemInfo(set).Name;
                WikiView.ItemLine(set, 0, null, "setn");
                Theme.Progress($"Собрано {have} из {pieces.Length}", have, pieces.Length, have == pieces.Length ? Theme.Green : Theme.Gold);
                foreach (var p in pieces)
                {
                    var has = owned.TryGetValue(p, out var where);
                    WikiView.ItemLine(p, 0, has ? $"✓ {where}" : "нет — где взять: нажми", $"sp{set}");
                }
            });
        });
        ImGui.EndChild();
    }
}
