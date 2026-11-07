using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>
/// Общие кусочки вики: ссылки на предметы, мобов и данжи, сетки лута, статус загрузки.
/// Любое имя кликабельно и ведёт в свою карточку.
/// </summary>
internal static class WikiView
{
    public static GuideData? Data;

    private static readonly Dictionary<uint, (string Name, uint Icon, uint Ilvl, byte Rarity)> itemCache = new();

    public static (string Name, uint Icon, uint Ilvl, byte Rarity) ItemInfo(uint id)
    {
        if (itemCache.TryGetValue(id, out var v)) return v;
        var row = Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(id);
        v = row is null ? ($"Предмет #{id}", 0u, 0u, (byte)1) : (row.Value.Name.ExtractText(), (uint)row.Value.Icon, row.Value.LevelItem.RowId, row.Value.Rarity);
        itemCache[id] = v;
        return v;
    }

    /// <summary>Статус загрузки: true — данные есть, рисуй. Иначе показывает «грузится» или ошибку с повтором.</summary>
    public static bool Status(object? doc, string kind, object id)
    {
        if (doc is not null) return true;
        var err = WikiService.Instance.Error(kind, id);
        if (err is not null)
        {
            Theme.FaIcon(FontAwesomeIcon.ExclamationTriangle, Theme.Gray);
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextDim, err);
            if (!err.StartsWith("Нет данных"))
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"Повторить##{kind}{id}")) WikiService.Instance.Retry(kind, id);
            }
            return false;
        }
        var dots = new string('.', 1 + (int)(ImGui.GetTime() * 3) % 3);
        Theme.FaIcon(FontAwesomeIcon.Spinner, Theme.GoldDim);
        ImGui.SameLine();
        ImGui.TextColored(Theme.TextDim, "Загружаю с Garland Tools" + dots);
        return false;
    }

    // ───────────── Улучшение экипировки ─────────────

    private static readonly Dictionary<GearSlot, int[]> SlotIndexes = new()
    {
        [GearSlot.MainHand] = new[] { 0 }, [GearSlot.OffHand] = new[] { 1 }, [GearSlot.Head] = new[] { 2 },
        [GearSlot.Body] = new[] { 3 }, [GearSlot.Hands] = new[] { 4 }, [GearSlot.Legs] = new[] { 6 },
        [GearSlot.Feet] = new[] { 7 }, [GearSlot.Ears] = new[] { 8 }, [GearSlot.Neck] = new[] { 9 },
        [GearSlot.Wrists] = new[] { 10 }, [GearSlot.Ring] = new[] { 11, 12 },
    };

    /// <summary>Подойдёт ли вещь текущей профессии и выше ли она по iLvl, чем надетая.</summary>
    public static bool IsUpgrade(uint itemId)
    {
        if (!PlayerInfo.Ready || Data is null) return false;
        var row = Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId);
        if (row is null || row.Value.EquipSlotCategory.RowId == 0) return false;
        if (row.Value.EquipSlotCategory.ValueNullable is not { } cat || GuideData.SlotOf(cat) is not { } slot) return false;
        var job = Plugin.PlayerState.ClassJob.RowId;
        if (!Data.JobInCategory(row.Value.ClassJobCategory.RowId, GuideData.JobAbbrEnglish(job))) return false;
        var items = Plugin.Data.GetExcelSheet<Item>();
        var worn = SlotIndexes[slot]
            .Select(i => PlayerInfo.EquippedItem(i))
            .Select(id => id == 0 ? 0u : items.GetRowOrDefault(id)?.LevelItem.RowId ?? 0u)
            .DefaultIfEmpty(0u).Min();
        return row.Value.LevelItem.RowId > worn;
    }

    // ───────────── Ссылки ─────────────

    /// <summary>Строка-ссылка на предмет: иконка, название, количество.</summary>
    public static void ItemLine(uint id, int amount = 0, string? extra = null, string uid = "")
    {
        var (name, icon, ilvl, _) = ItemInfo(id);
        var h = 26 * Theme.S;
        var pos = ImGui.GetCursorScreenPos();
        var label = (amount > 1 ? $"{amount}× " : "") + NameTranslations.Show(name) + (extra is null ? "" : $"  · {extra}");
        var clicked = ImGui.InvisibleButton($"##il{id}{uid}{amount}", new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), h));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        if (hovered) dl.AddRectFilled(pos, pos + new Vector2(ImGui.GetItemRectSize().X, h), Theme.U32(Theme.CardHover), 5f * Theme.S);
        Theme.GameIcon(icon, pos + new Vector2(2 * Theme.S), h - 4 * Theme.S);
        dl.AddText(pos + new Vector2(h + 6 * Theme.S, (h - ImGui.GetTextLineHeight()) / 2), Theme.U32(hovered ? Theme.Gold : Theme.Text), label);
        if (IsUpgrade(id))
        {
            var bw = Theme.BadgeSize("улучшение").X;
            Theme.Badge(new Vector2(pos.X + ImGui.GetItemRectSize().X - bw - 4 * Theme.S, pos.Y + 2 * Theme.S), "улучшение", Theme.Green);
        }
        if (hovered) InventoryHighlight.Request(id);
        if (hovered) ImGui.SetTooltip(ilvl > 1 ? $"{name}\niLvl {ilvl}\nНажми — открыть карточку" : $"{name}\nНажми — открыть карточку");
        if (clicked) WikiNav.Open(WikiKind.Item, id, name);
    }

    /// <summary>Сетка иконок лута с подсказками; зелёная рамка — улучшение для текущей профессии.</summary>
    public static void ItemGrid(IEnumerable<uint> ids, string uid)
    {
        var size = 40 * Theme.S;
        var gap = 6 * Theme.S;
        var right = Theme.PanelRight > 0 ? Theme.PanelRight : ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var startX = ImGui.GetCursorScreenPos().X;
        var x = startX;
        var y = ImGui.GetCursorScreenPos().Y;
        var dl = ImGui.GetWindowDrawList();
        var any = false;
        foreach (var id in ids)
        {
            if (any && x + size > right) { x = startX; y += size + gap; }
            any = true;
            var pos = new Vector2(x, y);
            ImGui.SetCursorScreenPos(pos);
            var clicked = ImGui.InvisibleButton($"##ig{uid}{id}", new Vector2(size));
            var hovered = ImGui.IsItemHovered();
            var (name, icon, ilvl, _) = ItemInfo(id);
            dl.AddRectFilled(pos, pos + new Vector2(size), Theme.U32(Theme.CardBg), 6f * Theme.S);
            Theme.GameIcon(icon, pos + new Vector2(2 * Theme.S), size - 4 * Theme.S, 5f);
            var up = IsUpgrade(id);
            if (up || hovered)
                dl.AddRect(pos, pos + new Vector2(size), Theme.U32(up ? Theme.Green : Theme.Gold), 6f * Theme.S, ImDrawFlags.None, 2f * Theme.S);
            if (PlayerInfo.ItemCount(id) > 0)
                dl.AddCircleFilled(pos + new Vector2(size - 6 * Theme.S, 6 * Theme.S), 4 * Theme.S, Theme.U32(Theme.Blue));
            if (hovered) InventoryHighlight.Request(id);
            if (hovered)
                ImGui.SetTooltip(NameTranslations.Show(name) + (ilvl > 1 ? $"\niLvl {ilvl}" : "") + (up ? "\nУлучшение для текущей профессии" : "") +
                                 (PlayerInfo.ItemCount(id) > 0 ? "\nУже есть у тебя" : "") + "\nНажми — открыть карточку");
            if (clicked) WikiNav.Open(WikiKind.Item, id, name);
            x += size + gap;
        }
        if (!any) return;
        ImGui.SetCursorScreenPos(new Vector2(startX, y + size + gap));
        ImGui.Dummy(Vector2.Zero);
    }

    /// <summary>Строка-ссылка на моба.</summary>
    public static void MobLine(WikiMobRow m, string uid = "")
    {
        var zone = WikiService.ZoneName(m.Zone);
        var sub = (m.Level.Length > 0 ? $"ур. {m.Level}" : "") + (zone.Length > 0 ? $" · {zone}" : "");
        var h = 26 * Theme.S;
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##ml{m.Id}{uid}", new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), h));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        if (hovered) dl.AddRectFilled(pos, pos + new Vector2(ImGui.GetItemRectSize().X, h), Theme.U32(Theme.CardHover), 5f * Theme.S);
        ImGui.PushFont(UiBuilder.IconFont);
        dl.AddText(pos + new Vector2(6 * Theme.S, (h - ImGui.GetTextLineHeight()) / 2), Theme.U32(Theme.Red), FontAwesomeIcon.Skull.ToIconString());
        ImGui.PopFont();
        var ty = (h - ImGui.GetTextLineHeight()) / 2;
        var mname = NameTranslations.Show(m.Name);
        dl.AddText(pos + new Vector2(28 * Theme.S, ty), Theme.U32(hovered ? Theme.Gold : Theme.Text), mname);
        dl.AddText(pos + new Vector2(28 * Theme.S + ImGui.CalcTextSize(mname).X + 10 * Theme.S, ty), Theme.U32(Theme.TextDim), sub);
        if (hovered) ImGui.SetTooltip("Нажми — открыть карточку моба");
        if (clicked) WikiNav.Open(WikiKind.Mob, m.Id, m.Name);
    }

    /// <summary>Строка-ссылка на данж/бой по InstanceContent id.</summary>
    public static void DutyLine(uint instanceId, string name, string extra, string uid = "")
    {
        var duty = Data?.Duties.FirstOrDefault(d => d.InstanceContentId == instanceId);
        var h = 26 * Theme.S;
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##dl{instanceId}{uid}", new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), h));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        if (hovered) dl.AddRectFilled(pos, pos + new Vector2(ImGui.GetItemRectSize().X, h), Theme.U32(Theme.CardHover), 5f * Theme.S);
        if (duty is not null) Theme.GameIcon(duty.Icon, pos + new Vector2(2 * Theme.S), h - 4 * Theme.S);
        var title = NameTranslations.Show(duty?.Name ?? name);
        var ty = (h - ImGui.GetTextLineHeight()) / 2;
        dl.AddText(pos + new Vector2(h + 6 * Theme.S, ty), Theme.U32(hovered ? Theme.Gold : Theme.Text), title);
        var sub = duty is not null ? $"{duty.Type} · ур. {duty.Level}" : extra;
        dl.AddText(pos + new Vector2(h + 16 * Theme.S + ImGui.CalcTextSize(title).X, ty), Theme.U32(Theme.TextDim), sub);
        if (hovered) ImGui.SetTooltip(duty is not null ? "Нажми — открыть данж: лут, механики, как открыть" : "Этого контента нет в списке данжей");
        if (clicked && duty is not null) WikiNav.Open(WikiKind.Duty, duty.CfcId, duty.Name);
    }

    /// <summary>NPC с кнопкой флага на карте.</summary>
    public static void NpcLine(WikiNpc n, string uid)
    {
        Theme.FaIcon(FontAwesomeIcon.User, Theme.GoldDim);
        ImGui.SameLine();
        var zone = WikiService.ZoneName(n.Zone);
        ImGui.TextColored(Theme.Text, NameTranslations.Show(n.Name));
        if (zone.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextDim, n.HasCoords ? $"{zone} ({n.X:0.0}, {n.Y:0.0})" : zone);
        }
        if (n.HasCoords && n.Zone != 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Флаг##npc{n.Id}{uid}")) WikiService.Flag(n.Zone, n.X, n.Y);
        }
    }
}
