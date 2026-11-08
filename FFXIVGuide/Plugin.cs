using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVGuide.Data;
using FFXIVGuide.Windows;

namespace FFXIVGuide;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IUnlockState Unlocks { get; private set; } = null!;
    [PluginService] internal static IFateTable Fates { get; private set; } = null!;
    [PluginService] internal static IKeyState Keys { get; private set; } = null!;
    [PluginService] internal static IPartyList Party { get; private set; } = null!;
    [PluginService] internal static ITargetManager Targets { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static Dalamud.Plugin.Services.INotificationManager Notifications { get; private set; } = null!;

    internal static Configuration Config { get; private set; } = null!;

    private const string Command = "/fguide";

    private readonly WindowSystem windows = new("FFXIVGuide");
    private readonly MainWindow mainWindow;
    private readonly DutyOverlayWindow overlay;
    private readonly MarketService market;
    private readonly WikiService wiki;
    private readonly OfflineDb offlineDb;
    private readonly NameTranslations names;
    private readonly MobImages mobImages;
    private readonly HuntTracker hunts;
    private readonly MiniWindow mini;
    private bool hotkeyWasDown;

    /// <summary>
    /// Плагин раньше назывался FFXIVGuide: переносим настройки, офлайн-базу и переводы,
    /// если под новым именем ещё ничего нет. Старые файлы не удаляются.
    /// </summary>
    private static void MigrateFromOldName()
    {
        try
        {
            var newFile = PluginInterface.ConfigFile;
            var newDir = PluginInterface.ConfigDirectory;
            var root = newFile.Directory;
            if (root is null) return;
            var oldFile = new System.IO.FileInfo(System.IO.Path.Combine(root.FullName, "FFXIVGuide.json"));
            var oldDir = new System.IO.DirectoryInfo(System.IO.Path.Combine(root.FullName, "FFXIVGuide"));
            if (!newFile.Exists && oldFile.Exists)
                oldFile.CopyTo(newFile.FullName);
            var newEmpty = !newDir.Exists || !newDir.EnumerateFileSystemInfos().Any();
            if (newEmpty && oldDir.Exists)
                CopyDir(oldDir, newDir);
        }
        catch (System.Exception e)
        {
            Log.Warning(e, "Не удалось перенести настройки со старого имени плагина");
        }
    }

    private static void CopyDir(System.IO.DirectoryInfo from, System.IO.DirectoryInfo to)
    {
        to.Create();
        foreach (var f in from.GetFiles()) f.CopyTo(System.IO.Path.Combine(to.FullName, f.Name), false);
        foreach (var d in from.GetDirectories()) CopyDir(d, new System.IO.DirectoryInfo(System.IO.Path.Combine(to.FullName, d.Name)));
    }

    public Plugin()
    {
        MigrateFromOldName();
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Instance = this;

        Theme.InitFonts();
        var data = new GuideData();

        wiki = new WikiService();
        Translator.Instance.LoadFromDisk();
        offlineDb = new OfflineDb();
        names = new NameTranslations();
        mobImages = new MobImages();
        // Тяжёлые справочники собираем в фоне сразу при запуске, чтобы разделы потом открывались мгновенно
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                _ = data.Duties;
                _ = data.Quests;
                _ = data.Gear;
                _ = data.Sources;
                _ = GatherIndex.All;
                _ = MechanicsDb.Count;
                _ = WikiService.Instance?.MobIndex();
            }
            catch (System.Exception e)
            {
                Log.Warning(e, "Фоновая загрузка данных не удалась — соберутся при открытии раздела");
            }
        });
        market = new MarketService();
        hunts = new HuntTracker();
        mainWindow = new MainWindow(data, market, hunts);
        overlay = new DutyOverlayWindow(data);
        windows.AddWindow(mainWindow);
        windows.AddWindow(overlay);
        mini = new MiniWindow(data, hunts, mainWindow.OpenTab) { IsOpen = Config.MiniWindow };
        windows.AddWindow(mini);
        windows.AddWindow(new ItemPeekWindow(market));
        ProgressDiary.Init(data);

        Commands.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Открыть справочник. /fguide <что искать> | story | duties | quests | items | gear | inv | daily | craft | hunt | collect | level | fate | gather | recap | deep | treasure | build | diary | ach | mob | wiki | timers | weather | saucer | tribes | glam | shop | subs | island | scrips | relic | ocean | unlocks | events | bis | mounts | furniture | ventures | raid | fish | blu | target | peek | mini | overlay | settings",
        });

        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.Draw += InventoryHighlight.Draw;
        PluginInterface.UiBuilder.OpenMainUi += mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
        ClientState.Login += OnLogin;
        ClientState.TerritoryChanged += overlay.OnTerritoryChanged;
        ClientState.TerritoryChanged += DutyRecorder.OnTerritoryChanged;
        Framework.Update += hunts.OnUpdate;
        Framework.Update += OnHotkey;
        DutyState.DutyCompleted += FFXIVGuide.Data.RaidTier.OnDutyCompleted;

        // Если плагин включили уже внутри данжа — сразу показать механики
        if (ClientState.IsLoggedIn)
        {
            overlay.OnTerritoryChanged(ClientState.TerritoryType);
            DutyRecorder.OnTerritoryChanged(ClientState.TerritoryType);
        }
    }

    private void OpenSettings() => mainWindow.OpenTab(MainTab.Settings);

    public static void SetMiniWindow(bool open)
    {
        Config.MiniWindow = open;
        Config.Save();
        Instance?.mini.IsOpen = open;
    }

    private static Plugin? Instance;

    /// <summary>Горячая клавиша: по умолчанию Ctrl+Shift+G открывает/закрывает окно.</summary>
    private void OnHotkey(IFramework framework)
    {
        if (!Config.HotkeyEnabled) return;
        var key = (Dalamud.Game.ClientState.Keys.VirtualKey)Config.HotkeyKey;
        var down = Keys[key]
                   && (!Config.HotkeyCtrl || Keys[Dalamud.Game.ClientState.Keys.VirtualKey.CONTROL])
                   && (!Config.HotkeyShift || Keys[Dalamud.Game.ClientState.Keys.VirtualKey.SHIFT]);
        if (down && !hotkeyWasDown)
            mainWindow.Toggle();
        hotkeyWasDown = down;
    }

    private void OnLogin()
    {
        if (Config.RemindChecklistOnLogin)
            Checklist.RemindInChat();
        ProgressDiary.OnLogin();
    }

    private void OnCommand(string command, string args)
    {
        var tab = args.Trim().ToLowerInvariant() switch
        {
            "story" or "msq" or "сюжет" => MainTab.Story,
            "search" or "поиск" => MainTab.Search,
            "inventory" or "inv" or "инвентарь" => MainTab.Inventory,
            "duties" or "dungeons" or "данжи" => MainTab.Duties,
            "quests" or "квесты" => MainTab.Quests,
            "items" or "предметы" => MainTab.Items,
            "gear" or "экипировка" => MainTab.Gear,
            "daily" or "weekly" or "дейлики" => MainTab.Checklist,
            "craft" or "крафт" => MainTab.Craft,
            "hunt" or "охота" => MainTab.Hunt,
            "collect" or "collections" or "коллекции" => MainTab.Collections,
            "settings" or "config" or "настройки" => MainTab.Settings,
            "level" or "skills" or "прокачка" => MainTab.Leveling,
            "fate" or "fates" or "атэриты" => MainTab.Fates,
            "gather" or "сбор" => MainTab.Gather,
            "recap" or "разбор" => MainTab.Recap,
            "deep" or "potd" => MainTab.Deep,
            "treasure" or "maps" or "карты" => MainTab.Treasure,
            "build" or "materia" or "food" or "материя" => MainTab.Build,
            "diary" or "дневник" => MainTab.Diary,
            "achievements" or "ach" or "достижения" => MainTab.Achievements,
            "mob" or "mobs" or "bestiary" or "мобы" or "бестиарий" => MainTab.Bestiary,
            "wiki" or "вики" or "zone" or "npc" => MainTab.Wiki,
            "weather" or "погода" or "et" => MainTab.Weather,
            "timers" or "таймеры" => MainTab.Timers,
            "saucer" or "cactpot" or "tt" or "gs" => MainTab.GoldSaucer,
            "tribes" or "племена" => MainTab.Tribes,
            "glam" or "glamour" or "гламур" => MainTab.Glamour,
            "shop" or "shopping" or "покупки" => MainTab.Shopping,
            "subs" or "voyages" or "субмарины" => MainTab.Voyages,
            "island" or "остров" => MainTab.Island,
            "scrips" or "collectables" => MainTab.CollectableItems,
            "relic" or "relics" or "eureka" or "bozja" or "реликвии" => MainTab.Relics,
            "ocean" or "океан" => MainTab.Ocean,
            "unlocks" or "emotes" or "эмоции" => MainTab.Unlocks,
            "events" or "deliveries" or "house" or "события" => MainTab.Weekly,
            "bis" => MainTab.Bis,
            "mounts" or "маунты" => MainTab.MountFarm,
            "furniture" or "мебель" => MainTab.Furniture,
            "ventures" or "вылазки" => MainTab.Ventures,
            "raid" or "лимиты" => MainTab.Weekly,
            "overlay" => (MainTab?)null,
            _ => (MainTab?)null,
        };

        var a0 = args.Trim().ToLowerInvariant();
        if (a0 is "peek" or "item")
        {
            // Карточка предмета, на который последним наводил в игре
            if (ItemPeekWindow.LastHovered != 0)
                FFXIVGuide.Windows.WikiNav.Open(FFXIVGuide.Windows.WikiKind.Item, ItemPeekWindow.LastHovered, FFXIVGuide.Windows.WikiView.ItemInfo(ItemPeekWindow.LastHovered).Name);
            else
                mainWindow.OpenTab(MainTab.Items);
            return;
        }
        if (a0 is "target" or "цель")
        {
            if (FFXIVGuide.Windows.BestiaryTab.TargetMobId() is { } tid)
                FFXIVGuide.Windows.WikiNav.Open(FFXIVGuide.Windows.WikiKind.Mob, tid, Targets.Target?.Name.TextValue ?? "");
            else
                mainWindow.OpenTab(MainTab.Bestiary);
            return;
        }
        if (a0 is "fish" or "рыбалка") { mainWindow.OpenWiki(3); return; }
        if (a0 is "blu" or "blue" or "синий") { mainWindow.OpenWiki(4); return; }

        if (args.Trim().ToLowerInvariant() == "mini")
        {
            SetMiniWindow(!mini.IsOpen);
            return;
        }

        if (args.Trim().ToLowerInvariant() == "overlay")
        {
            overlay.Toggle();
            return;
        }

        if (tab is null)
        {
            if (args.Trim().Length > 0)
                mainWindow.OpenSearch(args.Trim());   // /fguide Sastasha — сразу поиск
            else
                mainWindow.Toggle();
            return;
        }

        mainWindow.OpenTab(tab.Value);
    }

    public void Dispose()
    {
        Framework.Update -= hunts.OnUpdate;
        Framework.Update -= OnHotkey;
        DutyState.DutyCompleted -= FFXIVGuide.Data.RaidTier.OnDutyCompleted;
        Instance = null;
        ClientState.Login -= OnLogin;
        ClientState.TerritoryChanged -= overlay.OnTerritoryChanged;
        ClientState.TerritoryChanged -= DutyRecorder.OnTerritoryChanged;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.Draw -= InventoryHighlight.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
        Commands.RemoveHandler(Command);
        windows.RemoveAllWindows();
        market.Dispose();
        wiki.Dispose();
        names.Save();
        names.Dispose();
        offlineDb.Dispose();
        mobImages.Dispose();
        Translator.Instance.Dispose();
        Theme.DisposeFonts();
    }
}
