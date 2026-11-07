using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

public enum SourceKind { GilShop, Exchange, Craft, Gather, Market, Other }

/// <summary>Один способ получить предмет.</summary>
public sealed record ItemSource(SourceKind Kind, string Text, bool Easy, uint LevelId = 0);

/// <summary>
/// Откуда берутся предметы: магазины за гил, обмен на валюту/томестоны, крафт, рынок.
/// Собирается из файлов игры при первом обращении.
/// Дроп из данжей в файлах клиента не описан — такие вещи попадают в «Другое».
/// </summary>
public sealed class ItemSources
{
    private readonly Dictionary<uint, List<ItemSource>> map = new();

    public ItemSources()
    {
        var items = Plugin.Data.GetExcelSheet<Item>();
        BuildNpcIndex();

        // Магазины за гил
        foreach (var row in Plugin.Data.GetSubrowExcelSheet<GilShopItem>())
        {
            foreach (var s in row)
            {
                var id = s.Item.RowId;
                if (id == 0 || Has(id, SourceKind.GilShop)) continue;
                var price = s.Item.ValueNullable?.PriceMid ?? 0;
                var (npc, level) = NpcFor(row.RowId);
                Add(id, new ItemSource(SourceKind.GilShop, npc.Length > 0 ? $"У {npc} за {price:N0} гил" : $"У NPC за {price:N0} гил", true, level));
            }
        }

        // Томестоны: маленькие id в обменниках — это индексы, а не предметы
        var tomestoneByIndex = new Dictionary<uint, uint>();
        foreach (var t in Plugin.Data.GetExcelSheet<TomestonesItem>())
            if (t.Tomestones.RowId != 0 && t.Item.RowId != 0)
                tomestoneByIndex[t.Tomestones.RowId] = t.Item.RowId;

        foreach (var shop in Plugin.Data.GetExcelSheet<SpecialShop>())
        {
            foreach (var entry in shop.Item)
            {
                foreach (var receive in entry.ReceiveItems)
                {
                    var id = receive.Item.RowId;
                    if (id == 0 || CountOf(id, SourceKind.Exchange) >= 2) continue;

                    var costs = new List<string>();
                    var easy = true;
                    foreach (var c in entry.ItemCosts)
                    {
                        var costId = c.ItemCost.RowId;
                        if (costId == 0 || c.CurrencyCost == 0) continue;

                        if (costId < 100 && (shop.UseCurrencyType == 4 || shop.UseCurrencyType == 16)
                            && tomestoneByIndex.TryGetValue(costId, out var tomeItem))
                            costId = tomeItem;

                        var name = items.GetRowOrDefault(costId)?.Name.ExtractText() ?? $"#{costId}";
                        costs.Add($"{c.CurrencyCost} × {name}");

                        // «Лёгкие» валюты: гил, томестоны, сертификаты компании. Токены рейдов — нет.
                        var isTome = name.StartsWith("Allagan Tomestone", System.StringComparison.OrdinalIgnoreCase);
                        var isSeal = name.Contains("Seal", System.StringComparison.OrdinalIgnoreCase);
                        if (costId != 1 && !isTome && !isSeal) easy = false;
                    }

                    if (costs.Count == 0) continue;
                    var (npc, level) = NpcFor(shop.RowId);
                    var text = $"Обмен: {string.Join(" + ", costs)}" + (npc.Length > 0 ? $" — у {npc}" : "");
                    if (map.TryGetValue(id, out var existing) && existing.Any(e => e.Text == text)) continue;
                    Add(id, new ItemSource(SourceKind.Exchange, text, easy, level));
                }
            }
        }

        // Крафт
        foreach (var r in Plugin.Data.GetExcelSheet<Recipe>())
        {
            var id = r.ItemResult.RowId;
            if (id == 0 || Has(id, SourceKind.Craft)) continue;
            var craft = r.CraftType.ValueNullable?.Name.ExtractText() ?? "крафт";
            var lvl = r.RecipeLevelTable.ValueNullable?.ClassJobLevel ?? 0;
            Add(id, new ItemSource(SourceKind.Craft, $"Крафт: {craft}, ур. {lvl}", true));
        }
    }

