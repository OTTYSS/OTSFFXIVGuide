using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

/// <summary>Предмет в сумке/броне-шкафу.</summary>
public sealed record InvItem(uint ItemId, string Name, uint Icon, int Quantity, bool Hq, InventoryType Container, int Slot, Item Row)
{
    public bool IsGear => Row.EquipSlotCategory.RowId != 0 && Row.EquipSlotCategory.RowId != 17; // 17 — кристалл души
    public bool Marketable => Row.ItemSearchCategory.RowId != 0 && !Row.IsUntradable;
    public string Location => InventoryAnalyzer.LocationName(Container);
}

public enum ActionKind { Market, GrandCompany, Desynth, Vendor, Discard }

/// <summary>Совет, что сделать с предметом. Выполняет игрок сам.</summary>
public sealed record ItemAdvice(ActionKind Kind, string Text, long Value);

public sealed record OutdatedGear(InvItem Item, string BetterName, uint BetterIlvl, List<ItemAdvice> Advice);
public sealed record StackTip(InvItem Sample, int Slots, int TotalQuantity, int SlotsFreed);


/// <summary>
/// Разбор инвентаря: только чтение. Ничего не выбрасывает, не продаёт и не перемещает.
/// </summary>
public static unsafe class InventoryAnalyzer
{
    public static readonly InventoryType[] Bags =
    {
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
    };

    public static readonly InventoryType[] Armory =
    {
        InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead, InventoryType.ArmoryBody,
        InventoryType.ArmoryHands, InventoryType.ArmoryLegs, InventoryType.ArmoryFeets, InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck, InventoryType.ArmoryWrist, InventoryType.ArmoryRings,
    };

    /// <summary>Седельная сумка (игра отдаёт её, когда она была открыта в этой сессии).</summary>
    public static readonly InventoryType[] Saddle =
    {
        InventoryType.SaddleBag1, InventoryType.SaddleBag2, InventoryType.PremiumSaddleBag1, InventoryType.PremiumSaddleBag2,
    };

    public static string LocationName(InventoryType t) => t switch
    {
        InventoryType.SaddleBag1 or InventoryType.SaddleBag2 => "Седельная сумка",
        InventoryType.PremiumSaddleBag1 or InventoryType.PremiumSaddleBag2 => "Седельная сумка (премиум)",
        InventoryType.Inventory1 => "Сумка 1",
        InventoryType.Inventory2 => "Сумка 2",
        InventoryType.Inventory3 => "Сумка 3",
        InventoryType.Inventory4 => "Сумка 4",
        InventoryType.ArmoryMainHand => "Броне-шкаф: оружие",
        InventoryType.ArmoryOffHand => "Броне-шкаф: вторая рука",
        InventoryType.ArmoryHead => "Броне-шкаф: голова",
        InventoryType.ArmoryBody => "Броне-шкаф: тело",
        InventoryType.ArmoryHands => "Броне-шкаф: руки",
        InventoryType.ArmoryLegs => "Броне-шкаф: ноги",
        InventoryType.ArmoryFeets => "Броне-шкаф: обувь",
        InventoryType.ArmoryEar => "Броне-шкаф: серьги",
        InventoryType.ArmoryNeck => "Броне-шкаф: ожерелье",
        InventoryType.ArmoryWrist => "Броне-шкаф: браслеты",
        InventoryType.ArmoryRings => "Броне-шкаф: кольца",
        InventoryType.EquippedItems => "Надето",
        _ => t.ToString(),
    };

    public sealed record Result(
        List<InvItem> Items,
        int BagUsed, int BagTotal,
        int ArmoryUsed, int ArmoryTotal,
        List<OutdatedGear> Outdated,
        List<StackTip> Stacks,
        HashSet<uint> GearsetItems);

    public static Result? Scan()
    {
        if (!PlayerInfo.Ready) return null;
        var im = InventoryManager.Instance();
        if (im == null) return null;

        var sheet = Plugin.Data.GetExcelSheet<Item>();
        var items = new List<InvItem>();
        int bagUsed = 0, bagTotal = 0, armUsed = 0, armTotal = 0;

        void Read(InventoryType type, bool isBag, bool count = true)
        {
            var c = im->GetInventoryContainer(type);
            if (c == null) return;
            var size = (int)c->Size;
            if (count) { if (isBag) bagTotal += size; else armTotal += size; }
            for (var i = 0; i < size; i++)
            {
                var slot = c->GetInventorySlot(i);
                if (slot == null) continue;
                var id = slot->GetBaseItemId();
                if (id == 0) continue;
                if (count) { if (isBag) bagUsed++; else armUsed++; }
                var row = sheet.GetRowOrDefault(id);
                if (row is null) continue;
                items.Add(new InvItem(id, row.Value.Name.ExtractText(), row.Value.Icon, (int)slot->GetQuantity(),
                    slot->IsHighQuality(), type, i, row.Value));
            }
        }

        foreach (var b in Bags) Read(b, true);
        foreach (var a in Armory) Read(a, false);
        foreach (var s in Saddle) Read(s, false, false);

        // Надетое — только для сравнения «есть лучше»
        var equipped = new List<InvItem>();
        var eq = im->GetInventoryContainer(InventoryType.EquippedItems);
        if (eq != null)
        {
            for (var i = 0; i < eq->Size; i++)
            {
                var s = eq->GetInventorySlot(i);
                if (s == null || s->GetBaseItemId() == 0) continue;
                var row = sheet.GetRowOrDefault(s->GetBaseItemId());
                if (row is null) continue;
                equipped.Add(new InvItem(s->GetBaseItemId(), row.Value.Name.ExtractText(), row.Value.Icon, 1, s->IsHighQuality(), InventoryType.EquippedItems, i, row.Value));
            }
        }

        var gearsetItems = GearsetItemIds();

        return new Result(items, bagUsed, bagTotal, armUsed, armTotal,
            FindOutdated(items, equipped, gearsetItems), FindStacks(items), gearsetItems);
    }

