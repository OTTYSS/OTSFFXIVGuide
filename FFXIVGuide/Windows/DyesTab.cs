using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Каталог красок: образец цвета, оттенок, где взять, цена на рынке и сколько есть.</summary>
internal sealed class DyesTab
{
    private sealed record Dye(uint Stain, uint Item, string Name, uint Color, int Shade, bool Marketable);

    private static readonly string[] Shades = { "Все", "Белые/серые", "Красные", "Коричневые", "Жёлтые", "Зелёные", "Синие", "Фиолетовые", "Особые" };
    private readonly MarketService market;
    private List<Dye>? dyes;
    private string search = "";
    private int shade;

    public DyesTab(MarketService market) => this.market = market;

    private void Build()
    {
        var en = Plugin.Data.GetExcelSheet<Item>(Dalamud.Game.ClientLanguage.English);
        var items = Plugin.Data.GetExcelSheet<Item>();
        var byName = en.Where(i => i.ItemUICategory.RowId == 55 && i.Name.ExtractText().EndsWith(" Dye", StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => i.Name.ExtractText()[..^4].ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().RowId);
        var stainsEn = Plugin.Data.GetExcelSheet<Stain>(Dalamud.Game.ClientLanguage.English);
        dyes = new List<Dye>();
        foreach (var s in Plugin.Data.GetExcelSheet<Stain>())
        {
            var name = s.Name.ExtractText();
            if (name.Length == 0 || s.RowId == 0) continue;
            var enName = stainsEn.GetRowOrDefault(s.RowId)?.Name.ExtractText().ToLowerInvariant() ?? "";
            var item = byName.GetValueOrDefault(enName);
            var it = item != 0 ? items.GetRowOrDefault(item) : null;
            dyes.Add(new Dye(s.RowId, item, GuideData.Capitalize(name), s.Color, s.Shade, it is { } r && r.ItemSearchCategory.RowId != 0 && !r.IsUntradable));
        }
    }

    private static int ShadeGroup(int shade) => shade switch
    {
        2 => 1, 4 => 2, 5 => 3, 6 => 4, 7 => 5, 8 => 6, 9 => 7, _ => 8,
    };

    public void Focus(string text) { search = text; shade = 0; }

    public IEnumerable<SearchHit> Search(string term, Action<MainTab> go)
    {
        if (dyes is null) Build();
        return dyes!.Where(d => d.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Select(d => new SearchHit("Краски", d.Name, d.Item != 0 ? "краска" : "особая краска", d.Item != 0 ? WikiView.ItemInfo(d.Item).Icon : 0, () => { Focus(d.Name); go(MainTab.Dyes); }));
    }

    public void Draw()
    {
        Theme.Header("Краски", "Все краски с образцом цвета: где взять, цена на рынке и сколько у тебя есть");
        if (dyes is null)
        {
            try { Build(); }
            catch (Exception e) { Plugin.Log.Warning(e, "Краски: ошибка разбора"); dyes = new(); }
        }
        ImGui.SetNextItemWidth(Math.Min(300 * Theme.S, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##dysearch", "Название краски…", ref search, 64);
        ImGui.SameLine();
        Theme.Chips("dyshade", Shades, ref shade);

        var list = dyes!.Where(d => shade == 0 || ShadeGroup(d.Shade) == shade)
            .Where(d => search.Length == 0 || d.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        var region = MarketService.CurrentRegion(true);
        if (region is not null) market.Request(region, list.Where(d => d.Marketable).Select(d => d.Item));
        ImGui.TextColored(Theme.TextDim, $"Красок: {list.Count}. Нажми — карточка предмета со всеми источниками. Обычные краски продаёт NPC-торговец красками в городах.");

        var rowH = 40 * Theme.S;
        if (!ImGui.BeginChild("##dyes", Vector2.Zero)) { ImGui.EndChild(); return; }
        Theme.VirtualList(list.Count, rowH, i =>
        {
            var d = list[i];
            var pos = ImGui.GetCursorScreenPos();
            var sw = rowH - 8 * Theme.S;
            var col = new Vector4(((d.Color >> 16) & 0xFF) / 255f, ((d.Color >> 8) & 0xFF) / 255f, (d.Color & 0xFF) / 255f, 1);
            ImGui.GetWindowDrawList().AddRectFilled(pos + new Vector2(0, 4 * Theme.S), pos + new Vector2(sw, 4 * Theme.S + sw), ImGui.GetColorU32(col), 6f);
            ImGui.Dummy(new Vector2(sw, rowH));
            ImGui.SameLine();
            var have = d.Item != 0 ? PlayerInfo.ItemCount(d.Item) + RetainerTracker.CountOf(d.Item).Total : 0;
            var sub = $"#{d.Color:X6}";
            if (d.Item == 0) sub += " · не продаётся как предмет";
            if (d.Marketable && region is not null && market.Get(region, d.Item) is { } p) sub += $" · рынок от {p.Price:N0} гил";
            if (Theme.Card($"##dy{d.Stain}", d.Item != 0 ? WikiView.ItemInfo(d.Item).Icon : 0, d.Name, sub, have > 0 ? $"есть ×{have}" : null, Theme.Green, false, rowH) && d.Item != 0)
                WikiNav.Open(WikiKind.Item, d.Item, d.Name);
        });
        ImGui.EndChild();
    }
}