    public IReadOnlyList<ItemSource> Get(uint itemId, bool marketable)
    {
        var list = map.TryGetValue(itemId, out var l) ? new List<ItemSource>(l) : new List<ItemSource>();
        var spot = GatherIndex.For(itemId).FirstOrDefault();
        if (spot is not null)
            list.Add(new ItemSource(SourceKind.Gather, $"Сбор: {spot.Type} ур. {spot.Level} — {spot.Zone}", true));
        if (marketable)
            list.Add(new ItemSource(SourceKind.Market, "Рынок (маркетборд)", true));
        if (list.Count == 0)
            list.Add(new ItemSource(SourceKind.Other, "Дроп или награда (данжи, рейды, квесты)", false));
        return list;
    }

    /// <summary>Можно купить у NPC, за гил/томестоны, скрафтить или купить на рынке.</summary>
    public bool IsEasy(uint itemId, bool marketable) =>
        marketable || (map.TryGetValue(itemId, out var l) && l.Any(s => s.Easy));

    // ── NPC-торговцы: магазин -> (имя NPC с местом, Level для флага)
    private readonly Dictionary<uint, (string Npc, uint Level)> shopNpc = new();

    private (string, uint) NpcFor(uint shopId) => shopNpc.TryGetValue(shopId, out var v) ? v : ("", 0);

    private void BuildNpcIndex()
    {
        try
        {
            var npcLevel = new Dictionary<uint, Level>();
            foreach (var lv in Plugin.Data.GetExcelSheet<Level>())
                if (lv.Object.RowId != 0 && lv.Territory.RowId != 0)
                    npcLevel.TryAdd(lv.Object.RowId, lv);

            var topics = Plugin.Data.GetExcelSheet<TopicSelect>();
            var pre = Plugin.Data.GetExcelSheet<PreHandler>();
            var residents = Plugin.Data.GetExcelSheet<ENpcResident>();

            foreach (var npc in Plugin.Data.GetExcelSheet<ENpcBase>())
            {
                var name = residents.GetRowOrDefault(npc.RowId)?.Singular.ExtractText();
                if (string.IsNullOrEmpty(name)) continue;
                // Не у всех NPC есть точка в таблице Level — тогда только имя, без флага
                var hasLevel = npcLevel.TryGetValue(npc.RowId, out var lv);
                var place = hasLevel ? lv.Territory.ValueNullable?.PlaceName.ValueNullable?.Name.ExtractText() ?? "" : "";
                var label = place.Length > 0 ? $"{GuideData.Capitalize(name)} ({place})" : GuideData.Capitalize(name);
                var levelId = hasLevel ? lv.RowId : 0u;

                void Link(uint id)
                {
                    if (id == 0) return;
                    shopNpc.TryAdd(id, (label, levelId));
                    if (pre.GetRowOrDefault(id) is { } p) shopNpc.TryAdd(p.Target.RowId, (label, levelId));
                }

                foreach (var d in npc.ENpcData)
                {
                    var id = d.RowId;
                    Link(id);
                    if (pre.GetRowOrDefault(id) is { } ph && topics.GetRowOrDefault(ph.Target.RowId) is { } t2)
                        foreach (var s2 in t2.Shop) Link(s2.RowId);
                    if (topics.GetRowOrDefault(id) is { } t)
                        foreach (var s in t.Shop) Link(s.RowId);
                }
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.Warning(e, "Не удалось построить список торговцев");
        }
    }

    /// <summary>Флаг на карте у торговца.</summary>
    public static bool FlagLevel(uint levelId)
    {
        var level = Plugin.Data.GetExcelSheet<Level>().GetRowOrDefault(levelId);
        if (level is null) return false;
        var link = new Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload(
            level.Value.Territory.RowId, level.Value.Map.RowId, (int)(level.Value.X * 1000f), (int)(level.Value.Z * 1000f));
        return Plugin.GameGui.OpenMapWithMapLink(link);
    }

    private bool Has(uint id, SourceKind kind) => CountOf(id, kind) > 0;
    private int CountOf(uint id, SourceKind kind) => map.TryGetValue(id, out var l) ? l.Count(s => s.Kind == kind) : 0;

    private void Add(uint id, ItemSource s)
    {
        if (!map.TryGetValue(id, out var l))
            map[id] = l = new List<ItemSource>();
        l.Add(s);
    }
}
