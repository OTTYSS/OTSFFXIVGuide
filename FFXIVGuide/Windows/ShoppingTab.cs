using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

/// <summary>
/// Список покупок: что докупить (с учётом сумок и ретейнеров) и на каком мире датацентра дешевле.
/// Пополняется из дерева крафта в карточке предмета.
/// </summary>
internal sealed class ShoppingTab
{
    private readonly MarketService market;

    public ShoppingTab(MarketService market) => this.market = market;

    // ───────────── Хранение ─────────────

    public static List<(uint Item, int Qty)> Items =>
        Plugin.Config.ShoppingList.Select(s => s.Split('|')).Where(p => p.Length == 2 && uint.TryParse(p[0], out _) && int.TryParse(p[1], out _))
            .Select(p => (uint.Parse(p[0]), int.Parse(p[1]))).ToList();

    private static void Save(IEnumerable<(uint Item, int Qty)> list)
    {
        Plugin.Config.ShoppingList = list.Where(x => x.Qty > 0).Select(x => $"{x.Item}|{x.Qty}").ToList();
        Plugin.Config.Save();
    }

    public static void Add(uint item, int qty)
    {
        var list = Items;
        var i = list.FindIndex(x => x.Item == item);
        if (i >= 0) list[i] = (item, list[i].Qty + qty); else list.Add((item, qty));
        Save(list);
    }

    // ───────────── Окно ─────────────

    public void Draw()
    {
        Theme.Header("Список покупок", "Что докупить и на каком мире датацентра дешевле. Добавляй сырьё из дерева крафта в карточке предмета.");
        var list = Items;
        if (list.Count == 0)
        {
            Theme.EmptyState(FontAwesomeIcon.ShoppingCart, "Список пуст", "Открой предмет с рецептом → «Дерево крафта» → «В список покупок». Сырьё на нужное количество попадёт сюда.");
            return;
        }

        var dc = MarketService.CurrentRegion(false);
        if (dc is not null) market.Request(dc, list.Select(x => x.Item));

        var rows = list.Select(x =>
        {
            var have = PlayerInfo.ItemCount(x.Item) + RetainerTracker.CountOf(x.Item).Total;
            var need = Math.Max(0, x.Qty - have);
            var p = dc is null ? null : market.Get(dc, x.Item);
            return (x.Item, x.Qty, have, need, p);
        }).ToList();

        var total = rows.Where(r => r.p is not null).Sum(r => r.p!.Price * r.need);
        Theme.Panel("shsum", () =>
        {
            Theme.FaIcon(FontAwesomeIcon.Coins, Theme.Gold);
            ImGui.SameLine();
            Theme.LargeText(Theme.Gold, $"~{total:N0} гил");
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextDim, dc is null ? "зайди персонажем, чтобы увидеть цены" : $"по самым дешёвым лотам на {dc} · уже есть у тебя — не считается");
            if (Theme.IconButton("shclear", FontAwesomeIcon.Trash, "Очистить список")) { Save(Array.Empty<(uint, int)>()); }
        });

        if (!ImGui.BeginChild("##shop", Vector2.Zero)) { ImGui.EndChild(); return; }
        // По мирам: куда лететь за чем
        foreach (var g in rows.Where(r => r.need > 0).GroupBy(r => r.p?.World ?? "нет лотов / нет цены").OrderByDescending(g => g.Sum(r => (r.p?.Price ?? 0) * r.need)))
        {
            Theme.Panel($"shw{g.Key}", () =>
            {
                Theme.SubHeader($"{g.Key} · ~{g.Sum(r => (r.p?.Price ?? 0) * r.need):N0} гил");
                foreach (var r in g)
                {
                    WikiView.ItemLine(r.Item, r.need, r.p is null ? $"нужно {r.Qty}, есть {r.have}" : $"по {r.p.Price:N0} = {r.p.Price * r.need:N0} гил · нужно {r.Qty}, есть {r.have}", "sh");
                    ItemActions(r.Item, r.Qty);
                }
            });
        }

        var done = rows.Where(r => r.need == 0).ToList();
        if (done.Count > 0)
            Theme.Panel("shdone", () =>
            {
                Theme.SubHeader($"Уже хватает · {done.Count}");
                foreach (var r in done)
                {
                    WikiView.ItemLine(r.Item, r.Qty, $"есть {r.have} ✓", "shd");
                    ItemActions(r.Item, r.Qty);
                }
            });
        ImGui.EndChild();
    }

    private static void ItemActions(uint item, int qty)
    {
        ImGui.Indent(30 * Theme.S);
        if (ImGui.SmallButton($"-##m{item}")) Change(item, -1);
        ImGui.SameLine();
        if (ImGui.SmallButton($"+##p{item}")) Change(item, 1);
        ImGui.SameLine();
        if (ImGui.SmallButton($"Копировать название##c{item}")) ImGui.SetClipboardText(WikiView.ItemInfo(item).Name);
        ImGui.SameLine();
        if (ImGui.SmallButton($"Убрать##r{item}")) Change(item, -qty);
        ImGui.Unindent(30 * Theme.S);
    }

    private static void Change(uint item, int delta)
    {
        var list = Items;
        var i = list.FindIndex(x => x.Item == item);
        if (i < 0) return;
        list[i] = (item, list[i].Qty + delta);
        Save(list);
    }
}
