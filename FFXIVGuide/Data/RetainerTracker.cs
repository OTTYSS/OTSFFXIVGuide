using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace FFXIVGuide.Data;

[Serializable]
public sealed class StoredItem
{
    public uint ItemId { get; set; }
    public int Quantity { get; set; }
    public bool Hq { get; set; }
}

[Serializable]
public sealed class RetainerSnapshot
{
    public string Name { get; set; } = "";
    public DateTime Updated { get; set; }
    public List<StoredItem> Items { get; set; } = new();
    /// <summary>Надетое (по слотам игры: 0 оружие, 1 вторая рука, 2 голова … 12 кольцо), пусто — не снято.</summary>
    public List<StoredItem> Equipped { get; set; } = new();
    public byte ClassJob { get; set; }
    public byte Level { get; set; }
}

/// <summary>
/// Запоминает, что лежит у ретейнеров. Игра отдаёт их сумки, только пока открыто окно ретейнера,
/// поэтому снимок обновляется в этот момент и хранится в настройках. Только чтение.
/// </summary>
public static unsafe class RetainerTracker
{
    private static readonly InventoryType[] Pages =
    {
        InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3, InventoryType.RetainerPage4,
        InventoryType.RetainerPage5, InventoryType.RetainerPage6, InventoryType.RetainerPage7,
    };

    private static DateTime nextCheck = DateTime.MinValue;

    public static IEnumerable<RetainerSnapshot> All => Plugin.Config.Retainers.Values;

    /// <summary>Сколько штук предмета у всех ретейнеров и у кого именно.</summary>
    public static (int Total, string Where) CountOf(uint itemId)
    {
        var parts = new List<string>();
        var total = 0;
        foreach (var r in All)
        {
            var n = r.Items.Where(i => i.ItemId == itemId).Sum(i => i.Quantity);
            if (n == 0) continue;
            total += n;
            parts.Add($"{r.Name} ×{n}");
        }
        return (total, string.Join(", ", parts));
    }

    public static void Tick()
    {
        if (DateTime.Now < nextCheck || !PlayerInfo.Ready) return;
        nextCheck = DateTime.Now.AddSeconds(2);

        var rm = RetainerManager.Instance();
        if (rm == null) return;
        var active = rm->GetActiveRetainer();
        if (active == null || active->RetainerId == 0) return;

        var im = InventoryManager.Instance();
        if (im == null) return;

        var items = new List<StoredItem>();
        var loaded = false;
        foreach (var page in Pages)
        {
            var c = im->GetInventoryContainer(page);
            if (c == null || c->Size == 0) continue;
            loaded = true;
            for (var i = 0; i < c->Size; i++)
            {
                var s = c->GetInventorySlot(i);
                if (s == null || s->GetBaseItemId() == 0) continue;
                items.Add(new StoredItem { ItemId = s->GetBaseItemId(), Quantity = (int)s->GetQuantity(), Hq = s->IsHighQuality() });
            }
        }
        if (!loaded) return;

        var equipped = new List<StoredItem>();
        var eq = im->GetInventoryContainer(InventoryType.RetainerEquippedItems);
        if (eq != null)
            for (var i = 0; i < eq->Size; i++)
            {
                var s = eq->GetInventorySlot(i);
                equipped.Add(s == null ? new StoredItem() : new StoredItem { ItemId = s->GetBaseItemId(), Quantity = 1, Hq = s->IsHighQuality() });
            }

        var key = active->RetainerId.ToString();
        var name = active->NameString;
        var cfg = Plugin.Config;
        static bool Same(List<StoredItem> a, List<StoredItem> b) =>
            a.Count == b.Count && a.Zip(b).All(p => p.First.ItemId == p.Second.ItemId && p.First.Quantity == p.Second.Quantity && p.First.Hq == p.Second.Hq);
        if (cfg.Retainers.TryGetValue(key, out var old) && Same(old.Items, items) && Same(old.Equipped, equipped)
            && old.ClassJob == active->ClassJob && old.Level == active->Level)
            return;

        cfg.Retainers[key] = new RetainerSnapshot
        {
            Name = name, Updated = DateTime.Now, Items = items, Equipped = equipped, ClassJob = active->ClassJob, Level = active->Level,
        };
        cfg.Save();
    }
}
