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
    /// <summary>How — как получить (если открывается не предметом или у игры есть своя подсказка).</summary>
    private sealed record Entry(uint Id, string Name, uint Icon, uint ItemId, string? How = null);

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
                    .Select(m => new Entry(m.RowId, m.Name.ExtractText(), 87000 + m.RowId, ItemFor(ActionCard, m.RowId))).ToList(),
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
            ItemKind("Причёски и раскраска", HairstyleItems, HairPreviewIcon),
            ItemKind("Очки", () => ItemsWithAction(ActionGlasses), GlassesIcon),
            ItemKind("Framer's kits", () => ItemsWithAction(ActionFramersKit), KitIcon),
            new Kind
            {
                Title = "Титулы",
                Load = LoadTitles,
                Unlocked = id => Plugin.Data.GetExcelSheet<Title>().GetRowOrDefault(id) is { } r && u.IsTitleUnlocked(r),
            },
            new Kind
            {
                Title = "Plate: фоны",
                Load = () => Plugin.Data.GetExcelSheet<BannerBg>().Where(b => !b.Name.IsEmpty)
                    .Select(b => PlateEntry(b.RowId, b.Name.ExtractText(), b.Image != 0 ? (uint)b.Image : (uint)b.Icon, b.UnlockCondition.RowId)).OfType<Entry>().ToList(),
                Unlocked = id => Plugin.Data.GetExcelSheet<BannerBg>().GetRowOrDefault(id) is { } r && u.IsBannerBgUnlocked(r),
            },
            new Kind
            {
                Title = "Plate: рамки",
                Load = () => Plugin.Data.GetExcelSheet<BannerFrame>().Where(b => !b.Name.IsEmpty)
                    .Select(b => PlateEntry(b.RowId, b.Name.ExtractText(), b.Image != 0 ? (uint)b.Image : (uint)b.Icon, b.UnlockCondition.RowId)).OfType<Entry>().ToList(),
                Unlocked = id => Plugin.Data.GetExcelSheet<BannerFrame>().GetRowOrDefault(id) is { } r && u.IsBannerFrameUnlocked(r),
            },
            new Kind
            {
                Title = "Plate: акценты",
                Load = () => Plugin.Data.GetExcelSheet<BannerDecoration>().Where(b => !b.Name.IsEmpty)
                    .Select(b => PlateEntry(b.RowId, b.Name.ExtractText(), b.Image != 0 ? (uint)b.Image : (uint)b.Icon, b.UnlockCondition.RowId)).OfType<Entry>().ToList(),
                Unlocked = id => Plugin.Data.GetExcelSheet<BannerDecoration>().GetRowOrDefault(id) is { } r && u.IsBannerDecorationUnlocked(r),
            },
            new Kind
            {
                Title = "Plate: позы",
                Load = () => Plugin.Data.GetExcelSheet<BannerTimeline>()
                    .Select(b => PlateEntry(b.RowId, PoseName(b), (uint)b.Icon, b.UnlockCondition.RowId)).OfType<Entry>().ToList(),
                Unlocked = id => Plugin.Data.GetExcelSheet<BannerTimeline>().GetRowOrDefault(id) is { } r && u.IsBannerTimelineUnlocked(r),
            },
            new Kind
            {
                Title = "Plate: мимика",
                Load = () => Plugin.Data.GetExcelSheet<BannerFacial>()
                    .Select(b => PlateEntry(b.RowId, b.Emote.ValueNullable?.Name.ExtractText() ?? "", (uint)(b.Emote.ValueNullable?.Icon ?? 0), b.UnlockCondition.RowId)).OfType<Entry>().ToList(),
                Unlocked = id => Plugin.Data.GetExcelSheet<BannerFacial>().GetRowOrDefault(id) is { } r && u.IsBannerFacialUnlocked(r),
            },
        };
    }

    private Dictionary<uint, uint>? kitByCondition;

    private static string PoseName(BannerTimeline b)
    {
        var n = b.Name.ExtractText();
        if (n.Length > 0) return n;
        // Позы-эмоции хранят имя в таблице эмоций
        return Plugin.Data.GetExcelSheet<Emote>().GetRowOrDefault(b.AdditionalData.RowId)?.Name.ExtractText() ?? "";
    }

    /// <summary>Элемент портрета: null — если он открыт у всех с самого начала (или имени нет).</summary>
    private Entry? PlateEntry(uint id, string name, uint icon, uint condId)
    {
        if (name.Length == 0 || condId == 0) return null;
        var cond = Plugin.Data.GetExcelSheet<BannerCondition>().GetRowOrDefault(condId);
        if (cond is null || cond.Value.UnlockType1 == 10) return null; // доступен сразу
        if (kitByCondition is null)
        {
            kitByCondition = new();
            foreach (var it in ItemsWithAction(ActionFramersKit)) kitByCondition.TryAdd(it.AdditionalData.RowId, it.RowId);
        }
        var kit = kitByCondition.GetValueOrDefault(condId);
        string? how = null;
        var hint = Plugin.Data.GetExcelSheet<BannerObtainHintType>().GetRowOrDefault(cond.Value.UnlockHint.RowId)?.Text.ExtractText() ?? "";
        if (hint.Length > 0) how = hint;
        else if (cond.Value.UnlockType1 == 1 && cond.Value.UnlockCriteria1.Count > 0
                 && Plugin.Data.GetExcelSheet<Quest>().GetRowOrDefault(cond.Value.UnlockCriteria1[0].RowId) is { } q && !q.Name.IsEmpty)
            how = $"Квест «{NameTranslations.Show(q.Name.ExtractText())}»";
        else if (cond.Value.UnlockType1 is 9 or 11)
        {
            // Награды сезонов Crystalline Conflict: через достижение или у NPC за PvP-валюту
            how = "Награда сезона Crystalline Conflict (PvP)";
            var c2 = cond.Value.UnlockCriteria2.RowId;
            if (cond.Value.UnlockType2 == 11 && Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Achievement>().GetRowOrDefault(c2) is { } a && !a.Name.IsEmpty)
                how += $": достижение «{NameTranslations.Show(a.Name.ExtractText())}»";
            else if (cond.Value.UnlockType2 == 13 && Plugin.Data.GetExcelSheet<ENpcResident>().GetRowOrDefault(c2) is { } npc && !npc.Singular.IsEmpty)
                how += $": у NPC {GuideData.Capitalize(npc.Singular.ExtractText())}";
        }
        else if (kit == 0) how = "Способ получения в файлах игры не описан";
        return new Entry(id, name, icon, kit, how);
    }

    private static List<Entry> LoadTitles()
    {
        var byTitle = new Dictionary<uint, Lumina.Excel.Sheets.Achievement>();
        foreach (var a in Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Achievement>())
            if (a.Title.RowId != 0) byTitle.TryAdd(a.Title.RowId, a);
        return Plugin.Data.GetExcelSheet<Title>().Where(t => !t.Masculine.IsEmpty)
            .Select(t =>
            {
                var how = byTitle.TryGetValue(t.RowId, out var a)
                    ? $"Достижение «{NameTranslations.Show(a.Name.ExtractText())}»: {a.Description.ExtractText()}"
                    : "Не за достижение (квест, событие или контент)";
                var name = t.Masculine.ExtractText();
                var fem = t.Feminine.ExtractText();
                if (fem.Length > 0 && fem != name) name += " / " + fem;
                return new Entry(t.RowId, name, 0, 0, how);
            }).ToList();
    }

    /// <summary>Коллекция, которая открывается предметами: запись = сам предмет.</summary>
    private static Kind ItemKind(string title, Func<IEnumerable<Item>> items, Func<uint, uint>? iconFor = null) => new()
    {
        Title = title,
        Load = () => items().Where(i => !i.Name.IsEmpty)
            .Select(i => new Entry(i.RowId, i.Name.ExtractText(), iconFor?.Invoke(i.RowId) is { } ic and not 0 ? ic : i.Icon, i.RowId)).ToList(),
        Unlocked = id => Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(id) is { } r && Plugin.Unlocks.IsItemUnlocked(r),
    };

    /// <summary>Книга очков → значок самих очков (AdditionalData книги — строка таблицы Glasses).</summary>
    private static uint GlassesIcon(uint itemId) =>
        Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId) is { } it
        && Plugin.Data.GetExcelSheet<Glasses>().GetRowOrDefault(it.AdditionalData.RowId) is { } g ? (uint)g.Icon : 0;

    private static Dictionary<uint, uint>? kitImages;

    /// <summary>Framer's kit → картинка фона (или рамки, акцента), который он открывает.</summary>
    private static uint KitIcon(uint itemId)
    {
        if (kitImages is null)
        {
            kitImages = new();
            foreach (var b in Plugin.Data.GetExcelSheet<BannerBg>()) if (b.Image != 0) kitImages.TryAdd(b.UnlockCondition.RowId, (uint)b.Image);
            foreach (var b in Plugin.Data.GetExcelSheet<BannerFrame>()) if (b.Image != 0) kitImages.TryAdd(b.UnlockCondition.RowId, (uint)b.Image);
            foreach (var b in Plugin.Data.GetExcelSheet<BannerDecoration>()) if (b.Image != 0) kitImages.TryAdd(b.UnlockCondition.RowId, (uint)b.Image);
        }
        return Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId) is { } it ? kitImages.GetValueOrDefault(it.AdditionalData.RowId) : 0;
    }

    private static Dictionary<uint, uint>? hairIcons;

    /// <summary>
    /// Картинка самой причёски вместо значка книги: берём варианты внешности для расы, племени и пола
    /// текущего персонажа (HairMakeType), у которых подсказкой указана эта книга.
    /// </summary>
    private static unsafe uint HairPreviewIcon(uint itemId)
    {
        if (hairIcons is null)
        {
            hairIcons = new();
            var cmc = Plugin.Data.GetExcelSheet<CharaMakeCustomize>();
            var ps = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
            var mine = new HashSet<uint>();
            if (ps != null)
                foreach (var h in Plugin.Data.GetExcelSheet<HairMakeType>())
                    if (h.Race.RowId == ps->Race && h.Tribe.RowId == ps->Tribe && h.Gender == ps->Sex)
                        foreach (var st in h.CharaMakeStruct)
                            foreach (var param in st.SubMenuParam) mine.Add(param);
            // Сначала варианты своей расы, потом любые
            foreach (var pass in new[] { true, false })
                foreach (var c in cmc)
                    if (c.HintItem.RowId != 0 && c.Icon != 0 && (!pass || mine.Contains(c.RowId)))
                        hairIcons.TryAdd(c.HintItem.RowId, c.Icon);
        }
        return hairIcons.GetValueOrDefault(itemId);
    }

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
            // Видов много — раскладываем сеткой, чтобы сводка не занимала весь экран
            Theme.Grid("csumgrid", kinds.Length, 260f, i =>
            {
                var k = kinds[i];
                k.Entries ??= k.Load();
                var have = k.Entries.Count(e => k.Unlocked(e.Id));
                if (Theme.ProgressButton($"k{i}", k.Title, have, k.Entries.Count, kindIdx == i ? Theme.Gold : Theme.Blue))
                    kindIdx = i;
            });
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
        // Плитки — для видов, у которых есть свои картинки; у карт в плитке большая картинка (088xxx)
        var bigImages = kind.Title is not ("Оркестрион" or "Титулы" or "Plate: позы");
        var renderOffset = kind.Title is "Маунты" or "Минёны" ? 64000u : kind.Title == "Карты Triple Triad" ? 1000u : 0u;
        Theme.PreviewSizeChips("csize", bigImages);
        ImGui.TextColored(Theme.TextDim, $"{kind.Title}: {rows.Count}");
        Translator.Instance.Request(rows.Where(e => e.How is not null && e.How.StartsWith("Obtained", StringComparison.Ordinal)).Select(e => e.How!).Distinct());

        if (ImGui.BeginChild("##clist", Vector2.Zero))
        {
            var rowH = Theme.PreviewRowHeight;
            if (Theme.PreviewTiles(bigImages))
            {
                Theme.TileGrid(rows.Count, 150 * Theme.S,
                    i => ($"##ct{rows[i].Id}", rows[i].Icon + renderOffset, rows[i].Icon, rows[i].Name, show == 1 ? "есть" : null, Theme.Green, false),
                    i =>
                    {
                        if (rows[i].ItemId != 0) WikiNav.Open(WikiKind.Item, rows[i].ItemId, Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(rows[i].ItemId)?.Name.ExtractText() ?? "");
                    });
            }
            else
            Theme.VirtualList(rows.Count, rowH, i =>
            {
                var e = rows[i];
                string sub;
                if (e.How is not null)
                    sub = Translator.Instance.Get(e.How) ?? e.How;
                else if (e.ItemId == 0)
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
