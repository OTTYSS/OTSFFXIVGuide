using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using FFXIVGuide.Windows;

namespace FFXIVGuide;

/// <summary>Настройки плагина. Хранятся в pluginConfigs/OTSFFXIVGuide.json.</summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Общее
    public MainTab StartTab { get; set; } = MainTab.Home;
    public bool QuestTextRussian { get; set; } = true;
    public bool UseTermDictionary { get; set; } = true;
    public Dictionary<string, string> CustomTerms { get; set; } = new();
    public List<string> WikiFavorites { get; set; } = new();
    public List<string> WeatherAlerts { get; set; } = new();
    public List<string> ShoppingList { get; set; } = new();
    public List<int> PinnedTabs { get; set; } = new();
    public HashSet<string> MapMarkerKinds { get; set; } = new() { "vista", "current" };
    public Dictionary<string, int> NotifyModes { get; set; } = new();
    public List<string> HomeTiles { get; set; } = new() { "story", "daily", "character", "progress", "timers", "ocean", "week" };
    public List<string> MiniBlocks { get; set; } = new() { "story", "daily", "fate", "target", "hunt", "timers" };
    public Dictionary<string, float> SplitRatios { get; set; } = new();
    public bool SetupDone { get; set; }
    public DateTime? HouseWarned { get; set; }
    public List<uint> SeenFestivals { get; set; } = new();
    public Dictionary<uint, DateTime> SavageClears { get; set; } = new();
    public DateTime? HouseVisitPersonal { get; set; }
    public DateTime? HouseVisitFc { get; set; }
    /// <summary>Дополнять офлайн-базу данными Garland Tools (дроп мобов, лут по боссам).</summary>
    public bool WikiUseOnline { get; set; } = true;
    /// <summary>Названия: 0 — английские, 1 — русские, 2 — «русское (English)».</summary>
    public int NameMode { get; set; } = 2;
    public Dictionary<string, FFXIVGuide.Windows.VoyageInfo> Voyages { get; set; } = new();
    public bool NotifyRetainers { get; set; } = true;
    public bool NotifyVoyages { get; set; } = true;
    public List<uint> DresserItems { get; set; } = new();
    /// <summary>Подсказка при наведении на предмет в игре: 0 — выкл, 1 — с Shift, 2 — всегда.</summary>
    public int ItemPeekMode { get; set; } = 1;
    public bool MiniShowTarget { get; set; } = true;

    // Инвентарь
    public long ValuableThreshold { get; set; } = 5000;
    public HashSet<uint> ExcludedItems { get; set; } = new();

    // Дейлики/викли: ключ задачи -> когда отмечена выполненной (UTC)
    public Dictionary<string, DateTime> ChecklistDone { get; set; } = new();
    public bool RemindChecklistOnLogin { get; set; } = true;

    // Hunt
    public bool HuntAlerts { get; set; } = true;
    public bool HuntSound { get; set; } = true;
    public bool HuntRankS { get; set; } = true;
    public bool HuntRankA { get; set; } = true;
    public bool HuntRankB { get; set; } = false;

    // Ретейнеры: id -> снимок сумок
    public Dictionary<string, FFXIVGuide.Data.RetainerSnapshot> Retainers { get; set; } = new();

    // Мини-окно и горячая клавиша
    public bool MiniWindow { get; set; } = false;
    public bool HotkeyEnabled { get; set; } = true;
    public int HotkeyKey { get; set; } = 0x47; // G
    public bool HotkeyCtrl { get; set; } = true;
    public bool HotkeyShift { get; set; } = true;

    public bool CompactSidebar { get; set; } = false;
    public bool HighlightInGame { get; set; } = true;

    // Дневник прогресса: id персонажа -> снимки по дням
    public Dictionary<string, List<FFXIVGuide.Data.DiarySnapshot>> Diary { get; set; } = new();
    public int AccentColor { get; set; } = 0;
    public float UiScale { get; set; } = 1f;
    /// <summary>Размер картинок в списках коллекций: 0 мелкие, 1 средние, 2 крупные, 3 плитки.</summary>
    public int PreviewSize { get; set; } = 1;

    // Механики
    public bool DutyOverlay { get; set; } = true;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
