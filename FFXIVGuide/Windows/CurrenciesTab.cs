using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Все валюты игры: сколько есть, потолок и на что выгоднее потратить (по ценам рынка).</summary>
internal sealed unsafe class CurrenciesTab
{
    private sealed record Currency(uint Id, string Name, uint Icon, int Cap);
    private sealed record Buy(uint Item, uint Cost, uint Count);

    private readonly MarketService market;
    private List<Currency>? list;
    private Dictionary<uint, List<Buy>> buys = new();
    private uint selected;
    private string search = "";
    private bool onlyMarketable;

    private static readonly uint[] Seals = { 20, 21, 22 };

    public CurrenciesTab(MarketService market) => this.market = market;

    private void Build()
    {
        var items = Plugin.Data.GetExcelSheet<Item>();
        var tomeByIndex = new Dictionary<uint, uint>();
        foreach (var t in Plugin.Data.GetExcelSheet<TomestonesItem>())
            if (t.Tomestones.RowId != 0 && t.Item.RowId != 0) tomeByIndex[t.Tomestones.RowId] = t.Item.RowId;

        var ids = new List<uint> { 1, 20, 21, 22, 25, 27, 29 };
        ids.AddRange(tomeByIndex.Values.OrderBy(x => x));
        ids.AddRange(items.Where(i => i.ItemUICategory.RowId == 100 && i.Name.ExtractText().Length > 0).Select(i => i.RowId));

        // Обмены «валюта → предмет»
        var map = new Dictionary<uint, List<Buy>>();
        void Add(uint cur, uint item, uint cost, uint count)
        {
            if (item == 0 || cost == 0) return;
            if (!map.TryGetValue(cur, out var l)) map[cur] = l = new List<Buy>();
            if (l.All(b => b.Item != item)) l.Add(new Buy(item, cost, Math.Max(1, count)));
        }
        foreach (var shop in Plugin.Data.GetExcelSheet<SpecialShop>())
            foreach (var e in shop.Item)
            {
                var costs = e.ItemCosts.Where(c => c.ItemCost.RowId != 0 && c.CurrencyCost > 0).ToList();
                if (costs.Count != 1) continue; // смешанные цены (вещь + валюта) пропускаем
                var id = costs[0].ItemCost.RowId;
                if (id < 20)
                {
                    // Маленькие номера — индексы томестонов (только у магазинов томестонов, тип 4)
                    if (shop.UseCurrencyType != 4 || !tomeByIndex.TryGetValue(id, out var ti)) continue;
                    id = ti;
                }
                else if (shop.UseCurrencyType == 16 && id < 100) continue;
                foreach (var r in e.ReceiveItems) Add(id, r.Item.RowId, costs[0].CurrencyCost, r.ReceiveCount);
            }
        foreach (var row in Plugin.Data.GetSubrowExcelSheet<GCScripShopItem>())
            foreach (var g in row)
                foreach (var seal in Seals) Add(seal, g.Item.RowId, g.CostGCSeals, 1);
        buys = map;

        list = ids.Distinct()
            .Select(id => items.GetRowOrDefault(id))
            .Where(i => i is not null && i.Value.Name.ExtractText().Length > 0)
            .Select(i => new Currency(i!.Value.RowId, i.Value.Name.ExtractText(), i.Value.Icon, (int)i.Value.StackSize))
            .ToList();
    }

    private static long Count(uint id)
    {
        var im = InventoryManager.Instance();
        if (im == null) return 0;
        if (Seals.Contains(id))
        {
            var ps = PlayerState.Instance();
            return ps != null && ps->GrandCompany == id - 19 ? im->GetCompanySeals(ps->GrandCompany) : 0;
        }
        return im->GetInventoryItemCount(id);
    }

    public IEnumerable<SearchHit> Search(string term, Action<MainTab> go)
    {
        if (list is null) Build();
        return list!.Where(c => c.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Select(c => new SearchHit("Валюты", c.Name, "сколько есть и на что потратить", c.Icon, () => { selected = c.Id; go(MainTab.Currencies); }));
    }

    public void Draw()
    {
        Theme.Header("Валюты", "Все валюты: сколько есть, потолок и на что выгоднее потратить");
        if (list is null)
        {
            try { Build(); }
            catch (Exception e) { Plugin.Log.Warning(e, "Валюты: не удалось разобрать магазины"); list = new(); }
        }
        if (!PlayerInfo.Ready) { Theme.Panel("cnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем.")); return; }

        var avail = ImGui.GetContentRegionAvail();
        var leftW = Math.Min(360 * Theme.S, avail.X * 0.42f);
        if (ImGui.BeginChild("##curl", new Vector2(leftW, 0)))
        {
            var rows = list!.Select(c => (C: c, N: Count(c.Id))).OrderByDescending(x => x.N > 0).ThenBy(x => x.C.Id).ToList();
            var rowH = 40 * Theme.S;
            Theme.VirtualList(rows.Count, rowH, i =>
            {
                var (c, n) = rows[i];
                var cap = c.Id == 1 || c.Cap <= 1 ? "" : $" / {c.Cap:N0}";
                var near = c.Cap > 1 && c.Id != 1 && n >= c.Cap * 0.9;
                if (Theme.Card($"##cur{c.Id}", c.Icon, NameTranslations.Show(c.Name), $"{n:N0}{cap}", near ? "почти потолок" : null, Theme.Red, selected == c.Id, rowH))
                    selected = c.Id;
            });
        }
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##curr", Vector2.Zero)) DrawDetail();
        ImGui.EndChild();
    }

