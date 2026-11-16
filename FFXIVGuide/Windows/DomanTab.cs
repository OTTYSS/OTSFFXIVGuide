using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

/// <summary>Doman Enclave: недельные пожертвования на восстановление — сколько сдано, лимит и что из сумок выгоднее сдать.</summary>
internal sealed class DomanTab
{
    private readonly MarketService market;
    private DateTime nextScan = DateTime.MinValue;
    private List<InvItem> items = new();

    public DomanTab(MarketService market) => this.market = market;

    public unsafe void Draw()
    {
        Theme.Header("Doman Enclave", "Еженедельные пожертвования на восстановление Doman Enclave: сколько сдано, лимит и что выгоднее отдать");
        if (!PlayerInfo.Ready) { Theme.NeedLogin("Зайди в игру персонажем: данные Doman Enclave берутся из игры."); return; }
        var de = DomanEnclaveManager.Instance();
        Theme.Panel("dehow", () =>
            ImGui.TextColored(Theme.TextDim, "Открывается после сюжета Stormblood (квест «The Doman Enclave» / «Restoring the Enclave»). Пожертвования принимает Doman Enclave Reconstruction Box: " +
                                             "предметы засчитываются по цене продажи NPC, умноженной на коэффициент, — так можно выгоднее, чем продать торговцу. Лимит обновляется каждую неделю."));
        if (de == null || !de->IsLoaded)
        {
            Theme.EmptyState(FontAwesomeIcon.Home, "Данные не загружены", "Зайди в Doman Enclave один раз за сессию — плагин увидит, сколько уже сдано на этой неделе.");
        }
        else
        {
            var st = de->State;
            Theme.Panel("destate", () =>
            {
                Theme.Progress($"Сдано на этой неделе: {st.Donated:N0} / {st.Allowance:N0} гил", st.Donated, Math.Max(1, (int)st.Allowance), st.Donated >= st.Allowance ? Theme.Green : Theme.Gold);
                ImGui.TextColored(Theme.Text, $"Этап восстановления: {st.CurrentMilestone} · коэффициент: ×{st.Factor}");
                if (!st.IsAcceptingDonations) ImGui.TextColored(Theme.Yellow, "Сейчас пожертвования не принимаются (лимит недели исчерпан или ещё не открыто).");
                else ImGui.TextColored(Theme.Green, $"Можно сдать ещё на {Math.Max(0, st.Allowance - st.Donated):N0} гил.");
            });
        }

        if (DateTime.Now >= nextScan)
        {
            nextScan = DateTime.Now.AddSeconds(3);
            var scan = InventoryAnalyzer.Scan();
            items = scan is null ? new() : scan.Items
                .Where(i => i.Row.PriceLow > 0 && !i.Row.IsIndisposable && !scan.GearsetItems.Contains(i.ItemId) && i.Container.ToString().StartsWith("Inventory"))
                .OrderByDescending(i => (long)i.Row.PriceLow * i.Quantity).Take(60).ToList();
            var region = MarketService.CurrentRegion(true);
            if (region is not null) market.Request(region, items.Where(i => i.Marketable).Select(i => i.ItemId));
        }

        var factor = de != null && de->IsLoaded && de->State.Factor > 0 ? de->State.Factor : 2;
        var reg = MarketService.CurrentRegion(true);
        Theme.Panel("deitems", () =>
        {
            Theme.SubHeader("Что выгодно сдать из сумок");
            ImGui.TextColored(Theme.TextDim, $"Зачёт = цена у NPC × {factor}. Вещи, которые на рынке стоят дороже зачёта, лучше продать — они помечены.");
            if (!ImGui.BeginTable("##det", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp)) return;
            ImGui.TableSetupColumn("Предмет", ImGuiTableColumnFlags.None, 2.4f);
            ImGui.TableSetupColumn("Кол-во", ImGuiTableColumnFlags.None, 0.6f);
            ImGui.TableSetupColumn("Зачёт", ImGuiTableColumnFlags.None, 1f);
            ImGui.TableSetupColumn("Рынок", ImGuiTableColumnFlags.None, 1.2f);
            ImGui.TableHeadersRow();
            foreach (var it in items)
            {
                var credit = (long)it.Row.PriceLow * factor * it.Quantity;
                var price = it.Marketable && reg is not null ? market.Get(reg, it.ItemId) : null;
                var marketValue = price is null ? 0 : price.Price * it.Quantity;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(it.Name);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(it.Quantity.ToString());
                ImGui.TableNextColumn();
                ImGui.TextColored(Theme.Gold, $"{credit:N0}");
                ImGui.TableNextColumn();
                if (marketValue > credit) ImGui.TextColored(Theme.Yellow, $"{marketValue:N0} — лучше продать");
                else ImGui.TextColored(Theme.Gray, marketValue > 0 ? $"{marketValue:N0}" : "—");
            }
            ImGui.EndTable();
        });
    }
}
