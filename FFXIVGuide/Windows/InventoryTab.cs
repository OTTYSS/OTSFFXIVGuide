using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

/// <summary>
/// Разбор инвентаря: что устарело, что дорого стоит, что можно сложить.
/// Только подсказки — все действия игрок делает сам.
/// </summary>
internal sealed class InventoryTab
{
    private static long ValuableThreshold => Plugin.Config.ValuableThreshold;
    private static bool Excluded(uint id) => Plugin.Config.ExcludedItems.Contains(id);

    private readonly MarketService market;
    private InventoryAnalyzer.Result? result;
    private DateTime nextScan = DateTime.MinValue;
    private uint copiedId;
    private bool expanded;
    private DateTime copiedAt;

    public InventoryTab(MarketService market) => this.market = market;

    public void Draw()
    {
        Theme.Header("Разбор инвентаря", "Подсказки, что выбросить, продать или сдать. Всё делаешь сам — плагин ничего не трогает.");

        if (!PlayerInfo.Ready)
        {
            Theme.Panel("inone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем, чтобы разобрать инвентарь."));
            return;
        }

        if (result is null || DateTime.Now >= nextScan)
        {
            result = InventoryAnalyzer.Scan();
            nextScan = DateTime.Now.AddSeconds(5);
        }
        if (result is null) return;

        var region = MarketService.CurrentRegion(true);
        if (region is not null)
            market.Request(region, result.Items.Where(i => i.Marketable).Select(i => i.ItemId));

        MarketPrice? Price(InvItem it) => region is null || !it.Marketable ? null : market.Get(region, it.ItemId);

        var outdated = result.Outdated
            .Where(o => !Excluded(o.Item.ItemId))
            .Select(o => (o, advice: InventoryAnalyzer.Advise(o.Item, Price(o.Item))))
            .OrderByDescending(x => x.advice.Count > 0 ? x.advice.Max(a => a.Kind == ActionKind.GrandCompany ? 0 : a.Value) : 0)
            .ToList();

        var valuable = result.Items
            .Where(i => i.Marketable && InventoryAnalyzer.Bags.Contains(i.Container))
            .Select(i => (it: i, p: Price(i)))
            .Where(x => x.p is not null && x.p.Price * x.it.Quantity >= ValuableThreshold && x.p.Price > x.it.Row.PriceLow * 3)
            .Where(x => !result.GearsetItems.Contains(x.it.ItemId) && !Excluded(x.it.ItemId))
            .OrderByDescending(x => x.p!.Price * x.it.Quantity)
            .Take(25)
            .ToList();

        var freed = outdated.Count + result.Stacks.Sum(s => s.SlotsFreed);
        var gil = outdated.Sum(x => x.advice.Where(a => a.Kind is ActionKind.Market or ActionKind.Vendor).Select(a => a.Value).DefaultIfEmpty(0).Max());
        var seals = outdated.Sum(x => x.advice.Where(a => a.Kind == ActionKind.GrandCompany).Select(a => a.Value).DefaultIfEmpty(0).Max());

        if (!ImGui.BeginChild("##inv", Vector2.Zero))
        {
            ImGui.EndChild();
            return;
        }

