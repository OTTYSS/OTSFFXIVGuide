using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Коллекции: маунты, минёны, оркестрион, карты, броня чокобо, аксессуары, причёски, очки, Framer's kits.</summary>
internal sealed class CollectionsTab
{
    private sealed record Entry(uint Id, string Name, uint Icon, uint ItemId);

    private sealed class Kind
    {
        public required string Title;
        public required Func<List<Entry>> Load;
        public required Func<uint, bool> Unlocked;
        public List<Entry>? Entries;
    }

    private static readonly string[] ShowNames = { "Чего не хватает", "Что уже есть" };

    private readonly GuideData data;
    private readonly Kind[] kinds;
    private int kindIdx;
    private int show;
    private string search = "";
    private Dictionary<(uint ActionType, uint UnlockId), uint>? unlockItems;

    // Тип действия предмета (ItemAction.Action), который открывает коллекцию
    private const uint ActionMount = 1322, ActionMinion = 853, ActionOrchestrion = 25183, ActionCard = 3357,
        ActionBarding = 1013, ActionOrnament = 20086, ActionGlasses = 37312, ActionFramersKit = 29459;

    public CollectionsTab(GuideData data)
    {
        this.data = data;
        var u = Plugin.Unlocks;
        kinds = new[]
        {
            new Kind
            {
                Title = "Маунты",
                Load = () => Plugin.Data.GetExcelSheet<Mount>()
                    .Where(m => m.Icon != 0 && m.Order >= 0 && !m.Singular.IsEmpty)
                    .Select(m => new Entry(m.RowId, GuideData.Capitalize(m.Singular.ExtractText()), m.Icon, ItemFor(ActionMount, m.RowId))).ToList(),
                Unlocked = id => Plugin.Data.GetExcelSheet<Mount>().GetRowOrDefault(id) is { } r && u.IsMountUnlocked(r),
            },
            new Kind
            {
                Title = "Минёны",
                Load = () => Plugin.Data.GetExcelSheet<Companion>()
                    .Where(m => m.Icon != 0 && !m.Singular.IsEmpty)
                    .Select(m => new Entry(m.RowId, GuideData.Capitalize(m.Singular.ExtractText()), m.Icon, ItemFor(ActionMinion, m.RowId))).ToList(),
                Unlocked = id => Plugin.Data.GetExcelSheet<Companion>().GetRowOrDefault(id) is { } r && u.IsCompanionUnlocked(r),
            },
            new Kind
            {
                Title = "Оркестрион",
                Load = () => Plugin.Data.GetExcelSheet<Orchestrion>()
                    .Where(m => !m.Name.IsEmpty)
                    .Select(m => new Entry(m.RowId, m.Name.ExtractText(), 0, ItemFor(ActionOrchestrion, m.RowId))).ToList(),
                Unlocked = id => Plugin.Data.GetExcelSheet<Orchestrion>().GetRowOrDefault(id) is { } r && u.IsOrchestrionUnlocked(r),
            },
            new Kind
            {
                Title = "Карты Triple Triad",
                Load = () => Plugin.Data.GetExcelSheet<TripleTriadCard>()
                    .Where(m => !m.Name.IsEmpty)
                    .Select(m => new Entry(m.RowId, m.Name.ExtractText(), 0, ItemFor(ActionCard, m.RowId))).ToList(),
                Unlocked = id => Plugin.Data.GetExcelSheet<TripleTriadCard>().GetRowOrDefault(id) is { } r && u.IsTripleTriadCardUnlocked(r),
            },
            new Kind
            {
                Title = "Броня чокобо",
                Load = () => Plugin.Data.GetExcelSheet<BuddyEquip>()
                    .Where(m => !m.Name.IsEmpty)
                    .Select(m => new Entry(m.RowId, GuideData.Capitalize(m.Name.ExtractText()), (uint)(m.IconBody != 0 ? m.IconBody : m.IconHead), ItemFor(ActionBarding, m.RowId))).ToList(),
                Unlocked = id => Plugin.Data.GetExcelSheet<BuddyEquip>().GetRowOrDefault(id) is { } r && u.IsBuddyEquipUnlocked(r),
            },
            new Kind
            {
                Title = "Аксессуары",
                Load = () => Plugin.Data.GetExcelSheet<Ornament>()
                    .Where(m => !m.Singular.IsEmpty && m.Icon != 0)
                    .Select(m => new Entry(m.RowId, GuideData.Capitalize(m.Singular.ExtractText()), m.Icon, ItemFor(ActionOrnament, m.RowId))).ToList(),
                Unlocked = id => Plugin.Data.GetExcelSheet<Ornament>().GetRowOrDefault(id) is { } r && u.IsOrnamentUnlocked(r),
            },
            ItemKind("Причёски и раскраска", HairstyleItems),
            ItemKind("Очки", () => ItemsWithAction(ActionGlasses)),
            ItemKind("Framer's kits", () => ItemsWithAction(ActionFramersKit)),
        };
    }

