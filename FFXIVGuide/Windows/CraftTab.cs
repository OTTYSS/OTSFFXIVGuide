using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Крафт: рецепты, что есть из материалов, стоимость и прибыль на рынке твоего мира.</summary>
internal sealed class CraftTab
{
    private sealed record RecipeRow(uint Id, uint ResultId, string Name, uint Icon, int Level, uint CraftType, int Yield, (uint Item, int Amount)[] Ingredients, bool Marketable);
    private sealed record Profit(RecipeRow Recipe, long Sale, long Cost);

    private const long MarketTax = 5; // %

    private readonly MarketService market;
    private List<RecipeRow>? recipes;
    private string[]? craftNames;
    private int craftIdx;
    private string search = "";
    private RecipeRow? selected;
    private List<RecipeRow>? profitCandidates;

    public CraftTab(MarketService market) => this.market = market;

    public void Draw()
    {
        recipes ??= Build();
        craftNames ??= Plugin.Data.GetExcelSheet<CraftType>().Where(c => !c.Name.IsEmpty).Select(c => c.Name.ExtractText()).ToArray();

        Theme.Header("Крафт", "Что нужно для рецепта, что уже есть и сколько можно заработать");

        var region = MarketService.CurrentRegion(true);
        var level = CrafterLevel((uint)craftIdx);

        Theme.Chips("craft", craftNames, ref craftIdx);
        ImGui.TextColored(Theme.TextDim, level > 0 ? $"Твой уровень {craftNames[craftIdx]}: {level}" : $"{craftNames[craftIdx]} ещё не открыт или персонаж не загружен");
        Theme.Search("##rsearch", "Название того, что крафтим…", ref search);

        var avail = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##rlist", new Vector2(avail.X * 0.45f, 0)))
        {
            var list = recipes
                .Where(r => r.CraftType == craftIdx)
                .Where(r => search.Length < 2 ? (level == 0 || (r.Level <= level && r.Level >= level - 5)) : NameTranslations.Matches(r.Name, search))
                .OrderByDescending(r => r.Level).ThenBy(r => r.Name)
                .Take(300)
                .ToList();

            if (search.Length < 2)
                ImGui.TextColored(Theme.TextDim, level > 0 ? $"Рецепты ур. {Math.Max(1, level - 5)}–{level}. Введи название для поиска по всем." : "Введи название рецепта.");

            var rowH = 42 * Theme.S;
            Theme.VirtualList(list.Count, rowH, i =>
            {
                var r = list[i];
                if (Theme.Card($"##r{r.Id}", r.Icon, r.Name, $"ур. {r.Level}" + (r.Yield > 1 ? $" · даёт ×{r.Yield}" : ""), null, Theme.Gray, selected == r, rowH))
                    selected = r;
            });
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("##rdetails", Vector2.Zero))
        {
            if (selected is not null)
                DrawRecipe(selected, region);
            DrawProfitable(region, level);
        }
        ImGui.EndChild();
    }

