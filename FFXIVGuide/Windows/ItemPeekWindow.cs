using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>
/// Карточка предмета при наведении в окнах игры. С зажатым Shift (по настройке) прячет игровую подсказку
/// и показывает вместо неё полную карточку: характеристики, ремонт, цены, сколько есть, откуда взять и где используется.
/// Только показывает — ничего не нажимает.
/// </summary>
public sealed class ItemPeekWindow : Window
{
    private readonly MarketService market;
    private uint itemId;
    private bool hq;
    private double lastSeen;
    private bool replacing;
    private bool hiddenByUs;
    private Vector2? gamePos;
    private Instance? inst;

    /// <summary>Конкретный экземпляр предмета под курсором (из сумки, шкафа, ретейнера): материя, прочность, связь, гламур, краска.</summary>
    private sealed record Instance(bool Hq, float Condition, float Spiritbond, List<(string Name, string Stat, int Value)> Materia,
        uint Glamour, List<string> Dyes, bool Crafted);

    private unsafe Instance? ReadInstance()
    {
        var ag = AgentItemDetail.Instance();
        if (ag == null || ag->DetailKind != FFXIVClientStructs.FFXIV.Client.Enums.DetailKind.InventoryItem) return null;
        if (ag->ItemId % 500_000 != itemId) return null;
        var im = InventoryManager.Instance();
        if (im == null) return null;
        var slot = im->GetInventorySlot((InventoryType)ag->TypeOrId, (int)ag->Index);
        if (slot == null || slot->GetBaseItemId() != itemId) return null;
        var mats = new List<(string, string, int)>();
        var msheet = Plugin.Data.GetExcelSheet<Materia>();
        for (var i = 0; i < slot->Materia.Length; i++)
        {
            var mid = slot->Materia[i];
            if (mid == 0) continue;
            var grade = slot->MateriaGrades[i];
            if (msheet.GetRowOrDefault(mid) is not { } m || grade >= m.Item.Count) continue;
            mats.Add((m.Item[grade].ValueNullable?.Name.ExtractText() ?? "?", m.BaseParam.ValueNullable?.Name.ExtractText() ?? "", m.Value[grade]));
        }
        var stains = Plugin.Data.GetExcelSheet<Stain>();
        var dyes = new List<string>();
        for (var i = 0; i < slot->Stains.Length; i++)
            if (slot->Stains[i] != 0 && stains.GetRowOrDefault(slot->Stains[i]) is { } st) dyes.Add(st.Name.ExtractText());
        return new Instance(slot->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality), slot->Condition / 300f, slot->SpiritbondOrCollectability / 100f,
            mats, slot->GlamourId, dyes, slot->CrafterContentId != 0);
    }

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
        var show = false;
        if (mode != 0 && PlayerInfo.Ready)
        {
            var raw = Plugin.GameGui.HoveredItem;
            var id = (uint)(raw % 500_000);
            if (id != 0 && raw < 2_000_000)
            {
                itemId = id;
                hq = raw >= 1_000_000;
                LastHovered = id;
                lastSeen = ImGui.GetTime();
            }
            var shift = Plugin.Keys[VirtualKey.SHIFT];
            show = itemId != 0 && ImGui.GetTime() - lastSeen <= 0.15 && (mode == 2 || shift);
            // Заменяем игровую подсказку всегда, когда показываем свою карточку (режим «Всегда» — даже без Shift)
            replacing = show && Plugin.Config.ItemPeekReplace;
            if (show) inst = ReadInstance();
        }
        else replacing = false;
        UpdateGameTooltip();
        return show;
    }

    /// <summary>Прячем игровую подсказку, пока показываем свою вместо неё; потом возвращаем.</summary>
    private unsafe void UpdateGameTooltip()
    {
        var unit = Plugin.GameGui.GetAddonByName<AtkUnitBase>("ItemDetail");
        if (unit == null) { hiddenByUs = false; gamePos = null; return; }
        if (replacing)
        {
            if (unit->IsVisible)
            {
                gamePos = new Vector2(unit->X, unit->Y);
                unit->IsVisible = false;
                hiddenByUs = true;
            }
        }
        else if (hiddenByUs)
        {
            hiddenByUs = false;
            gamePos = null;
            // Возвращаем, только если курсор всё ещё на предмете
            if (Plugin.GameGui.HoveredItem != 0) unit->IsVisible = true;
        }
    }

    public override void PreDraw()
    {
        Theme.Push();
        var mouse = ImGui.GetMousePos();
        if (replacing && gamePos is { } gp)
            ImGui.SetNextWindowPos(ImGui.GetMainViewport().Pos + gp, ImGuiCond.Always);
        else
            // Слева от курсора, чтобы не закрывать игровую подсказку (она обычно справа)
            ImGui.SetNextWindowPos(new Vector2(mouse.X - 24, mouse.Y + 12), ImGuiCond.Always, new Vector2(1, 0));
        ImGui.SetNextWindowSizeConstraints(new Vector2(replacing ? 340 : 260, 0), new Vector2(replacing ? 460 : 380, 900));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar();
        Theme.Pop();
    }

    private static Vector4 RarityColor(byte r) => r switch
    {
        2 => Theme.Green,
        3 => Theme.Blue,
        4 => new Vector4(0.75f, 0.55f, 1f, 1f),
        7 => new Vector4(0.95f, 0.55f, 0.75f, 1f),
        _ => Theme.Text,
    };

    public override void Draw()
    {
        var row = Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId);
        var (name, icon, ilvl, _) = WikiView.ItemInfo(itemId);

        // Заголовок
        var pos = ImGui.GetCursorScreenPos();
        var big = replacing ? 44 * Theme.S : 32 * Theme.S;
        Theme.GameIcon(icon, pos, big, 5f);
        ImGui.SetCursorScreenPos(pos + new Vector2(big + 8 * Theme.S, 0));
        ImGui.BeginGroup();
        ImGui.TextColored(row is { } r0 ? RarityColor(r0.Rarity) : Theme.Gold, name + (hq || inst?.Hq == true ? " (HQ)" : ""));
        if (row is { } r1)
        {
            var cat = r1.ItemUICategory.ValueNullable?.Name.ExtractText() ?? "";
            var flags = new List<string>();
            if (r1.IsUnique) flags.Add("Unique");
            if (r1.IsUntradable) flags.Add("Untradable");
            if (r1.AlwaysCollectable) flags.Add("Collectable");
            ImGui.TextColored(Theme.TextDim, cat + (flags.Count > 0 ? " · " + string.Join(" · ", flags) : ""));
        }
        ImGui.EndGroup();
        ImGui.SetCursorScreenPos(new Vector2(pos.X, Math.Max(ImGui.GetCursorScreenPos().Y, pos.Y + big + 4 * Theme.S)));

        if (replacing && row is { } it) DrawStats(it, ilvl, inst);
        else if (inst is { Materia.Count: > 0 } mi) ImGui.TextColored(Theme.Blue, "Материя: " + string.Join(", ", mi.Materia.Select(m => $"{m.Stat} +{m.Value}")));

        // Сколько есть и где
        var bags = PlayerInfo.ItemCount(itemId);
        var (ret, where) = RetainerTracker.CountOf(itemId);
        if (bags > 0 || ret > 0)
            ImGui.TextColored(Theme.Green, $"У тебя: {bags + ret}" + (bags > 0 ? $" · в сумках {bags}" : "") + (ret > 0 ? $" · у ретейнеров: {where}" : ""));
        if (row is { } ra && ra.ItemAction.RowId != 0 && Plugin.Unlocks.IsItemUnlocked(ra))
            ImGui.TextColored(Theme.Green, "Уже изучено / есть в коллекции ✓");

        // Цены
        var marketable = row is not null && row.Value.ItemSearchCategory.RowId != 0 && !row.Value.IsUntradable;
        if (row is { } rp && rp.PriceLow > 0) ImGui.TextColored(Theme.TextDim, $"Продать NPC: {rp.PriceLow:N0} гил");
        if (marketable && MarketService.CurrentRegion(false) is { } dc)
        {
            market.Request(dc, new[] { itemId });
            var p = market.Get(dc, itemId);
            ImGui.TextColored(Theme.Green, p is null ? (market.IsLoading(dc, itemId) ? "Рынок: загрузка…" : "Рынок: нет лотов") : $"Рынок: от {p.Price:N0} гил ({p.World})");
        }
        else if (row is not null && !marketable) ImGui.TextColored(Theme.Gray, "На рынке не продаётся");

        DrawSources(row);

        if (replacing && row is { } rd && !rd.Description.IsEmpty)
        {
            ImGui.Separator();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 420 * Theme.S);
            ImGui.TextColored(Theme.TextDim, rd.Description.ExtractText());
            ImGui.PopTextWrapPos();
        }
        ImGui.TextColored(Theme.Gray, replacing ? "Карточка FFXIV Guide вместо игровой подсказки · /fguide peek" : "Полная карточка: /fguide peek");
    }

    private static void DrawStats(Item it, uint ilvl, Instance? inst)
    {
        var lines = new List<string>();
        if (ilvl > 1) lines.Add($"iLvl {ilvl}" + (it.LevelEquip > 1 ? $" · ур. {it.LevelEquip}" : ""));
        var jobs = it.ClassJobCategory.ValueNullable?.Name.ExtractText();
        if (!string.IsNullOrEmpty(jobs) && it.EquipSlotCategory.RowId != 0) lines.Add(jobs);
        foreach (var l in lines) ImGui.TextColored(Theme.Gold, l);

        if (it.DamagePhys > 0 || it.DamageMag > 0)
            ImGui.TextColored(Theme.Text, $"Физ. урон {it.DamagePhys} · Маг. урон {it.DamageMag}" + (it.Delayms > 0 ? $" · задержка {it.Delayms / 1000f:0.00}" : ""));
        if (it.DefensePhys > 0 || it.DefenseMag > 0)
            ImGui.TextColored(Theme.Text, $"Защита {it.DefensePhys} · Маг. защита {it.DefenseMag}");

        // Бонусы к характеристикам (+ HQ-бонус, если наведён HQ-предмет — точный экземпляр игра нам не сообщает)
        var bp = Plugin.Data.GetExcelSheet<BaseParam>();
        var stats = new List<(string Name, int Value)>();
        for (var i = 0; i < it.BaseParam.Count; i++)
        {
            var id = it.BaseParam[i].RowId;
            if (id == 0 || it.BaseParamValue[i] == 0) continue;
            stats.Add((bp.GetRowOrDefault(id)?.Name.ExtractText() ?? $"#{id}", it.BaseParamValue[i]));
        }
        if (stats.Count > 0)
        {
            ImGui.Separator();
            var col = 0;
            foreach (var (n, v) in stats)
            {
                if (col % 2 == 1) ImGui.SameLine(200 * Theme.S);
                ImGui.TextColored(Theme.Text, $"{n} +{v}");
                col++;
            }
        }

        var extras = new List<string>();
        if (it.MateriaSlotCount > 0 || inst is { Materia.Count: > 0 })
        {
            ImGui.Separator();
            ImGui.TextColored(Theme.GoldDim, "МАТЕРИЯ" + (it.IsAdvancedMeldingPermitted ? " · можно Advanced Melding" : " · Advanced Melding нельзя"));
            if (inst is null)
                ImGui.TextColored(Theme.Gray, $"Слотов: {it.MateriaSlotCount} (вставленная материя видна, когда наводишь на свой предмет в сумке или шкафу)");
            else
            {
                for (var i = 0; i < Math.Max(it.MateriaSlotCount, inst.Materia.Count); i++)
                {
                    var over = i >= it.MateriaSlotCount;
                    if (i < inst.Materia.Count)
                    {
                        var (mn, ms, mv) = inst.Materia[i];
                        Theme.FaIcon(FontAwesomeIcon.Gem, over ? Theme.Red : Theme.Green);
                        ImGui.SameLine();
                        ImGui.TextColored(Theme.Text, $"{ms} +{mv}");
                        ImGui.SameLine();
                        ImGui.TextColored(Theme.TextDim, mn + (over ? " (сверх слотов)" : ""));
                    }
                    else
                    {
                        Theme.FaIcon(FontAwesomeIcon.Circle, Theme.Gray);
                        ImGui.SameLine();
                        ImGui.TextColored(Theme.Gray, "пустой слот");
                    }
                }
            }
        }
        if (inst is not null && it.EquipSlotCategory.RowId != 0)
        {
            ImGui.Separator();
            ImGui.TextColored(inst.Condition < 20 ? Theme.Red : Theme.Text, $"Прочность: {inst.Condition:0.##}%");
            ImGui.SameLine(200 * Theme.S);
            ImGui.TextColored(inst.Spiritbond >= 100 ? Theme.Gold : Theme.Text, $"Духовная связь: {inst.Spiritbond:0.##}%");
            if (inst.Spiritbond >= 100) ImGui.TextColored(Theme.Gold, "Можно извлечь материю ✓");
            if (inst.Glamour != 0) ImGui.TextColored(Theme.TextDim, $"Гламур: {WikiView.ItemInfo(inst.Glamour).Name}");
            if (inst.Dyes.Count > 0) ImGui.TextColored(Theme.TextDim, $"Краска: {string.Join(", ", inst.Dyes)}");
            if (inst.Crafted) ImGui.TextColored(Theme.TextDim, "Сделано игроком");
        }
        if (it.ClassJobRepair.RowId != 0)
            extras.Add($"Ремонт: {Plugin.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(it.ClassJobRepair.RowId)?.Abbreviation.ExtractText()}"
                       + (it.ItemRepair.ValueNullable?.Item.ValueNullable is { } dm ? $" · {dm.Name.ExtractText()}" : ""));
        if (it.DyeCount > 0) extras.Add($"Красится ({it.DyeCount})");
        if (it.Desynth > 0) extras.Add("Разбирается (Desynthesis)");
        if (it.IsGlamorous) extras.Add("Для гламура");
        if (extras.Count > 0)
        {
            ImGui.Separator();
            foreach (var e in extras) ImGui.TextColored(Theme.TextDim, e);
        }
        ImGui.Separator();
    }

    private void DrawSources(Item? row)
    {
        var w = WikiService.Instance.Item(itemId);
        if (w is null)
        {
            ImGui.TextColored(Theme.Gray, WikiService.Instance.Error("item", itemId) ?? "Загружаю источники…");
            return;
        }

        var max = replacing ? 9 : 6;
        var lines = 0;
        void Line(FontAwesomeIcon ic, Vector4 c, string text)
        {
            if (lines >= max) return;
            Theme.FaIcon(ic, c);
            ImGui.SameLine();
            ImGui.TextColored(Theme.Text, text);
            lines++;
        }

        if (w.HasAnySource) ImGui.TextColored(Theme.GoldDim, "ОТКУДА ВЗЯТЬ");
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

        // Где используется
        var uses = new List<string>();
        if (w.TradeCurrency.Count > 0) uses.Add($"меняется на {w.TradeCurrency.Count} предметов");
        if (w.IngredientOf.Count > 0) uses.Add($"нужен в {w.IngredientOf.Count} рецептах");
        if (row is { } it && it.EquipSlotCategory.RowId != 0 && it.Rarity is 2 or 3 && !it.IsIndisposable && it.PriceLow > 0
            && Plugin.Data.GetExcelSheet<GCSupplyDutyReward>().GetRowOrDefault(it.LevelItem.RowId)?.SealsExpertDelivery is { } seals and > 0)
            uses.Add($"GC Expert Delivery: {seals:N0} печатей");
        if (uses.Count > 0)
        {
            ImGui.TextColored(Theme.GoldDim, "ГДЕ ИСПОЛЬЗУЕТСЯ");
            foreach (var u in uses) ImGui.TextColored(Theme.Blue, "• " + u);
        }
        if (!w.HasAnySource && uses.Count == 0)
            ImGui.TextColored(Theme.Gray, "Нет данных об источниках");
    }
}
