using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Game;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

/// <summary>Данжи: строка из ContentFinderCondition + квест, который её открывает.</summary>
public sealed record DutyEntry(
    uint CfcId,
    string Name,
    string Type,
    string Expansion,
    byte Level,
    ushort ItemLevel,
    uint InstanceContentId,
    uint UnlockQuestId,
    bool HighEnd,
    ushort SortKey,
    uint Icon,
    uint Image);

/// <summary>Квест с тем, что он открывает.</summary>
public sealed record QuestEntry(
    uint Id,
    string Name,
    string Category,
    string Genre,
    string Place,
    string Expansion,
    int Level,
    string Unlocks,
    uint[] Previous,
    uint IssuerLevelId,
    uint Icon,
    bool IsMsq,
    uint CategoryId,
    ushort SortKey);

/// <summary>Снаряжение, которое можно надеть.</summary>
public sealed record GearEntry(
    uint Id,
    string Name,
    ushort Icon,
    uint ItemLevel,
    byte EquipLevel,
    uint ClassJobCategory,
    GearSlot Slot,
    (uint Param, int Value)[] Stats,
    bool Pvp,
    bool Marketable);

public enum GearSlot
{
    MainHand, OffHand, Head, Body, Hands, Legs, Feet, Ears, Neck, Wrists, Ring,
}

/// <summary>
/// Статические данные из файлов игры. Строятся один раз при первом обращении.
/// </summary>
public sealed class GuideData
{
    private List<DutyEntry>? duties;
    private Dictionary<uint, QuestEntry>? quests;
    private List<GearEntry>? gear;
    private readonly Dictionary<(uint Category, string Abbr), bool> jobCategoryCache = new();

    public IReadOnlyList<DutyEntry> Duties => duties ??= BuildDuties();
    public IReadOnlyDictionary<uint, QuestEntry> Quests => quests ??= BuildQuests();
    public IReadOnlyList<GearEntry> Gear => gear ??= BuildGear();

    private ItemSources? sources;
    public ItemSources Sources => sources ??= new ItemSources();

    public static readonly Dictionary<uint, string> StatNames = new()
    {
        [1] = "Сила", [2] = "Ловкость", [3] = "Живучесть", [4] = "Интеллект", [5] = "Разум",
        [6] = "Набожность", [19] = "Стойкость", [22] = "Прямой удар", [27] = "Крит",
        [44] = "Решимость", [45] = "Скорость навыков", [46] = "Скорость заклинаний",
        [12] = "Физ. урон", [13] = "Маг. урон", [21] = "Защита", [24] = "Маг. защита",
        [70] = "Мастерство (крафт)", [71] = "Контроль", [11] = "CP",
        [72] = "Сбор", [73] = "Восприятие", [10] = "GP",
    };

    private List<DutyEntry> BuildDuties()
    {
        // InstanceContent -> квест, который его открывает
        var unlockBy = new Dictionary<uint, uint>();
        foreach (var q in Plugin.Data.GetExcelSheet<Quest>())
        {
            var ic = q.InstanceContentUnlock.RowId;
            if (ic != 0 && !unlockBy.ContainsKey(ic))
                unlockBy[ic] = q.RowId;
        }

        var result = new List<DutyEntry>();
        foreach (var cfc in Plugin.Data.GetExcelSheet<ContentFinderCondition>())
        {
            var name = cfc.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name) || cfc.PvP)
                continue;

            var type = cfc.ContentType.ValueNullable?.Name.ExtractText() ?? "";
            if (string.IsNullOrEmpty(type))
                continue;

            var icId = cfc.ContentLinkType == 1 ? cfc.Content.RowId : 0;
            unlockBy.TryGetValue(icId, out var questId);