    private void DrawRecipe(RecipeRow r, string? region)
    {
        if (region is not null)
            market.Request(region, r.Ingredients.Select(x => x.Item).Append(r.ResultId));

        Theme.Panel($"rec{r.Id}", () =>
        {
            var pos = ImGui.GetCursorScreenPos();
            Theme.GameIcon(r.Icon, pos, 44 * Theme.S, 6f);
            ImGui.SetCursorScreenPos(pos + new Vector2(54 * Theme.S, 0));
            ImGui.BeginGroup();
            Theme.LargeText(Theme.Gold, r.Name);
            ImGui.TextColored(Theme.TextDim, $"{craftNames![r.CraftType]} · ур. {r.Level}" + (r.Yield > 1 ? $" · даёт ×{r.Yield}" : ""));
            ImGui.EndGroup();
            ImGui.SetCursorScreenPos(new Vector2(pos.X, Math.Max(ImGui.GetCursorScreenPos().Y, pos.Y + 50 * Theme.S)));

            Theme.SubHeader("Материалы");
            long costAll = 0, costMissing = 0;
            var unknown = false;
            var sheet = Plugin.Data.GetExcelSheet<Item>();
            foreach (var (item, amount) in r.Ingredients)
            {
                var row = sheet.GetRowOrDefault(item);
                if (row is null) continue;
                var have = PlayerInfo.ItemCount(item);
                var price = region is null ? null : market.Get(region, item);
                if (price is null) unknown = true;
                var unit = price?.Price ?? 0;
                costAll += unit * amount;
                costMissing += unit * Math.Max(0, amount - have);

                var h = ImGui.GetTextLineHeight() * 1.3f;
                var p = ImGui.GetCursorScreenPos();
                Theme.GameIcon(row.Value.Icon, p, h, 3f);
                ImGui.Dummy(new Vector2(h, h));
                ImGui.SameLine();
                ImGui.TextColored(Theme.Text, $"{row.Value.Name.ExtractText()} ×{amount}");
                ImGui.SameLine();
                Theme.InlineBadge($"есть {have}", have >= amount ? Theme.Green : have > 0 ? Theme.Yellow : Theme.Gray);
                if (price is not null)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.TextDim, $"{price.Price:N0} гил/шт");
                }
            }

            Theme.SubHeader("Деньги");
            var sale = region is null ? null : market.Get(region, r.ResultId);
            if (!r.Marketable)
            {
                ImGui.TextColored(Theme.TextDim, "Результат нельзя продать на рынке.");
            }
            else if (sale is null)
            {
                ImGui.TextColored(Theme.TextDim, region is null ? "Цены недоступны." : "Цена результата загружается или лотов нет.");
            }
            else
            {
                var revenue = sale.Price * r.Yield * (100 - MarketTax) / 100;
                ImGui.TextColored(Theme.Text, $"Продажа: ~{revenue:N0} гил (с налогом {MarketTax}%)");
                ImGui.TextColored(Theme.Text, $"Купить всё: ~{costAll:N0} гил → прибыль {revenue - costAll:N0}");
                ImGui.TextColored(Theme.Text, $"Докупить недостающее: ~{costMissing:N0} гил → прибыль {revenue - costMissing:N0}");
            }
            if (unknown) ImGui.TextColored(Theme.Gray, "Часть цен ещё не загружена или на рынке нет лотов.");
            ImGui.TextColored(Theme.Gray, $"Цены: Universalis, мир {region ?? "?"}.");
        });
    }

    private void DrawProfitable(string? region, int level)
    {
        Theme.Panel("rprofit", () =>
        {
            Theme.SubHeader("Выгодные рецепты на твоём мире");
            if (region is null || level == 0)
            {
                ImGui.TextColored(Theme.TextDim, "Нужен загруженный персонаж с открытым ремеслом.");
                return;
            }

            if (Theme.IconButton("findprofit", FontAwesomeIcon.Coins, "Найти выгодные рецепты"))
            {
                profitCandidates = recipes!
                    .Where(r => r.CraftType == craftIdx && r.Marketable && r.Level <= level && r.Level >= Math.Max(1, level - 15))
                    .OrderByDescending(r => r.Level)
                    .Take(120)
                    .ToList();
                market.Request(region, profitCandidates.SelectMany(r => r.Ingredients.Select(x => x.Item).Append(r.ResultId)));
            }
            ImGui.SameLine();
            ImGui.TextColored(Theme.Gray, $"До 120 рецептов ур. {Math.Max(1, level - 15)}–{level}");

            if (profitCandidates is null) return;

            var results = new List<Profit>();
            var loading = 0;
            foreach (var r in profitCandidates)
            {
                var sale = market.Get(region, r.ResultId);
                if (sale is null) { if (market.IsLoading(region, r.ResultId)) loading++; continue; }
                long cost = 0;
                var ok = true;
                foreach (var (item, amount) in r.Ingredients)
                {
                    var p = market.Get(region, item);
                    if (p is null) { ok = false; break; }
                    cost += p.Price * amount;
                }
                if (!ok) continue;
                results.Add(new Profit(r, sale.Price * r.Yield * (100 - MarketTax) / 100, cost));
            }

            if (loading > 0) ImGui.TextColored(Theme.TextDim, $"Загружаю цены… ({loading})");
            foreach (var p in results.OrderByDescending(x => x.Sale - x.Cost).Take(20))
            {
                var profit = p.Sale - p.Cost;
                if (Theme.Card($"##pf{p.Recipe.Id}", p.Recipe.Icon, p.Recipe.Name,
                        $"ур. {p.Recipe.Level} · продажа ~{p.Sale:N0} · материалы ~{p.Cost:N0}",
                        $"{(profit >= 0 ? "+" : "")}{profit:N0}", profit > 0 ? Theme.Green : Theme.Red, selected == p.Recipe, 42 * Theme.S))
                    selected = p.Recipe;
            }
            ImGui.TextColored(Theme.Gray, "Прибыль = цена результата минус налог минус материалы по рынку. Без учёта времени и HQ.");
        });
    }

    private static int CrafterLevel(uint craftType)
    {
        if (!PlayerInfo.Ready) return 0;
        var job = Plugin.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(8 + craftType);
        return job is null ? 0 : Plugin.PlayerState.GetClassJobLevel(job.Value);
    }

    private static List<RecipeRow> Build()
    {
        var list = new List<RecipeRow>();
        foreach (var r in Plugin.Data.GetExcelSheet<Recipe>())
        {
            var result = r.ItemResult.ValueNullable;
            if (result is null || r.ItemResult.RowId == 0) continue;
            var name = result.Value.Name.ExtractText();
            if (name.Length == 0) continue;
            var ingredients = new List<(uint, int)>();
            for (var i = 0; i < r.Ingredient.Count; i++)
            {
                var id = r.Ingredient[i].RowId;
                var amt = i < r.AmountIngredient.Count ? r.AmountIngredient[i] : (byte)0;
                if (id != 0 && amt > 0) ingredients.Add((id, amt));
            }
            list.Add(new RecipeRow(r.RowId, r.ItemResult.RowId, name, result.Value.Icon,
                r.RecipeLevelTable.ValueNullable?.ClassJobLevel ?? 0, r.CraftType.RowId, Math.Max(1, (int)r.AmountResult),
                ingredients.ToArray(), result.Value.ItemSearchCategory.RowId != 0 && !result.Value.IsUntradable));
        }
        return list;
    }
}
