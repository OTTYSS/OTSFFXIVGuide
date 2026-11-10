using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Книги мастер-рецептов (и другие секретные книги): что открыто, чего нет, где купить.</summary>
internal sealed unsafe class RecipeBooksTab
{
    private sealed record Book(uint Id, string Name, string Group, uint Item);

    private readonly MarketService market;
    private List<Book>? books;
    private bool onlyMissing = true;

    public RecipeBooksTab(MarketService market) => this.market = market;

    private void Build()
    {
        books = Plugin.Data.GetExcelSheet<SecretRecipeBook>()
            .Where(b => b.Name.ExtractText().Length > 0)
            .Select(b =>
            {
                var name = b.Name.ExtractText();
                var group = Regex.Replace(name, @"\s+[IVXLC]+$", "").Trim();
                return new Book(b.RowId, name, group, b.Item.RowId);
            })
            .ToList();
    }

    public void Draw()
    {
        Theme.Header("Книги рецептов", "Мастер-рецепты и другие секретные книги: что уже открыто, чего не хватает и где купить");
        books ??= new List<Book>();
        if (books.Count == 0)
        {
            try { Build(); }
            catch (Exception e) { Plugin.Log.Warning(e, "Книги: ошибка разбора"); }
        }
        var ps = PlayerState.Instance();
        if (!PlayerInfo.Ready || ps == null) { Theme.Panel("rbnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем.")); return; }

        bool Has(Book b) => ps->IsSecretRecipeBookUnlocked(b.Id);
        var done = books.Count(Has);
        Theme.Panel("rbtop", () =>
        {
            Theme.Progress("Открыто книг", done, books.Count, done == books.Count ? Theme.Green : Theme.Gold);
            ImGui.Checkbox("Только недостающие", ref onlyMissing);
            ImGui.SameLine();
            ImGui.TextColored(Theme.Gray, "Нажми на книгу — карточка предмета со всеми местами покупки.");
        });

        var region = MarketService.CurrentRegion(true);
        var items = Plugin.Data.GetExcelSheet<Item>();
        if (!ImGui.BeginChild("##rbooks", Vector2.Zero)) { ImGui.EndChild(); return; }
        foreach (var g in books.GroupBy(b => b.Group))
        {
            var list = g.Where(b => !onlyMissing || !Has(b)).ToList();
            if (list.Count == 0) continue;
            var have = g.Count(Has);
            Theme.Panel($"rb{g.Key}", () =>
            {
                ImGui.TextColored(have == g.Count() ? Theme.Green : Theme.Gold, $"{NameTranslations.Show(g.Key)} — {have} из {g.Count()}");
                foreach (var b in list)
                {
                    var owned = Has(b);
                    var extra = owned ? "открыта ✓" : "не открыта";
                    if (!owned && b.Item != 0)
                    {
                        if (RaidTier.Exchange(b.Item) is { Count: > 0 } ex)
                            extra += " · обмен: " + string.Join(" + ", ex.Select(c => $"{c.Amount}× {NameTranslations.Show(WikiView.ItemInfo(c.Item).Name)}"));
                        var it = items.GetRowOrDefault(b.Item);
                        if (region is not null && it is { } row && row.ItemSearchCategory.RowId != 0 && !row.IsUntradable)
                        {
                            market.Request(region, new[] { b.Item });
                            if (market.Get(region, b.Item) is { } p) extra += $" · рынок от {p.Price:N0} гил";
                        }
                        if (PlayerInfo.ItemCount(b.Item) > 0) extra += " · книга в сумке — прочитай!";
                    }
                    if (b.Item != 0) WikiView.ItemLine(b.Item, 0, extra, "rbi" + b.Id);
                    else ImGui.TextColored(owned ? Theme.Green : Theme.Text, $"{b.Name} — {extra}");
                }
            });
        }
        ImGui.EndChild();
    }
}
