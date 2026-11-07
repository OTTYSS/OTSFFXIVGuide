using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>
/// Подсказка рядом с курсором, когда наводишь на предмет в окнах игры:
/// где взять, на что меняется, цена. Только показывает.
/// </summary>
public sealed class ItemPeekWindow : Window
{
    private readonly MarketService market;
    private uint itemId;
    private double lastSeen;

    public ItemPeekWindow(MarketService market)
        : base("##FFXIVGuidePeek", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing |
                                    ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoSavedSettings)
    {
        this.market = market;
        RespectCloseHotkey = false;
        IsOpen = true;
    }

    /// <summary>Последний предмет, на который наводили в игре (для горячей клавиши «открыть»).</summary>
    public static uint LastHovered { get; private set; }

    public override bool DrawConditions()
    {
        var mode = Plugin.Config.ItemPeekMode;
        if (mode == 0 || !PlayerInfo.Ready) return false;
        var raw = Plugin.GameGui.HoveredItem;
        var id = (uint)(raw % 500_000);
        if (id != 0 && raw < 2_000_000)
        {
            itemId = id;
            LastHovered = id;
            lastSeen = ImGui.GetTime();
        }
        if (itemId == 0 || ImGui.GetTime() - lastSeen > 0.15) return false;
        if (mode == 1 && !Plugin.Keys[VirtualKey.SHIFT]) return false;
        return true;
    }

    public override void PreDraw()
    {
        Theme.Push();
        // Слева от курсора, чтобы не закрывать игровую подсказку (она обычно справа)
        var mouse = ImGui.GetMousePos();
        ImGui.SetNextWindowPos(new Vector2(mouse.X - 24, mouse.Y + 12), ImGuiCond.Always, new Vector2(1, 0));
        ImGui.SetNextWindowSizeConstraints(new Vector2(260, 0), new Vector2(380, 600));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar();
        Theme.Pop();
    }

    public override void Draw()
    {
        var (name, icon, ilvl, _) = WikiView.ItemInfo(itemId);
        var pos = ImGui.GetCursorScreenPos();
        Theme.GameIcon(icon, pos, 32 * Theme.S, 5f);
        ImGui.SetCursorScreenPos(pos + new Vector2(40 * Theme.S, 0));
        ImGui.BeginGroup();
        ImGui.TextColored(Theme.Gold, name);
        ImGui.TextColored(Theme.TextDim, ilvl > 1 ? $"iLvl {ilvl} · FFXIV Guide" : "FFXIV Guide");
        ImGui.EndGroup();
        ImGui.SetCursorScreenPos(new Vector2(pos.X, Math.Max(ImGui.GetCursorScreenPos().Y, pos.Y + 36 * Theme.S)));

        // Цена на рынке
        var row = Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId);
        var marketable = row is not null && row.Value.ItemSearchCategory.RowId != 0 && !row.Value.IsUntradable;
        if (marketable && MarketService.CurrentRegion(false) is { } dc)
        {
            market.Request(dc, new[] { itemId });
            var p = market.Get(dc, itemId);
            ImGui.TextColored(Theme.Green, p is null ? (market.IsLoading(dc, itemId) ? "Рынок: загрузка…" : "Рынок: нет лотов") : $"Рынок: от {p.Price:N0} гил ({p.World})");
        }

        var w = WikiService.Instance.Item(itemId);
        if (w is null)
        {
            ImGui.TextColored(Theme.Gray, WikiService.Instance.Error("item", itemId) ?? "Загружаю источники…");
            return;
        }

        var lines = 0;
        void Line(FontAwesomeIcon ic, Vector4 c, string text)
        {
            if (lines >= 6) return;
            Theme.FaIcon(ic, c);
            ImGui.SameLine();
            ImGui.TextColored(Theme.Text, text);
            lines++;
        }

        if (w.Vendors.Count > 0) Line(FontAwesomeIcon.Home, Theme.Gold, $"Торговец: {w.Vendors[0].Name}" + (w.VendorPrice > 0 ? $" · {w.VendorPrice:N0} гил" : ""));
        foreach (var t in w.TradeShops.Take(2))
            Line(FontAwesomeIcon.Gem, Theme.Gold, $"Обмен: {string.Join(" + ", t.Pay.Select(p => $"{p.Amount}× {WikiView.ItemInfo(p.Item).Name}"))}");
        if (w.Crafts.Count > 0) Line(FontAwesomeIcon.Hammer, Theme.Gold, $"Крафт: {string.Join(", ", w.Crafts.Select(c => $"{Plugin.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(c.Job)?.Abbreviation.ExtractText()} {c.Level}"))}");
        if (w.Nodes.Count > 0) Line(FontAwesomeIcon.Leaf, Theme.Green, $"Сбор: {w.Nodes[0].Name} ({WikiService.ZoneName(w.Nodes[0].Zone)})");
        if (w.FishSpots.Count > 0) Line(FontAwesomeIcon.Fish, Theme.Blue, $"Рыбалка: {w.FishSpots[0].Spot}");
        if (w.Instances.Count > 0) Line(FontAwesomeIcon.Dungeon, Theme.Red, $"Данж: {w.Instances[0].Name}" + (w.Instances.Count > 1 ? $" и ещё {w.Instances.Count - 1}" : ""));
        if (w.Drops.Count > 0) Line(FontAwesomeIcon.Skull, Theme.Red, $"Дроп: {w.Drops[0].Name}" + (w.Drops.Count > 1 ? $" и ещё {w.Drops.Count - 1}" : ""));
        if (w.Treasure.Count > 0) Line(FontAwesomeIcon.Box, Theme.GoldDim, $"Из: {WikiView.ItemInfo(w.Treasure[0]).Name}");
        if (w.DesynthFrom.Count > 0) Line(FontAwesomeIcon.Recycle, Theme.GoldDim, $"Разбором из {w.DesynthFrom.Count} вещей");

        if (w.TradeCurrency.Count > 0 || w.IngredientOf.Count > 0)
        {
            ImGui.Separator();
            if (w.TradeCurrency.Count > 0) ImGui.TextColored(Theme.Blue, $"Меняется на {w.TradeCurrency.Count} предметов");
            if (w.IngredientOf.Count > 0) ImGui.TextColored(Theme.Blue, $"Нужен в {w.IngredientOf.Count} рецептах");
        }
        if (!w.HasAnySource && w.TradeCurrency.Count == 0 && w.IngredientOf.Count == 0)
            ImGui.TextColored(Theme.Gray, "Нет данных об источниках");
        ImGui.TextColored(Theme.Gray, "Полная карточка: /fguide peek");
    }
}
