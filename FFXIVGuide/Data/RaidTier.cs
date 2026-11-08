using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.DutyState;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

/// <summary>Цена обмена у NPC: что отдать и сколько.</summary>
public sealed record ExchangeCost(uint Item, uint Amount, bool Tomestone);

/// <summary>
/// Текущий рейдовый тир, собранный из файлов игры: томестоны с недельным лимитом, книги саведжа,
/// обмены «вещь ← валюта», этажи саведжа. Плюс учёт закрытых на этой неделе этажей.
/// </summary>
public static class RaidTier
{
    private static bool built;
    private static readonly Dictionary<uint, List<ExchangeCost>> exchanges = new();

    /// <summary>Текущие томестоны с недельным лимитом (предмет).</summary>
    public static uint Tomestone { get; private set; }
    /// <summary>Книги саведжа текущего тира, по порядку этажей.</summary>
    public static List<uint> Books { get; private set; } = new();
    /// <summary>Этажи саведжа текущего тира (ContentFinderCondition), по порядку.</summary>
    public static List<(uint Cfc, string Name)> Floors { get; private set; } = new();
    /// <summary>iLvl саведжа текущего тира.</summary>
    public static uint SavageIlvl { get; private set; }

    public const int TomestoneCap = 2000;

    public static IReadOnlyList<ExchangeCost>? Exchange(uint item)
    {
        Build();
        return exchanges.TryGetValue(item, out var l) ? l : null;
    }

    public static void Build()
    {
        if (built) return;
        built = true;
        try { BuildInner(); }
        catch (Exception e) { Plugin.Log.Warning(e, "RaidTier: не удалось разобрать обмены"); }
    }

    private static void BuildInner()
    {
        var items = Plugin.Data.GetExcelSheet<Item>();
        var tomeByIndex = new Dictionary<uint, uint>();
        foreach (var t in Plugin.Data.GetExcelSheet<TomestonesItem>())
            if (t.Tomestones.RowId != 0 && t.Item.RowId != 0)
                tomeByIndex[t.Tomestones.RowId] = t.Item.RowId;
        var tomeItems = tomeByIndex.Values.ToHashSet();

        foreach (var shop in Plugin.Data.GetExcelSheet<SpecialShop>())
            foreach (var entry in shop.Item)
            {
                var costs = new List<ExchangeCost>();
                foreach (var c in entry.ItemCosts)
                {
                    var id = c.ItemCost.RowId;
                    if (id == 0 || c.CurrencyCost == 0) continue;
                    if (id < 100 && (shop.UseCurrencyType == 4 || shop.UseCurrencyType == 16) && tomeByIndex.TryGetValue(id, out var ti)) id = ti;
                    costs.Add(new ExchangeCost(id, c.CurrencyCost, tomeItems.Contains(id)));
                }
                if (costs.Count == 0) continue;
                foreach (var r in entry.ReceiveItems)
                    if (r.Item.RowId != 0) exchanges.TryAdd(r.Item.RowId, costs);
            }

        // Экипировка за томестоны: самая высокая — текущий тир
        uint bestTome = 0, bestTomeIlvl = 0, bookIlvl = 0;
        var bookCands = new Dictionary<uint, HashSet<uint>>(); // ilvl -> книги
        foreach (var (id, costs) in exchanges)
        {
            var it = items.GetRowOrDefault(id);
            if (it is null || it.Value.EquipSlotCategory.RowId == 0) continue;
            var ilvl = it.Value.LevelItem.RowId;
            if (costs.Count == 1 && costs[0].Tomestone)
            {
                if (ilvl > bestTomeIlvl) { bestTomeIlvl = ilvl; bestTome = costs[0].Item; }
                continue;
            }
            // Книги: одна непередаваемая неэкипировочная «валюта», не гил и не томестоны
            if (costs.Count != 1 || costs[0].Tomestone || costs[0].Item == 1) continue;
            var cost = items.GetRowOrDefault(costs[0].Item);
            if (cost is null || cost.Value.EquipSlotCategory.RowId != 0 || !cost.Value.IsUntradable) continue;
            if (!bookCands.TryGetValue(ilvl, out var set)) bookCands[ilvl] = set = new HashSet<uint>();
            set.Add(costs[0].Item);
        }
        Tomestone = bestTome;
        // Савадж-тир: самый высокий iLvl, где книг хотя бы 3 вида (так отсекаются токены ультимейтов)
        foreach (var (ilvl, set) in bookCands.OrderByDescending(x => x.Key))
            if (set.Count >= 3) { bookIlvl = ilvl; Books = set.OrderBy(x => x).ToList(); break; }
        SavageIlvl = bookIlvl;

        // Этажи: рейды high-end (савадж) с самым большим требуемым iLvl
        var raids = Plugin.Data.GetExcelSheet<ContentFinderCondition>()
            .Where(c => c.HighEndDuty && c.ContentType.RowId == 5 && c.Name.ExtractText().Length > 0)
            .ToList();
        if (raids.Count > 0)
        {
            var top = raids.Max(c => c.ItemLevelRequired);
            Floors = raids.Where(c => c.ItemLevelRequired == top).OrderBy(c => c.RowId).Take(4)
                .Select(c => (c.RowId, GuideData.Capitalize(c.Name.ExtractText()))).ToList();
        }
    }

    // ───────────── Недельный сброс и закрытые этажи ─────────────

    /// <summary>Последний недельный сброс: вторник 08:00 UTC.</summary>
    public static DateTime LastReset
    {
        get
        {
            var now = DateTime.UtcNow;
            var d = now.Date.AddHours(8);
            while (d.DayOfWeek != DayOfWeek.Tuesday || d > now) d = d.AddDays(-1);
            return d;
        }
    }

    public static DateTime NextReset => LastReset.AddDays(7);

    public static bool ClearedThisWeek(uint cfc) =>
        Plugin.Config.SavageClears.TryGetValue(cfc, out var t) && t.ToUniversalTime() >= LastReset;

    /// <summary>Бой завершён: если это этаж саведжа — запоминаем время.</summary>
    public static void OnDutyCompleted(IDutyStateEventArgs args)
    {
        try
        {
            var sheet = Plugin.Data.GetExcelSheet<ContentFinderCondition>();
            var id = args.ContentFinderCondition.RowId;
            var territory = args.TerritoryType.RowId;
            var cfc = id != 0 ? sheet.GetRowOrDefault(id)
                : sheet.FirstOrDefault(c => c.TerritoryType.RowId == territory && c.HighEndDuty);
            if (cfc is null || cfc.Value.RowId == 0 || !cfc.Value.HighEndDuty) return;
            Plugin.Config.SavageClears[cfc.Value.RowId] = DateTime.Now;
            Plugin.Config.Save();
        }
        catch (Exception e) { Plugin.Log.Warning(e, "RaidTier: не удалось записать закрытие"); }
    }

    public static unsafe (int Got, int Max, int Have) Tomes()
    {
        var im = InventoryManager.Instance();
        if (im == null) return (0, 0, 0);
        return (im->GetWeeklyAcquiredTomestoneCount(), InventoryManager.GetLimitedTomestoneWeeklyLimit(),
            Tomestone == 0 ? 0 : im->GetInventoryItemCount(Tomestone));
    }
}
