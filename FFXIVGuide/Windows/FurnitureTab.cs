using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Каталог мебели и хаузинга: категории, источники, цена на рынке, что уже есть.</summary>
internal sealed class FurnitureTab
{
    private sealed record Row(uint Id, string Name, uint Icon, string Category, bool Outdoor, bool Marketable);

    private readonly MarketService market;
    private List<Row>? rows;
    private string[] categories = { "Все" };
    private int category;
    private string search = "";
    private bool onlyOwned;
    private List<Row> filtered = new();
    private string lastKey = "";

    public FurnitureTab(MarketService market) => this.market = market;

    private void Build()
    {
        var items = Plugin.Data.GetExcelSheet<Item>();
        var indoor = Plugin.Data.GetExcelSheet<HousingFurniture>().Where(f => f.Item.RowId != 0).Select(f => f.Item.RowId).ToHashSet();
        var outdoor = Plugin.Data.GetExcelSheet<HousingYardObject>().Where(f => f.Item.RowId != 0).Select(f => f.Item.RowId).ToHashSet();
        rows = indoor.Concat(outdoor).Distinct()
            .Select(id => items.GetRowOrDefault(id))
            .Where(i => i is not null && i.Value.Name.ExtractText().Length > 0)
            .Select(i => i!.Value)
            .Select(i => new Row(i.RowId, i.Name.ExtractText(), i.Icon, i.ItemUICategory.ValueNullable?.Name.ExtractText() ?? "", outdoor.Contains(i.RowId),
                i.ItemSearchCategory.RowId != 0 && !i.IsUntradable))
            .OrderBy(r => r.Name)
            .ToList();
        categories = new[] { "Все" }.Concat(rows.Select(r => r.Category).Where(c => c.Length > 0).Distinct().OrderBy(c => c)).ToArray();
    }

    public void Draw()
    {
        Theme.Header("Мебель и хаузинг", "Вся мебель по категориям: где взять, сколько стоит на рынке, что уже есть у тебя");
        if (rows is null) Build();

        ImGui.SetNextItemWidth(220 * Theme.S);
        if (ImGui.BeginCombo("##fcat", categories[category]))
        {
            for (var i = 0; i < categories.Length; i++)
                if (ImGui.Selectable(categories[i], i == category)) category = i;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Math.Max(200 * Theme.S, ImGui.GetContentRegionAvail().X - 180 * Theme.S));
        ImGui.InputTextWithHint("##fsearch", "Название (можно по-русски)…", ref search, 64);
        ImGui.SameLine();
        ImGui.Checkbox("Только мои", ref onlyOwned);

        var key = $"{category}|{search}|{onlyOwned}";
        if (key != lastKey)
        {
            lastKey = key;
            var term = search.Trim();
            filtered = rows!.Where(r => (category == 0 || r.Category == categories[category])
                                        && (term.Length < 2 || NameTranslations.Matches(r.Name, term))
                                        && (!onlyOwned || PlayerInfo.ItemCount(r.Id) + RetainerTracker.CountOf(r.Id).Total > 0)).ToList();
        }
        ImGui.TextColored(Theme.TextDim, $"Найдено: {filtered.Count} из {rows!.Count}. Нажми — карточка предмета со всеми источниками.");

        var region = MarketService.CurrentRegion(false);
        var rowH = 44 * Theme.S;
        if (!ImGui.BeginChild("##furn", Vector2.Zero)) { ImGui.EndChild(); return; }
        Theme.VirtualList(filtered.Count, rowH, i =>
        {
            var r = filtered[i];
            var have = PlayerInfo.ItemCount(r.Id) + RetainerTracker.CountOf(r.Id).Total;
            var sub = r.Category + (r.Outdoor ? " · для участка" : "");
            if (r.Marketable && region is not null)
            {
                market.Request(region, new[] { r.Id });
                var p = market.Get(region, r.Id);
                if (p is not null) sub += $" · рынок от {p.Price:N0} гил ({p.World})";
            }
            else if (!r.Marketable) sub += " · не продаётся на рынке";
            if (Theme.Card($"##fu{r.Id}", r.Icon, r.Name, sub, have > 0 ? $"есть ×{have}" : null, Theme.Green, false, rowH))
                WikiNav.Open(WikiKind.Item, r.Id, r.Name);
        });
        ImGui.EndChild();
    }
}