        // ── Сводка
        Theme.Panel("isum", () =>
        {
            Theme.SubHeader("Сводка");
            Theme.Progress("Сумки", result.BagUsed, result.BagTotal, result.BagUsed > result.BagTotal * 0.9 ? Theme.Red : Theme.Gold);
            Theme.Progress("Броне-шкаф", result.ArmoryUsed, result.ArmoryTotal, Theme.Blue);

            Theme.FaIcon(FontAwesomeIcon.Broom, Theme.Gold);
            ImGui.SameLine();
            ImGui.TextColored(Theme.Text, $"Можно освободить примерно {freed} слотов");
            if (gil > 0 || seals > 0)
            {
                Theme.FaIcon(FontAwesomeIcon.Coins, Theme.Gold);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Text, $"За устаревшее: до ~{gil:N0} гил или ~{seals:N0} печатей GC");
            }
            if (region is null)
                ImGui.TextColored(Theme.TextDim, "Цены рынка недоступны — не удалось определить мир.");
            else if (result.Items.Any(i => i.Marketable && market.IsLoading(region, i.ItemId)))
                ImGui.TextColored(Theme.TextDim, $"Загружаю цены рынка ({region})…");

            if (Theme.IconButton("irescan", FontAwesomeIcon.SyncAlt, "Обновить"))
                nextScan = DateTime.MinValue;
            ImGui.SameLine();
            ImGui.TextColored(Theme.Gray, "Обновляется само раз в 5 секунд. Вещи из гирсетов не трогаются.");
        });

        // ── Устаревшее снаряжение
        Theme.Panel("iold", () =>
        {
            Theme.SubHeader($"Устаревшее снаряжение · {outdated.Count}");
            if (outdated.Count == 0)
            {
                ImGui.TextColored(Theme.TextDim, "Нет: всё снаряжение либо в гирсетах, либо лучшее в своём слоте.");
                return;
            }
            ImGui.TextColored(Theme.TextDim, "Не входит ни в один гирсет, и для тех же профессий у тебя есть вещь лучше.");
            ImGui.Dummy(new Vector2(0, 2 * Theme.S));

            foreach (var (o, advice) in outdated)
            {
                ItemRow(o.Item, $"{o.Item.Location} · iLvl {o.Item.Row.LevelItem.RowId} · есть лучше: {o.BetterName} ({o.BetterIlvl})");
                AdviceRow(o.Item, advice);
            }
        });

        // ── Дорогое
        Theme.Panel("ival", () =>
        {
            Theme.SubHeader($"Дорого стоит на рынке · {valuable.Count}");
            if (valuable.Count == 0)
            {
                ImGui.TextColored(Theme.TextDim, region is null ? "Нет данных о ценах." : $"В сумках нет вещей дороже {ValuableThreshold:N0} гил на {region}.");
                return;
            }
            ImGui.TextColored(Theme.TextDim, $"Цены — самый дешёвый лот на {region} (Universalis). Выставь через ретейнера.");
            ImGui.Dummy(new Vector2(0, 2 * Theme.S));
            foreach (var (it, p) in valuable)
            {
                ItemRow(it, $"{it.Location} · {p!.Price:N0} гил за шт. · у NPC {it.Row.PriceLow:N0}");
                ImGui.Dummy(new Vector2(30 * Theme.S, 0));
                ImGui.SameLine();
                Theme.InlineBadge($"~{p.Price * it.Quantity:N0} гил", Theme.Gold);
                ImGui.SameLine();
                CopyButton(it);
                ImGui.SameLine();
                ExcludeButton(it);
                ImGui.Dummy(new Vector2(0, 3 * Theme.S));
            }
        });

        // ── Ретейнеры
        Theme.Panel("iret", () =>
        {
            var rets = RetainerTracker.All.ToList();
            Theme.SubHeader($"У ретейнеров · {rets.Count}");
            if (rets.Count == 0)
            {
                ImGui.TextColored(Theme.TextDim, "Открой окно ретейнера у колокольчика — плагин запомнит, что у него лежит.");
                return;
            }
            var sheet = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            foreach (var r in rets)
            {
                ImGui.TextColored(Theme.Gold, r.Name);
                ImGui.SameLine();
                ImGui.TextColored(Theme.TextDim, $"{r.Items.Count} слотов · обновлено {r.Updated:dd.MM HH:mm}");
                if (region is null) continue;
                market.Request(region, r.Items.Select(i => i.ItemId));
                var rich = r.Items
                    .Select(i => (i, row: sheet.GetRowOrDefault(i.ItemId), p: market.Get(region, i.ItemId)))
                    .Where(x => x.row is not null && x.p is not null && !Excluded(x.i.ItemId)
                                && x.p.Price * x.i.Quantity >= ValuableThreshold && x.p.Price > x.row.Value.PriceLow * 3)
                    .OrderByDescending(x => x.p!.Price * x.i.Quantity)
                    .ToList();
                if (rich.Count > 0)
                {
                    ImGui.SameLine();
                    Theme.InlineBadge($"можно продать на ~{rich.Sum(x => x.p!.Price * x.i.Quantity):N0} гил", Theme.Green);
                }
                foreach (var (i, row, p) in rich.Take(8))
                {
                    ImGui.Dummy(new Vector2(16 * Theme.S, 0));
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.Text, $"{row!.Value.Name.ExtractText()} ×{i.Quantity}");
                    ImGui.SameLine();
                    Theme.InlineBadge($"~{p!.Price * i.Quantity:N0} гил", Theme.Gold);
                }
            }
        });

        // ── Одно и то же в разных местах
        Theme.Panel("idup", () =>
        {
            var places = new Dictionary<uint, List<(string Where, int Qty)>>();
            void Add(uint id, string where, int qty)
            {
                if (!places.TryGetValue(id, out var l)) places[id] = l = new();
                var i = l.FindIndex(x => x.Where == where);
                if (i >= 0) l[i] = (where, l[i].Qty + qty); else l.Add((where, qty));
            }
            foreach (var it in result.Items.Where(i => i.Row.StackSize > 1))
                Add(it.ItemId, InventoryAnalyzer.Bags.Contains(it.Container) ? "сумки" : InventoryAnalyzer.Saddle.Contains(it.Container) ? "седельная сумка" : "броне-шкаф", it.Quantity);
            var sheet2 = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            foreach (var r in RetainerTracker.All)
                foreach (var it in r.Items)
                    if ((sheet2.GetRowOrDefault(it.ItemId)?.StackSize ?? 1) > 1) Add(it.ItemId, $"ретейнер {r.Name}", it.Quantity);
            var dups = places.Where(kv => kv.Value.Count > 1 && !Excluded(kv.Key)).OrderByDescending(kv => kv.Value.Count).ToList();
            Theme.SubHeader($"Одно и то же в разных местах · {dups.Count}");
            if (dups.Count == 0)
            {
                ImGui.TextColored(Theme.TextDim, "Дубликатов между сумками, седельной сумкой и ретейнерами нет.");
                return;
            }
            ImGui.TextColored(Theme.TextDim, "Собери в одно место — освободишь слоты. У ретейнера помогает кнопка «Entrust Duplicates».");
            foreach (var (id, l) in dups.Take(expanded ? 200 : 10))
                WikiView.ItemLine(id, 0, string.Join(" · ", l.Select(x => $"{x.Where} ×{x.Qty}")), "dup");
            if (dups.Count > 10 && ImGui.SmallButton(expanded ? "Свернуть##dup" : $"Показать все ({dups.Count})##dup")) expanded = !expanded;
        });

        // ── Стаки
        Theme.Panel("istack", () =>
        {
            Theme.SubHeader($"Можно сложить в один стак · {result.Stacks.Count}");
            if (result.Stacks.Count == 0)
            {
                ImGui.TextColored(Theme.TextDim, "Все стаки уже сложены.");
                return;
            }
            ImGui.TextColored(Theme.TextDim, "Кнопка сортировки в окне сумок сама соединит неполные стаки.");
            foreach (var s in result.Stacks)
                ItemRow(s.Sample, $"{s.Slots} слота, всего {s.TotalQuantity} шт. · освободит {s.SlotsFreed}");
        });

        // ── Встроенные инструменты игры
        Theme.Panel("itools", () =>
        {
            Theme.SubHeader("Массовые действия в самой игре");
            Tip(FontAwesomeIcon.Store, "Grand Company → Expert Delivery", "сдать сразу много старого зелёного и синего снаряжения за печати.");
            Tip(FontAwesomeIcon.Box, "Ретейнер → Entrust Duplicates", "переложить к ретейнеру всё, что у него уже есть.");
            Tip(FontAwesomeIcon.Hammer, "Desynthesis", "разобрать снаряжение на материалы (открывается квестом в Ul'dah после 30 ур. крафтера).");
            Tip(FontAwesomeIcon.Recycle, "Сортировка сумок", "соединяет неполные стаки и освобождает слоты.");
        });

        ImGui.EndChild();
    }

    private static void ItemRow(InvItem it, string sub)
    {
        var h = ImGui.GetTextLineHeight() * 2.1f;
        var pos = ImGui.GetCursorScreenPos();
        Theme.GameIcon(it.Icon, pos, h, 4f);
        ImGui.SetCursorScreenPos(pos + new Vector2(h + 8 * Theme.S, 0));
        ImGui.BeginGroup();
        ImGui.TextColored(Theme.Text, it.Name + (it.Hq ? " (HQ)" : "") + (it.Quantity > 1 ? $" ×{it.Quantity}" : ""));
        ImGui.TextColored(Theme.TextDim, sub);
        ImGui.EndGroup();
        ImGui.SetCursorScreenPos(new Vector2(pos.X, Math.Max(ImGui.GetCursorScreenPos().Y, pos.Y + h + 2 * Theme.S)));
        // Навёл на строку — слот подсвечивается в сумке игры
        if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(pos, pos + new Vector2(ImGui.GetContentRegionAvail().X, h)))
            InventoryHighlight.Request(it.ItemId);
    }

    private void AdviceRow(InvItem it, List<ItemAdvice> advice)
    {
        ImGui.Dummy(new Vector2(30 * Theme.S, 0));
        ImGui.SameLine();
        var first = true;
        foreach (var a in advice)
        {
            if (!first) ImGui.SameLine();
            var color = a.Kind switch
            {
                ActionKind.Market => Theme.Gold,
                ActionKind.GrandCompany => Theme.Blue,
                ActionKind.Vendor => Theme.Green,
                ActionKind.Desynth => Theme.TextDim,
                _ => Theme.Red,
            };
            Theme.InlineBadge(a.Text, color);
            first = false;
        }
        if (advice.Any(a => a.Kind == ActionKind.Market))
        {
            ImGui.SameLine();
            CopyButton(it);
        }
        ImGui.SameLine();
        ExcludeButton(it);
        ImGui.Dummy(new Vector2(0, 3 * Theme.S));
    }

    private void CopyButton(InvItem it)
    {
        var just = copiedId == it.ItemId && DateTime.Now - copiedAt < TimeSpan.FromSeconds(2);
        if (ImGui.SmallButton(just ? $"Скопировано ✓##c{it.ItemId}{it.Slot}" : $"Копировать название##c{it.ItemId}{it.Slot}{it.Container}"))
        {
            ImGui.SetClipboardText(it.Name);
            copiedId = it.ItemId;
            copiedAt = DateTime.Now;
        }
    }

    private static void ExcludeButton(InvItem it)
    {
        if (ImGui.SmallButton($"Не предлагать##x{it.ItemId}{it.Slot}{it.Container}"))
        {
            Plugin.Config.ExcludedItems.Add(it.ItemId);
            Plugin.Config.Save();
        }
        Theme.Tooltip("Убрать из подсказок (гламур, вещь для другой профессии). Вернуть можно в настройках.");
    }

    private static void Tip(FontAwesomeIcon icon, string title, string text)
    {
        Theme.FaIcon(icon, Theme.Gold);
        ImGui.SameLine();
        ImGui.TextColored(Theme.Text, title);
        ImGui.SameLine();
        ImGui.TextColored(Theme.TextDim, "— " + text);
    }
}