    private void DrawDetail()
    {
        var c = list!.FirstOrDefault(x => x.Id == selected);
        if (c is null) { Theme.EmptyState(FontAwesomeIcon.Coins, "Выбери валюту", "Слева все валюты; нажми, чтобы увидеть, на что её потратить."); return; }
        var n = Count(c.Id);
        Theme.Panel("cdet", () =>
        {
            Theme.LargeText(Theme.Gold, NameTranslations.Show(c.Name));
            if (c.Id != 1 && c.Cap > 1) Theme.Progress("Есть", (int)Math.Min(n, int.MaxValue), c.Cap, n >= c.Cap * 0.9 ? Theme.Red : Theme.Gold);
            else ImGui.TextColored(Theme.Text, $"Есть: {n:N0}");
            if (Seals.Contains(c.Id))
                ImGui.TextColored(Theme.Gray, "Печати своей Grand Company; потолок зависит от ранга в компании.");
        });

        if (!buys.TryGetValue(c.Id, out var b) || b.Count == 0)
        {
            Theme.Panel("cnob", () => ImGui.TextColored(Theme.TextDim, c.Name.Contains("Scrip")
                ? "Скрипы меняются у NPC «Scrip Exchange» (Solution Nine, Eulmore, Radz-at-Han, Old Sharlayan) — магазины скрипов игра хранит в формате, который плагин не может надёжно разобрать."
                : "Обменов за эту валюту в файлах игры не найдено (или её тратят не в магазинах)."));
            return;
        }

        var items = Plugin.Data.GetExcelSheet<Item>();
        var region = MarketService.CurrentRegion(true);
        bool Marketable(uint id) => items.GetRowOrDefault(id) is { } it && it.ItemSearchCategory.RowId != 0 && !it.IsUntradable;
        if (region is not null) market.Request(region, b.Where(x => Marketable(x.Item)).Select(x => x.Item));
        double Value(Buy x) => region is not null && market.Get(region, x.Item) is { } p ? p.Price * (double)x.Count / x.Cost : 0;

        ImGui.SetNextItemWidth(Math.Max(160 * Theme.S, ImGui.GetContentRegionAvail().X - 220 * Theme.S));
        ImGui.InputTextWithHint("##cs", "Поиск предмета…", ref search, 64);
        ImGui.SameLine();
        ImGui.Checkbox("Только для продажи", ref onlyMarketable);
        var shown = b.Where(x => !onlyMarketable || Marketable(x.Item))
            .Where(x => search.Length == 0 || WikiView.ItemInfo(x.Item).Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || NameTranslations.Show(WikiView.ItemInfo(x.Item).Name).Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(Value).ThenBy(x => x.Cost).ToList();
        ImGui.TextColored(Theme.TextDim, $"Обменов: {shown.Count}. Сверху — что дороже всего продаётся на рынке ({region ?? "мир не определён"}) в пересчёте на 1 единицу валюты.");

        var rowH = 40 * Theme.S;
        if (!ImGui.BeginChild("##curbuys", Vector2.Zero)) { ImGui.EndChild(); return; }
        Theme.VirtualList(shown.Count, rowH, i =>
        {
            var x = shown[i];
            var info = WikiView.ItemInfo(x.Item);
            var v = Value(x);
            var sub = $"{x.Cost:N0} за {(x.Count > 1 ? x.Count + " шт." : "шт.")}" + (n >= x.Cost ? " · хватает" : "");
            if (v > 0) sub += $" · рынок {market.Get(region!, x.Item)!.Price:N0} гил";
            if (Theme.Card($"##cb{c.Id}_{x.Item}", info.Icon, NameTranslations.Show(info.Name), sub, v > 0 ? $"{v:N0} гил/ед." : null, Theme.Gold, false, rowH))
                WikiNav.Open(WikiKind.Item, x.Item, info.Name);
        });
        ImGui.EndChild();
    }
}