    private static HashSet<uint> GearsetItemIds()
    {
        var set = new HashSet<uint>();
        var gm = RaptureGearsetModule.Instance();
        if (gm == null) return set;
        for (var i = 0; i < 100; i++)
        {
            if (!gm->IsValidGearset(i)) continue;
            var entry = gm->GetGearset(i);
            if (entry == null) continue;
            foreach (var it in entry->Items)
                if (it.ItemId != 0)
                    set.Add(it.ItemId % 1_000_000); // HQ-предметы хранятся с +1 000 000
        }
        return set;
    }

    /// <summary>
    /// Снаряжение не из гирсетов, для которого в том же слоте и для тех же профессий
    /// уже есть вещь с большим iLvl (в сумках, броне-шкафу или надетая).
    /// </summary>
    private static List<OutdatedGear> FindOutdated(List<InvItem> items, List<InvItem> equipped, HashSet<uint> gearset)
    {
        var all = items.Where(i => i.IsGear).Concat(equipped).ToList();
        var result = new List<OutdatedGear>();

        foreach (var it in items.Where(i => i.IsGear))
        {
            if (gearset.Contains(it.ItemId)) continue;
            var ilvl = it.Row.LevelItem.RowId;
            var better = all
                .Where(o => o.ItemId != it.ItemId
                         && o.Row.EquipSlotCategory.RowId == it.Row.EquipSlotCategory.RowId
                         && o.Row.ClassJobCategory.RowId == it.Row.ClassJobCategory.RowId
                         && o.Row.LevelItem.RowId > ilvl)
                .OrderByDescending(o => o.Row.LevelItem.RowId)
                .FirstOrDefault();
            if (better is null) continue;

            result.Add(new OutdatedGear(it, better.Name, better.Row.LevelItem.RowId, new List<ItemAdvice>()));
        }

        return result;
    }

    /// <summary>Одинаковые предметы, разложенные по нескольким неполным стакам.</summary>
    private static List<StackTip> FindStacks(List<InvItem> items)
    {
        return items
            .Where(i => Bags.Contains(i.Container) && i.Row.StackSize > 1)
            .GroupBy(i => (i.ItemId, i.Hq))
            .Select(g =>
            {
                var total = g.Sum(x => x.Quantity);
                var need = (int)Math.Ceiling(total / (double)g.First().Row.StackSize);
                return new StackTip(g.First(), g.Count(), total, g.Count() - need);
            })
            .Where(t => t.SlotsFreed > 0)
            .OrderByDescending(t => t.SlotsFreed)
            .ToList();
    }

    /// <summary>Советы для предмета, от самого выгодного. price — цена на рынке твоего мира (если есть).</summary>
    public static List<ItemAdvice> Advise(InvItem it, MarketPrice? price)
    {
        var list = new List<ItemAdvice>();
        var row = it.Row;
        var npc = (long)row.PriceLow * it.Quantity;

        if (it.Marketable && price is not null && price.Price > Math.Max(row.PriceLow * 2, 500))
            list.Add(new ItemAdvice(ActionKind.Market, $"Рынок ~{price.Price * it.Quantity:N0} гил", price.Price * it.Quantity));

        if (it.IsGear && row.Rarity is 2 or 3 && !row.IsIndisposable && row.PriceLow > 0)
        {
            var seals = Plugin.Data.GetExcelSheet<GCSupplyDutyReward>().GetRowOrDefault(row.LevelItem.RowId)?.SealsExpertDelivery ?? 0;
            if (seals > 0)
                list.Add(new ItemAdvice(ActionKind.GrandCompany, $"GC Expert Delivery: {seals:N0} печатей", seals));
        }

        if (it.IsGear && row.Desynth > 0)
            list.Add(new ItemAdvice(ActionKind.Desynth, "Можно разобрать (Desynthesis)", 0));

        if (npc > 0)
            list.Add(new ItemAdvice(ActionKind.Vendor, $"Продать NPC: {npc:N0} гил", npc));
        else if (!row.IsIndisposable)
            list.Add(new ItemAdvice(ActionKind.Discard, "Ничего не стоит — можно выбросить", 0));

        return list;
    }
}
