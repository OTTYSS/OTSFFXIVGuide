using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

/// <summary>Лоты ретейнеров на рынке: сверка цен с Universalis — кого перебили и на сколько. Цены меняешь сам.</summary>
internal sealed class RetainerMarketTab
{
    private readonly MarketService market;
    private bool onlyUndercut;

    public RetainerMarketTab(MarketService market) => this.market = market;

    public void Draw()
    {
        Theme.Header("Лоты ретейнеров", "Что выставлено на рынке, по какой цене и кого перебили — по данным Universalis");
        var retainers = RetainerTracker.All.Where(r => r.Market.Count > 0 || r.MarketUpdated != DateTime.MinValue).ToList();
        if (retainers.Count == 0)
        {
            Theme.EmptyState(FontAwesomeIcon.Store, "Лоты ещё не известны",
                "Подойди к колокольчику ретейнеров и открой каждого ретейнера (и его список продаж) — плагин запомнит, что и почём выставлено.");
            return;
        }
        var world = MarketService.CurrentRegion(true);
        if (world is not null) market.Request(world, retainers.SelectMany(r => r.Market).Select(m => m.ItemId));

        var total = retainers.Sum(r => r.Market.Sum(m => (long)m.Price * m.Quantity));
        var undercut = 0;
        foreach (var r in retainers)
            foreach (var m in r.Market)
                if (world is not null && market.Get(world, m.ItemId) is { } p && (ulong)p.Price < m.Price) undercut++;

        Theme.Panel("rmsum", () =>
        {
            ImGui.TextColored(Theme.Text, $"Лотов: {retainers.Sum(r => r.Market.Count)} · на сумму {total:N0} гил");
            ImGui.TextColored(undercut > 0 ? Theme.Yellow : Theme.Green, undercut > 0 ? $"Перебили по цене: {undercut}" : "Тебя никто не перебил ✓");
            ImGui.TextColored(Theme.Gray, $"Цены рынка — самый дешёвый лот на мире {world ?? "?"} (Universalis, может отставать на несколько минут). Если самый дешёвый — твой, он показан как «твоя цена самая низкая».");
            ImGui.Checkbox("Показывать только перебитые", ref onlyUndercut);
        });

        var items = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
        foreach (var r in retainers)
        {
            Theme.Panel($"rm{r.Name}", () =>
            {
                var expire = r.MarketExpire > 0 ? DateTimeOffset.FromUnixTimeSeconds(r.MarketExpire).LocalDateTime : (DateTime?)null;
                ImGui.TextColored(Theme.Gold, r.Name);
                ImGui.SameLine();
                ImGui.TextColored(Theme.TextDim, $"· лотов {r.Market.Count} · данные от {r.MarketUpdated:dd.MM HH:mm}" + (expire is { } e ? $" · выставлено до {e:dd.MM HH:mm}" : ""));
                if (r.Market.Count == 0) { ImGui.TextColored(Theme.Gray, "Ничего не выставлено."); return; }
                if (!ImGui.BeginTable($"##rmt{r.Name}", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp)) return;
                ImGui.TableSetupColumn("Предмет", ImGuiTableColumnFlags.None, 2.2f);
                ImGui.TableSetupColumn("Кол-во", ImGuiTableColumnFlags.None, 0.5f);
                ImGui.TableSetupColumn("Твоя цена", ImGuiTableColumnFlags.None, 0.9f);
                ImGui.TableSetupColumn("Дешевле всего", ImGuiTableColumnFlags.None, 1.2f);
                ImGui.TableSetupColumn("Итог", ImGuiTableColumnFlags.None, 1.4f);
                ImGui.TableHeadersRow();
                foreach (var m in r.Market)
                {
                    var p = world is null ? null : market.Get(world, m.ItemId);
                    var under = p is not null && (ulong)p.Price < m.Price;
                    if (onlyUndercut && !under) continue;
                    var name = items.GetRowOrDefault(m.ItemId)?.Name.ExtractText() ?? $"#{m.ItemId}";
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    if (ImGui.Selectable($"{name}{(m.Hq ? " (HQ)" : "")}##rmi{r.Name}{m.ItemId}{m.Price}")) WikiNav.Open(WikiKind.Item, m.ItemId, name);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(m.Quantity.ToString());
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted($"{m.Price:N0}");
                    ImGui.TableNextColumn();
                    if (p is null) ImGui.TextColored(Theme.Gray, world is not null && market.IsLoading(world, m.ItemId) ? "…" : "нет данных");
                    else ImGui.TextColored(Theme.TextDim, $"{p.Price:N0}{(p.Hq ? " HQ" : "")}");
                    ImGui.TableNextColumn();
                    if (p is null) ImGui.TextColored(Theme.Gray, "—");
                    else if (under)
                    {
                        var diff = m.Price - (ulong)p.Price;
                        ImGui.TextColored(Theme.Yellow, $"перебили на {diff:N0}" + (m.Hq && !p.Hq ? " (дешевле NQ)" : ""));
                    }
                    else ImGui.TextColored(Theme.Green, (ulong)p.Price == m.Price ? "твоя цена самая низкая" : "дешевле всех ✓");
                }
                ImGui.EndTable();
            });
        }
        ImGui.TextColored(Theme.Gray, "Плагин только показывает — цены меняешь сам у колокольчика.");
    }
}