            result.Add(new DutyEntry(
                cfc.RowId,
                Capitalize(name),
                type,
                cfc.RequiredExVersion.ValueNullable?.Name.ExtractText() ?? "",
                cfc.ClassJobLevelRequired,
                cfc.ItemLevelRequired,
                icId,
                questId,
                cfc.HighEndDuty,
                cfc.SortKey,
                cfc.ContentType.ValueNullable?.Icon ?? 0,
                cfc.Image));
        }

        return result
            .OrderBy(d => d.Level)
            .ThenBy(d => d.ItemLevel)
            .ThenBy(d => d.SortKey)
            .ToList();
    }

    private Dictionary<uint, QuestEntry> BuildQuests()
    {
        var result = new Dictionary<uint, QuestEntry>();
        var dutyByIc = Duties.Where(d => d.InstanceContentId != 0).GroupBy(d => d.InstanceContentId).ToDictionary(g => g.Key, g => g.First());
        foreach (var q in Plugin.Data.GetExcelSheet<Quest>())
        {
            var name = q.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var unlocks = new List<string>();
            if (q.InstanceContentUnlock.RowId != 0)
            {
                dutyByIc.TryGetValue(q.InstanceContentUnlock.RowId, out var duty);
                unlocks.Add(duty is null ? "контент" : $"данж/бой: {duty.Name}");
            }
            if (q.ClassJobUnlock.RowId != 0)
                unlocks.Add($"класс/джоба: {q.ClassJobUnlock.ValueNullable?.Name.ExtractText()}");
            if (q.ActionReward.RowId != 0)
                unlocks.Add($"навык: {q.ActionReward.ValueNullable?.Name.ExtractText()}");
            if (q.GeneralActionReward.Any(a => a.RowId != 0))
                unlocks.Add("общее действие");
            if (q.EmoteReward.RowId != 0)
                unlocks.Add($"эмоция: {q.EmoteReward.ValueNullable?.Name.ExtractText()}");

            var genre = q.JournalGenre.ValueNullable;
            result[q.RowId] = new QuestEntry(
                q.RowId,
                name,
                genre?.JournalCategory.ValueNullable?.Name.ExtractText() ?? "",
                genre?.Name.ExtractText() ?? "",
                q.PlaceName.ValueNullable?.Name.ExtractText() ?? "",
                q.Expansion.ValueNullable?.Name.ExtractText() ?? "",
                q.ClassJobLevel.Count > 0 ? q.ClassJobLevel[0] : 0,
                string.Join(", ", unlocks.Where(u => !string.IsNullOrWhiteSpace(u))),
                q.PreviousQuest.Select(p => p.RowId).Where(id => id != 0).ToArray(),
                q.IssuerLocation.RowId,
                (uint)Math.Max(0, genre?.Icon ?? 0),
                (genre?.JournalCategory.ValueNullable?.JournalSection.RowId ?? 99) <= 1,
                genre?.JournalCategory.RowId ?? 0,
                q.SortKey);
        }

        return result;
    }

    private static List<GearEntry> BuildGear()
    {
        var result = new List<GearEntry>();
        foreach (var item in Plugin.Data.GetExcelSheet<Item>())
        {
            if (item.EquipSlotCategory.RowId == 0 || item.ClassJobCategory.RowId == 0)
                continue;

            var slotRow = item.EquipSlotCategory.ValueNullable;
            if (slotRow is null)
                continue;

            var slot = SlotOf(slotRow.Value);
            if (slot is null)
                continue;

            var name = item.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var stats = new List<(uint, int)>();
            for (var i = 0; i < item.BaseParam.Count; i++)
            {
                var p = item.BaseParam[i].RowId;
                if (p != 0 && item.BaseParamValue[i] != 0)
                    stats.Add((p, item.BaseParamValue[i]));
            }

            result.Add(new GearEntry(
                item.RowId,
                name,
                item.Icon,
                item.LevelItem.RowId,
                item.LevelEquip,
                item.ClassJobCategory.RowId,
                slot.Value,
                stats.ToArray(),
                item.RequiredPvpRank > 0,
                item.ItemSearchCategory.RowId != 0 && !item.IsUntradable));
        }

        return result;
    }

    public static GearSlot? SlotOf(EquipSlotCategory c)
    {
        if (c.MainHand == 1) return GearSlot.MainHand;
        if (c.OffHand == 1) return GearSlot.OffHand;
        if (c.Head == 1) return GearSlot.Head;
        if (c.Body == 1) return GearSlot.Body;
        if (c.Gloves == 1) return GearSlot.Hands;
        if (c.Legs == 1) return GearSlot.Legs;
        if (c.Feet == 1) return GearSlot.Feet;
        if (c.Ears == 1) return GearSlot.Ears;
        if (c.Neck == 1) return GearSlot.Neck;
        if (c.Wrists == 1) return GearSlot.Wrists;
        if (c.FingerL == 1 || c.FingerR == 1) return GearSlot.Ring;
        return null;
    }

    /// <summary>Входит ли профессия (по английской аббревиатуре) в ClassJobCategory.</summary>
    public bool JobInCategory(uint category, string jobAbbr)
    {
        if (jobCategoryCache.TryGetValue((category, jobAbbr), out var cached))
            return cached;

        var value = false;
        var row = Plugin.Data.GetExcelSheet<ClassJobCategory>().GetRowOrDefault(category);
        var prop = typeof(ClassJobCategory).GetProperty(jobAbbr, BindingFlags.Public | BindingFlags.Instance);
        if (row is not null && prop?.PropertyType == typeof(bool))
            value = (bool)prop.GetValue(row.Value)!;

        jobCategoryCache[(category, jobAbbr)] = value;
        return value;
    }

    public static string JobAbbrEnglish(uint classJobId) =>
        Plugin.Data.GetExcelSheet<ClassJob>(ClientLanguage.English).GetRowOrDefault(classJobId)?.Abbreviation.ExtractText() ?? "";

    public static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