    /// <summary>Коллекция, которая открывается предметами: запись = сам предмет.</summary>
    private static Kind ItemKind(string title, Func<IEnumerable<Item>> items) => new()
    {
        Title = title,
        Load = () => items().Where(i => !i.Name.IsEmpty)
            .Select(i => new Entry(i.RowId, i.Name.ExtractText(), i.Icon, i.RowId)).ToList(),
        Unlocked = id => Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(id) is { } r && Plugin.Unlocks.IsItemUnlocked(r),
    };

    private static IEnumerable<Item> ItemsWithAction(uint action) =>
        Plugin.Data.GetExcelSheet<Item>().Where(i => i.ItemAction.ValueNullable is { } a && a.Action.RowId == action);

    /// <summary>Книги причёсок и раскраски лица: предметы-подсказки у покупаемых вариантов внешности.</summary>
    private static IEnumerable<Item> HairstyleItems()
    {
        var items = Plugin.Data.GetExcelSheet<Item>();
        return Plugin.Data.GetExcelSheet<CharaMakeCustomize>()
            .Where(c => c.IsPurchasable && c.HintItem.RowId != 0)
            .Select(c => c.HintItem.RowId).Distinct()
            .Select(id => items.GetRowOrDefault(id)).Where(i => i is not null).Select(i => i!.Value);
    }

    private uint ItemFor(uint actionType, uint unlockId)
    {
        if (unlockItems is null)
        {
            unlockItems = new();
            foreach (var item in Plugin.Data.GetExcelSheet<Item>())
            {
                var act = item.ItemAction.ValueNullable;
                if (act is null || act.Value.Data.Count == 0) continue;
                var key = (act.Value.Action.RowId, (uint)act.Value.Data[0]);
                unlockItems.TryAdd(key, item.RowId);
            }
        }
        return unlockItems.TryGetValue((actionType, unlockId), out var id) ? id : 0;
    }

    public void Draw()
    {
        Theme.Header("Коллекции", "Что собрано и где взять недостающее");

        if (!PlayerInfo.Ready)
        {
            Theme.Panel("cnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем, чтобы увидеть коллекции."));
            return;
        }

        // Сводка по всем видам
        Theme.Panel("csum", () =>
        {
            for (var i = 0; i < kinds.Length; i++)
            {
                var k = kinds[i];
                k.Entries ??= k.Load();
                var have = k.Entries.Count(e => k.Unlocked(e.Id));
                if (Theme.ProgressButton($"k{i}", k.Title, have, k.Entries.Count, kindIdx == i ? Theme.Gold : Theme.Blue))
                    kindIdx = i;
            }
        });

        var kind = kinds[kindIdx];
        kind.Entries ??= kind.Load();

        Theme.Chips("cshow", ShowNames, ref show);
        Theme.Search("##csearch", $"Поиск: {kind.Title.ToLowerInvariant()}…", ref search);

        var rows = kind.Entries
            .Where(e => kind.Unlocked(e.Id) == (show == 1))
            .Where(e => search.Length == 0 || NameTranslations.Matches(e.Name, search))
            .OrderBy(e => e.Name)
            .ToList();
        ImGui.TextColored(Theme.TextDim, $"{kind.Title}: {rows.Count}");

        if (ImGui.BeginChild("##clist", Vector2.Zero))
        {
            var rowH = 46 * Theme.S;
            Theme.VirtualList(rows.Count, rowH, i =>
            {
                var e = rows[i];
                string sub;
                if (e.ItemId == 0)
                    sub = show == 1 ? "собрано" : "Открывается не предметом (квест, достижение, контент)";
                else
                {
                    var itemRow = Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(e.ItemId);
                    var marketable = itemRow is { } ir && ir.ItemSearchCategory.RowId != 0 && !ir.IsUntradable;
                    var src = data.Sources.Get(e.ItemId, marketable).FirstOrDefault();
                    sub = $"Предмет: {itemRow?.Name.ExtractText()}" + (src is null ? "" : $" · {src.Text}");
                }
                var icon = e.Icon != 0 ? e.Icon : (e.ItemId != 0 ? (uint)(Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(e.ItemId)?.Icon ?? 0) : 0);
                if (Theme.Card($"##ce{e.Id}", icon, e.Name, sub, show == 1 ? "есть" : null, Theme.Green, false, rowH) && e.ItemId != 0)
                    Dalamud.Utility.Util.OpenLink($"https://www.garlandtools.org/db/#item/{e.ItemId}");
            });
        }
        ImGui.EndChild();
    }
}
