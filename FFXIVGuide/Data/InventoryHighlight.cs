using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

/// <summary>
/// Подсветка выбранного в справочнике предмета прямо в окнах игры: сумки, броне-шкаф,
/// седельная сумка, ретейнер. Только рисует рамку поверх — ничего не нажимает и не двигает.
/// </summary>
public static unsafe class InventoryHighlight
{
    private static uint itemId;
    private static uint iconId;
    private static double lastRequest;

    /// <summary>Окна игры, в которых ищем слоты с предметами.</summary>
    private static readonly string[] Addons =
    {
        "InventoryGrid0E", "InventoryGrid1E", "InventoryGrid2E", "InventoryGrid3E",
        "InventoryGrid0", "InventoryGrid1", "InventoryGrid",
        "ArmouryBoard", "InventoryBuddy", "InventoryBuddy2",
        "RetainerGrid0", "RetainerGrid1", "RetainerGrid2", "RetainerGrid3", "RetainerGrid4", "RetainerGrid",
        "InventoryEventGrid0", "InventoryEventGrid1", "InventoryEventGrid2",
    };

    /// <summary>Вызывать каждый кадр, пока карточка предмета открыта. Подсветка гаснет сама через секунду.</summary>
    public static void Request(uint id)
    {
        if (!Plugin.Config.HighlightInGame || id == 0) return;
        if (id != itemId)
        {
            itemId = id;
            iconId = Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(id)?.Icon ?? 0;
        }
        lastRequest = ImGui.GetTime();
    }

    public static bool Active => itemId != 0 && iconId != 0 && ImGui.GetTime() - lastRequest < 1.0;

    /// <summary>Рисует рамки (подписано на UiBuilder.Draw).</summary>
    public static void Draw()
    {
        if (!Active || !PlayerInfo.Ready) return;
        try
        {
            var dl = ImGui.GetForegroundDrawList();
            var offset = ImGui.GetMainViewport().Pos;
            var pulse = 0.55f + 0.45f * MathF.Sin((float)ImGui.GetTime() * 5f);
            var color = ImGui.GetColorU32(new Vector4(1f, 0.82f, 0.30f, pulse));
            var glow = ImGui.GetColorU32(new Vector4(1f, 0.82f, 0.30f, 0.18f * pulse));

            foreach (var name in Addons)
            {
                var ptr = Plugin.GameGui.GetAddonByName(name, 1);
                if (ptr.IsNull) continue;
                var unit = (AtkUnitBase*)ptr.Address;
                if (unit == null || !unit->IsVisible || unit->UldManager.NodeList == null) continue;

                for (var i = 0; i < unit->UldManager.NodeListCount; i++)
                {
                    var node = unit->UldManager.NodeList[i];
                    if (node == null || !node->IsVisible() || (ushort)node->Type < 1000) continue;
                    var comp = ((AtkComponentNode*)node)->Component;
                    if (comp == null) continue;
                    var info = (AtkUldComponentInfo*)comp->UldManager.Objects;
                    if (info == null || info->ComponentType != ComponentType.DragDrop) continue;
                    var icon = ((AtkComponentDragDrop*)comp)->AtkComponentIcon;
                    if (icon == null) continue;
                    var shown = (uint)(icon->IconId % 1_000_000);
                    if (shown != iconId) continue;

                    var scale = unit->Scale;
                    var pos = offset + new Vector2(node->ScreenX, node->ScreenY);
                    var size = new Vector2(node->Width, node->Height) * scale;
                    var pad = 2f * scale;
                    dl.AddRectFilled(pos - new Vector2(pad), pos + size + new Vector2(pad), glow, 6f * scale);
                    dl.AddRect(pos - new Vector2(pad), pos + size + new Vector2(pad), color, 6f * scale, ImDrawFlags.None, 3f * scale);
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Подсветка предмета: ошибка, выключаю до перезапуска");
            itemId = 0;
        }
    }
}
