using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

public enum MainTab { Home, Search, Story, Duties, Quests, Items, Gear, Inventory, Checklist, Craft, Hunt, Collections, Settings, Leveling, Fates, Gather, Recap, Deep, Treasure, Build, Diary, Achievements, Bestiary, Wiki, Weather, Timers, GoldSaucer, Tribes, Glamour, Shopping, Voyages, Island, CollectableItems, Relics, Ocean, Unlocks, Weekly, Bis, MountFarm, Furniture, Ventures, Occult, Currencies, Statuses, Jobs, Leves, SRanks, Salvage, ChocoboGarden, Progress, RecipeBooks, PvP, Variant, Cosmic, Sightseeing, HuntingLog, Dyes, Patch, Mentor, SideStories, Mounts }

public sealed class MainWindow : Window
{
    private readonly HomeTab home;
    private readonly StoryTab story;
    private readonly SearchTab search;
    private readonly InventoryTab inventory;
    private readonly DutiesTab duties;
    private readonly QuestsTab quests;
    private readonly ItemsTab items;
    private readonly GearTab gear;
    private readonly ChecklistTab checklist = new();
    private readonly CraftTab craft;
    private readonly HuntTab hunt;
    private readonly CollectionsTab collections;
    private readonly SettingsTab settings = new();
    private readonly LevelingTab leveling;
    private readonly FateTab fates = new();
    private readonly GatherTab gather;
    private readonly RecapTab recap = new();
    private readonly DeepDungeonTab deep = new();
    private readonly TreasureTab treasure = new();
    private readonly BuildTab build;
    private readonly DiaryTab diary = new();
    private readonly AchievementsTab achievements = new();
    private MainTab current = Plugin.Config.StartTab;

    /// <summary>Раздел меню и его вкладки.</summary>
    internal sealed record Hub(string Name, FontAwesomeIcon Icon, (MainTab Tab, FontAwesomeIcon Icon, string Name)[] Pages);

    internal static readonly Hub[] Hubs =
    {
        new("Обзор", FontAwesomeIcon.Compass, new[] { (MainTab.Home, FontAwesomeIcon.Compass, "Обзор"), (MainTab.Progress, FontAwesomeIcon.ChartPie, "Прогресс аккаунта"), (MainTab.Patch, FontAwesomeIcon.Bullhorn, "Что нового в патче"), (MainTab.Mentor, FontAwesomeIcon.Crown, "Ментор") }),
        new("Поиск", FontAwesomeIcon.Search, new[] { (MainTab.Search, FontAwesomeIcon.Search, "Поиск") }),
        new("Сюжет и квесты", FontAwesomeIcon.Book, new[]
        {
            (MainTab.Story, FontAwesomeIcon.Book, "Сюжет"),
            (MainTab.Quests, FontAwesomeIcon.Scroll, "Квесты"),
            (MainTab.SideStories, FontAwesomeIcon.BookOpen, "Побочные сюжеты"),
        }),
        new("Бои", FontAwesomeIcon.Dungeon, new[]
        {
            (MainTab.Duties, FontAwesomeIcon.Dungeon, "Данжи и бои"),
            (MainTab.Recap, FontAwesomeIcon.Skull, "Разбор боёв"),
            (MainTab.Deep, FontAwesomeIcon.Dragon, "Deep Dungeons"),
            (MainTab.Relics, FontAwesomeIcon.Khanda, "Реликвии, Eureka, Bozja"),
            (MainTab.Occult, FontAwesomeIcon.HatWizard, "Occult Crescent"),
            (MainTab.Variant, FontAwesomeIcon.Route, "Variant и Criterion"),
            (MainTab.PvP, FontAwesomeIcon.FistRaised, "PvP"),
        }),
        new("Вики", FontAwesomeIcon.BookOpen, new[]
        {
            (MainTab.Items, FontAwesomeIcon.Gem, "Предметы"),
            (MainTab.Bestiary, FontAwesomeIcon.Skull, "Бестиарий"),
            (MainTab.Wiki, FontAwesomeIcon.BookOpen, "Зоны, NPC, рыбалка, избранное"),
            (MainTab.Furniture, FontAwesomeIcon.Couch, "Мебель"),
            (MainTab.Jobs, FontAwesomeIcon.UserGraduate, "Профессии"),
            (MainTab.Statuses, FontAwesomeIcon.Magic, "Статусы"),
            (MainTab.Salvage, FontAwesomeIcon.Recycle, "Разборка и сундуки"),
            (MainTab.Dyes, FontAwesomeIcon.PaintBrush, "Краски"),
        }),
        new("Активности", FontAwesomeIcon.Fire, new[]
        {
            (MainTab.Fates, FontAwesomeIcon.Fire, "FATE и атэриты"),
            (MainTab.Hunt, FontAwesomeIcon.Crosshairs, "Hunt-марки"),
            (MainTab.SRanks, FontAwesomeIcon.Skull, "S-ранги"),
            (MainTab.HuntingLog, FontAwesomeIcon.Book, "Hunting Log"),
            (MainTab.Sightseeing, FontAwesomeIcon.Binoculars, "Виды (Sightseeing)"),
            (MainTab.Leves, FontAwesomeIcon.ClipboardList, "Левы"),
            (MainTab.Treasure, FontAwesomeIcon.Gift, "Карты сокровищ"),
            (MainTab.Ocean, FontAwesomeIcon.Water, "Океан"),
            (MainTab.GoldSaucer, FontAwesomeIcon.Dice, "Gold Saucer"),
            (MainTab.Island, FontAwesomeIcon.Umbrella, "Остров"),
            (MainTab.ChocoboGarden, FontAwesomeIcon.Seedling, "Чокобо и сад"),
            (MainTab.Tribes, FontAwesomeIcon.Handshake, "Племена"),
            (MainTab.Weekly, FontAwesomeIcon.CalendarCheck, "Рейды, события, поставки"),
        }),
        new("Персонаж", FontAwesomeIcon.ShieldAlt, new[]
        {
            (MainTab.Gear, FontAwesomeIcon.ShieldAlt, "Экипировка"),
            (MainTab.Bis, FontAwesomeIcon.Star, "BiS-сеты"),
            (MainTab.Build, FontAwesomeIcon.Flask, "Материя и еда"),
            (MainTab.Leveling, FontAwesomeIcon.ArrowCircleUp, "Прокачка"),
            (MainTab.Achievements, FontAwesomeIcon.Trophy, "Достижения"),
            (MainTab.Diary, FontAwesomeIcon.ChartLine, "Дневник"),
            (MainTab.Mounts, FontAwesomeIcon.Horse, "Маунты"),
            (MainTab.Collections, FontAwesomeIcon.Paw, "Коллекции"),
            (MainTab.MountFarm, FontAwesomeIcon.Dungeon, "Фарм коллекций"),
            (MainTab.Unlocks, FontAwesomeIcon.Unlock, "Разблокировки"),
            (MainTab.Glamour, FontAwesomeIcon.Tshirt, "Гламур"),
        }),
        new("Экономика", FontAwesomeIcon.Coins, new[]
        {
            (MainTab.Inventory, FontAwesomeIcon.BoxOpen, "Инвентарь"),
            (MainTab.Currencies, FontAwesomeIcon.Coins, "Валюты"),
            (MainTab.Shopping, FontAwesomeIcon.ShoppingCart, "Покупки"),
            (MainTab.Craft, FontAwesomeIcon.Hammer, "Крафт"),
            (MainTab.RecipeBooks, FontAwesomeIcon.BookOpen, "Книги рецептов"),
            (MainTab.Cosmic, FontAwesomeIcon.Rocket, "Cosmic Exploration"),
            (MainTab.Gather, FontAwesomeIcon.Leaf, "Сбор"),
            (MainTab.CollectableItems, FontAwesomeIcon.Box, "Коллекционные"),
            (MainTab.Voyages, FontAwesomeIcon.Ship, "Субмарины"),
            (MainTab.Ventures, FontAwesomeIcon.UserTie, "Вылазки ретейнеров"),
        }),
        new("Время", FontAwesomeIcon.Clock, new[]
        {
            (MainTab.Checklist, FontAwesomeIcon.Tasks, "Дейлики и викли"),
            (MainTab.Timers, FontAwesomeIcon.Stopwatch, "Таймеры"),
            (MainTab.Weather, FontAwesomeIcon.CloudSun, "Погода"),
        }),
        new("Настройки", FontAwesomeIcon.Cog, new[] { (MainTab.Settings, FontAwesomeIcon.Cog, "Настройки") }),
    };

    /// <summary>Все вкладки плоским списком (для поиска разделов и закрепления).</summary>
    internal static IEnumerable<(MainTab Tab, FontAwesomeIcon Icon, string Name, string Hub)> AllPages =>
        Hubs.SelectMany(h => h.Pages.Select(p => (p.Tab, p.Icon, p.Name, h.Name)));

    private static int HubOf(MainTab t) => Array.FindIndex(Hubs, h => h.Pages.Any(p => p.Tab == t));

    // Последняя открытая вкладка каждого раздела
    private readonly Dictionary<int, MainTab> lastPage = new();

    public MainWindow(GuideData data, MarketService market, HuntTracker hunts) : base("FFXIV Guide###FFXIVGuideMain", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoTitleBar)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(820, 560),
            MaximumSize = new Vector2(4000, 4000),
        };
        Size = new Vector2(1060, 680);
        SizeCondition = ImGuiCond.FirstUseEver;

        quests = new QuestsTab(data);
        duties = new DutiesTab(data, quests, Go);
        items = new ItemsTab(data, market);
        gear = new GearTab(data, market);
        home = new HomeTab(data, quests, duties, Go);
        shopping = new ShoppingTab(market);
        relics = new RelicTab(data);
        weekly = new WeeklyTab(data);
        mountFarm = new MountFarmTab(data);
        mounts = new MountsTab(data, market);
        furniture = new FurnitureTab(market);
        ventures = new VenturesTab(data, market);
        occult = new OccultTab();
        currencies = new CurrenciesTab(market);
        statuses = new StatusesTab();
        jobsTab = new JobsTab(data);
        leves = new LevesTab(data);
        sranks = new SRanksTab();
        salvage = new SalvageTab();
        chocoGarden = new ChocoboGardenTab();
        progress = new ProgressTab(data, Go);
        patch = new PatchTab();
        mentor = new MentorTab();
        recipeBooks = new RecipeBooksTab(market);
        pvp = new PvPTab();
        variant = new VariantTab(data);
        cosmic = new CosmicTab(data);
        sightseeing = new SightseeingTab();
        huntingLog = new HuntingLogTab();
        dyes = new DyesTab(market);
        sideStories = new SideStoryTab();
        story = new StoryTab(data, quests, duties);
        search = new SearchTab(data, duties, quests, items, Go);
        search.Providers.Add(t => statuses.Search(t, Go));
        search.Providers.Add(t => jobsTab.Search(t, Go));
        search.Providers.Add(t => leves.Search(t, Go));
        search.Providers.Add(t => sranks.Search(t, Go));
        search.Providers.Add(t => chocoGarden.Search(t, Go));
        search.Providers.Add(t => variant.Search(t, Go));
        search.Providers.Add(t => cosmic.Search(t, Go));
        search.Providers.Add(t => currencies.Search(t, Go));
        search.Providers.Add(t => sightseeing.Search(t, Go));
        search.Providers.Add(t => huntingLog.Search(t, Go));
        search.Providers.Add(t => dyes.Search(t, Go));
        search.Providers.Add(t => sideStories.Search(t, Go));
        search.Providers.Add(t => mounts.Search(t, Go));
        inventory = new InventoryTab(market);
        craft = new CraftTab(market);
        hunt = new HuntTab(hunts);
        collections = new CollectionsTab(data);
        leveling = new LevelingTab(data, quests, duties);
        gather = new GatherTab(market);
        build = new BuildTab(market);
        bestiary = new BestiaryTab(data);
        search.Bestiary = bestiary;

        wiki = new WikiTab(data, quests);

        // Перекрёстные ссылки вики: любое имя открывает свою карточку
        WikiView.Data = data;
        WikiNav.Navigate = link =>
        {
            IsOpen = true;
            switch (link.Kind)
            {
                case WikiKind.Item:
                    items.Show((uint)link.Id, WikiView.ItemInfo((uint)link.Id).Name);
                    current = MainTab.Items;
                    break;
                case WikiKind.Mob:
                    bestiary.Show(link.Id);
                    current = MainTab.Bestiary;
                    break;
                case WikiKind.Duty:
                    duties.ShowByCfc((uint)link.Id);
                    break;
                case WikiKind.Zone:
                    wiki.ShowZone((uint)link.Id);
                    current = MainTab.Wiki;
                    break;
                case WikiKind.Npc:
                    wiki.ShowNpc((uint)link.Id);
                    current = MainTab.Wiki;
                    break;
                case WikiKind.Quest:
                    if (data.Quests.TryGetValue((uint)link.Id, out var q)) quests.Show(q);
                    break;
            }
        };
    }

    public void OpenWiki(int section)
    {
        if (section == 3) wiki.ShowFishing(); else if (section == 4) wiki.ShowBlue();
        OpenTab(MainTab.Wiki);
    }

    public void OpenTab(MainTab tab)
    {
        current = tab;
        IsOpen = true;
    }

    private void Go(MainTab tab) => current = tab;

    /// <summary>Открыть поиск с готовым запросом (например, из чата).</summary>
    private readonly BestiaryTab bestiary;
    private readonly WikiTab wiki;
    private readonly WeatherTab weather = new();
    private readonly TimersTab timers = new();
    private readonly GoldSaucerTab goldSaucer = new();
    private readonly TribesTab tribes = new();
    private readonly GlamourTab glamour = new();
    private readonly ShoppingTab shopping;
    private readonly VoyagesTab voyages = new();
    private readonly IslandTab island = new();
    private readonly CollectablesTab collectables = new();
    private readonly RelicTab relics;
    private readonly OceanTab ocean = new();
    private readonly UnlocksTab unlocks = new();
    private readonly WeeklyTab weekly;
    private readonly BisTab bis = new();
    private readonly MountFarmTab mountFarm;
    private readonly MountsTab mounts;
    private readonly FurnitureTab furniture;
    private readonly VenturesTab ventures;
    private readonly OccultTab occult;
    private readonly CurrenciesTab currencies;
    private readonly StatusesTab statuses;
    private readonly JobsTab jobsTab;
    private readonly LevesTab leves;
    private readonly SRanksTab sranks;
    private readonly SalvageTab salvage;
    private readonly ChocoboGardenTab chocoGarden;
    private readonly ProgressTab progress;
    private readonly PatchTab patch;
    private readonly MentorTab mentor;
    private readonly RecipeBooksTab recipeBooks;
    private readonly PvPTab pvp;
    private readonly VariantTab variant;
    private readonly CosmicTab cosmic;
    private readonly SightseeingTab sightseeing;
    private readonly HuntingLogTab huntingLog;
    private readonly DyesTab dyes;
    private readonly SideStoryTab sideStories;
    private string headerSearch = "";
    private bool focusHeaderSearch;

    /// <summary>Своя шапка: логотип, поиск по всему справочнику и кнопки окна.</summary>
    private void DrawTitleBar()
    {
        var S = Theme.S;
        var dl = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var w = ImGui.GetContentRegionAvail().X;
        var h = 40 * S;

        // Логотип
        var c = start + new Vector2(18 * S, h / 2);
        dl.AddCircleFilled(c, 15 * S, Theme.U32(Theme.Alpha(Theme.Gold, 0.18f)));
        dl.AddCircle(c, 15 * S, Theme.U32(Theme.Gold), 0, 1.5f * S);
        ImGui.PushFont(UiBuilder.IconFont);
        var logo = FontAwesomeIcon.Compass.ToIconString();
        var ls = ImGui.CalcTextSize(logo);
        dl.AddText(c - ls / 2, Theme.U32(Theme.Gold), logo);
        ImGui.PopFont();
        var title = "FFXIV Guide";
        dl.AddText(start + new Vector2(42 * S, (h - ImGui.GetTextLineHeight()) / 2), Theme.U32(Theme.Text), title);
        var titleW = 42 * S + ImGui.CalcTextSize(title).X + 16 * S;

        // Назад / вперёд по вики и звёздочка «в избранное» для текущей карточки
        ImGui.SetCursorScreenPos(start + new Vector2(titleW, (h - 26 * S) / 2));
        ImGui.BeginDisabled(!WikiNav.CanBack);
        if (TitleButton("back", FontAwesomeIcon.ArrowLeft, "Назад по вики")) WikiNav.Back();
        ImGui.EndDisabled();
        ImGui.SameLine(0, 2 * S);
        ImGui.BeginDisabled(!WikiNav.CanForward);
        if (TitleButton("fwd", FontAwesomeIcon.ArrowRight, "Вперёд по вики")) WikiNav.Forward();
        ImGui.EndDisabled();
        ImGui.SameLine(0, 2 * S);
        var cur = WikiNav.Current;
        ImGui.BeginDisabled(cur is null);
        var fav = cur is not null && WikiNav.IsFavorite(cur);
        if (TitleButton("star", fav ? FontAwesomeIcon.Star : FontAwesomeIcon.StarHalfAlt,
                cur is null ? "Открой карточку, чтобы добавить в избранное" : fav ? $"Убрать «{cur.Name}» из избранного" : $"В избранное: {cur.Name}"))
            WikiNav.ToggleFavorite(cur!);
        ImGui.EndDisabled();
        titleW += 3 * 28 * S + 14 * S;

        // Поиск в шапке: печатаешь — сразу открывается раздел «Поиск»
        var buttonsW = 3 * 30 * S + 10 * S;
        var searchW = Math.Min(w - titleW - buttonsW, 520 * S);
        ImGui.SetCursorScreenPos(start + new Vector2(titleW, (h - ImGui.GetFrameHeight()) / 2));
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(Theme.TextDim, FontAwesomeIcon.Search.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        if (focusHeaderSearch)
        {
            ImGui.SetKeyboardFocusHere();
            focusHeaderSearch = false;
        }
        ImGui.SetNextItemWidth(searchW - 26 * S);
        if (ImGui.InputTextWithHint("##hdrsearch", "Поиск: данж, квест, предмет… (можно по-русски)", ref headerSearch, 128))
        {
            search.SetQuery(headerSearch, focus: false);
            if (headerSearch.Trim().Length > 0) current = MainTab.Search;
        }

        // Кнопки справа: мини-окно, настройки, закрыть
        var bx = start.X + w - buttonsW + 10 * S;
        ImGui.SetCursorScreenPos(new Vector2(bx, start.Y + (h - 26 * S) / 2));
        if (TitleButton("mini", FontAwesomeIcon.Compass, "Мини-окно")) Plugin.SetMiniWindow(!Plugin.Config.MiniWindow);
        ImGui.SameLine(0, 4 * S);
        if (TitleButton("cfg", FontAwesomeIcon.Cog, "Настройки")) current = MainTab.Settings;
        ImGui.SameLine(0, 4 * S);
        if (TitleButton("close", FontAwesomeIcon.Times, "Закрыть (Ctrl+Shift+G — открыть снова)")) IsOpen = false;

        // Разделитель
        var y = start.Y + h + 4 * S;
        dl.AddLine(new Vector2(start.X, y), new Vector2(start.X + w, y), Theme.U32(Theme.Border));
        ImGui.SetCursorScreenPos(new Vector2(start.X, y + 8 * S));
        ImGui.Dummy(Vector2.Zero);
    }

    private static bool TitleButton(string id, FontAwesomeIcon icon, string tip)
    {
        var S = Theme.S;
        var size = new Vector2(26 * S);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##tb{id}", size);
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        if (hovered)
            dl.AddRectFilled(pos, pos + size, Theme.U32(id == "close" ? Theme.Alpha(Theme.Red, 0.35f) : Theme.CardHover), 6f * S);
        ImGui.PushFont(UiBuilder.IconFont);
        var s = icon.ToIconString();
        var ts = ImGui.CalcTextSize(s);
        dl.AddText(pos + (size - ts) / 2, Theme.U32(hovered ? Theme.Text : Theme.TextDim), s);
        ImGui.PopFont();
        if (hovered) ImGui.SetTooltip(tip);
        return clicked;
    }

    public void OpenSearch(string text)
    {
        search.SetQuery(text);
        OpenTab(MainTab.Search);
    }

    public override void PreDraw() => Theme.Push();
    public override void PostDraw() => Theme.Pop();

    public override void Draw()
    {
        // Переход во вкладку «Квесты» из других разделов: флаг гасим только здесь,
        // в начале следующего кадра, иначе переключение терялось.
        if (quests.RequestFocus)
        {
            current = MainTab.Quests;
            quests.EndFrame();
        }

        DrawTitleBar();

        DrawSidebar();
        ImGui.SameLine(0, 12 * Theme.S);

        foreach (var n in AllPages)
            if (n.Tab == current) Theme.CurrentIcon = n.Icon;

        if (ImGui.BeginChild("##content", Vector2.Zero, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            HandleHotkeys();
            if (!Plugin.Config.SetupDone)
            {
                DrawSetup();
            }
            else
            {
                DrawPageTabs();
                DrawPageSafe();
            }
        }
        ImGui.EndChild();
    }

    private void DrawSidebar()
    {
        var S = Theme.S;
        var compact = Plugin.Config.CompactSidebar || ImGui.GetWindowWidth() < 880 * Theme.S;
        if (!ImGui.BeginChild("##sidebar", new Vector2((compact ? 52 : 210) * S, 0), false, ImGuiWindowFlags.None))
        {
            ImGui.EndChild();
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var wp = ImGui.GetWindowPos();
        var ws = ImGui.GetWindowSize();
        dl.AddRectFilled(wp, wp + ws, Theme.U32(Theme.PanelBg), 10f * S);

        // Персонаж
        ImGui.SetCursorPos(new Vector2(compact ? 6 : 12, 12) * S);
        if (PlayerInfo.Ready && compact && Plugin.PlayerState.ClassJob.ValueNullable is { } cjob)
        {
            var cpos = ImGui.GetCursorScreenPos();
            Theme.GameIcon(62100 + cjob.RowId, cpos, 40 * S, 20f);
            ImGui.Dummy(new Vector2(40 * S));
            Theme.Tooltip($"{Plugin.PlayerState.CharacterName} · {GuideData.Capitalize(cjob.Name.ExtractText())} {Plugin.PlayerState.Level}");
        }
        else if (PlayerInfo.Ready && Plugin.PlayerState.ClassJob.ValueNullable is { } job)
        {
            DrawCharacterCard(job);
        }
        else if (!compact)
        {
            ImGui.TextColored(Theme.TextDim, "Персонаж не загружен");
        }

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 10 * S);
        var lp = ImGui.GetCursorScreenPos();
        dl.AddLine(lp + new Vector2(12 * S, 0), lp + new Vector2(ws.X - 12 * S, 0), Theme.U32(Theme.Border));
        ImGui.Dummy(new Vector2(0, 8 * S));

        // Закреплённые вкладки
        var pinned = Plugin.Config.PinnedTabs.Select(t => AllPages.FirstOrDefault(p => (int)p.Tab == t)).Where(p => p.Name is not null).ToList();
        if (pinned.Count > 0)
        {
            NavGroup("Закреплённые", compact);
            foreach (var p in pinned)
                if (NavButton($"pin{p.Tab}", p.Icon, p.Name, current == p.Tab, compact)) current = p.Tab;
            ImGui.Dummy(new Vector2(0, 4 * S));
            if (!compact) NavGroup("Разделы", compact);
        }

        var activeHub = HubOf(current);
        for (var i = 0; i < Hubs.Length; i++)
        {
            var h = Hubs[i];
            if (NavButton($"hub{i}", h.Icon, h.Name, i == activeHub, compact))
                current = lastPage.TryGetValue(i, out var last) ? last : h.Pages[0].Tab;
        }

        // Переключатель компактного меню
        ImGui.Dummy(new Vector2(0, 4 * S));
        NavToggle(compact);

        // Низ панели
        if (!compact)
        {
            ImGui.SetCursorPos(new Vector2(14 * S, System.Math.Max(ImGui.GetCursorPosY() + 8 * S, ws.Y - 44 * S)));
            ImGui.TextColored(Theme.Gray, "/fguide · Ctrl+Shift+G");
            ImGui.SetCursorPosX(14 * S);
            ImGui.TextColored(Theme.Gray, "Только информация · v2.18");
        }

        ImGui.EndChild();
    }

    private static void NavGroup(string name, bool compact)
    {
        var S = Theme.S;
        ImGui.Dummy(new Vector2(0, 6 * S));
        if (compact)
        {
            var p = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddLine(p + new Vector2(12 * S, 0), p + new Vector2(ImGui.GetWindowWidth() - 12 * S, 0), Theme.U32(Theme.Border));
            ImGui.Dummy(new Vector2(0, 4 * S));
            return;
        }
        ImGui.SetCursorPosX(16 * S);
        ImGui.TextColored(Theme.Gray, name.ToUpperInvariant());
    }

    /// <summary>Карточка персонажа: иконка профессии в кольце, имя, уровень, средний iLvl, опыт.</summary>
    private static unsafe void DrawCharacterCard(Lumina.Excel.Sheets.ClassJob job)
    {
        var S = Theme.S;
        var dl = ImGui.GetWindowDrawList();
        var w = ImGui.GetWindowWidth() - 16 * S;
        var start = new Vector2(ImGui.GetWindowPos().X + 8 * S, ImGui.GetCursorScreenPos().Y);
        var height = 96 * S;

        // Фон карточки: градиент от золотого к фону панели
        dl.AddRectFilledMultiColor(start, start + new Vector2(w, height),
            Theme.U32(Theme.Alpha(Theme.Gold, 0.20f)), Theme.U32(Theme.Alpha(Theme.Gold, 0.04f)),
            Theme.U32(Theme.Alpha(Theme.Gold, 0.02f)), Theme.U32(Theme.Alpha(Theme.Gold, 0.10f)));
        dl.AddRect(start, start + new Vector2(w, height), Theme.U32(Theme.Alpha(Theme.Gold, 0.35f)), 8f * S);

        // Иконка профессии в золотом кольце
        var iconSize = 44 * S;
        var ip = start + new Vector2(10 * S, 10 * S);
        dl.AddCircleFilled(ip + new Vector2(iconSize / 2), iconSize / 2 + 3 * S, Theme.U32(Theme.Alpha(Theme.Gold, 0.6f)));
        Theme.GameIcon(62100 + job.RowId, ip, iconSize, 22f);

        // Имя и профессия
        var tx = ip.X + iconSize + 10 * S;
        var level = Plugin.PlayerState.Level;
        // Текст обрезается по правому краю карточки, чтобы длинное имя не вылезало
        var lineH = ImGui.GetTextLineHeight();
        var clip = new Vector4(tx, start.Y, start.X + w - 8 * S, start.Y + height);
        var font = ImGui.GetFont();
        var fs = ImGui.GetFontSize();
        dl.AddText(font, fs, new Vector2(tx, ip.Y), Theme.U32(Theme.Text), Plugin.PlayerState.CharacterName, 0f, ref clip);
        dl.AddText(font, fs, new Vector2(tx, ip.Y + lineH), Theme.U32(Theme.Gold),
            $"{GuideData.Capitalize(job.Name.ExtractText())} · {level}", 0f, ref clip);

        // Средний уровень предметов — третьей строкой, под профессией
        var ilvl = AverageItemLevel();
        if (ilvl > 0)
            Theme.Badge(new Vector2(tx, ip.Y + lineH * 2 + 3 * S), $"iLvl {ilvl}", Theme.Blue);

        // Полоса опыта до следующего уровня
        var barY = start.Y + height - 14 * S;
        var expNow = Plugin.PlayerState.GetClassJobExperience(job);
        var grow = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.ParamGrow>().GetRowOrDefault((uint)level);
        var expMax = grow?.ExpToNext ?? 0;
        var frac = expMax > 0 ? System.Math.Clamp((float)expNow / expMax, 0f, 1f) : 1f;
        var bx = start.X + 10 * S;
        var bw = w - 20 * S;
        dl.AddRectFilled(new Vector2(bx, barY), new Vector2(bx + bw, barY + 6 * S), Theme.U32(Theme.CardBg), 3f * S);
        if (frac > 0)
            dl.AddRectFilled(new Vector2(bx, barY), new Vector2(bx + System.Math.Max(bw * frac, 6 * S), barY + 6 * S), Theme.U32(Theme.Gold), 3f * S);

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, start.Y));
        ImGui.InvisibleButton("##charcard", new Vector2(w, height));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(expMax > 0 ? $"Опыт: {expNow:N0} / {expMax:N0} ({frac * 100:0}%)" : "Максимальный уровень");
    }

    private static unsafe int AverageItemLevel()
    {
        var im = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
        if (im == null) return 0;
        var eq = im->GetInventoryContainer(FFXIVClientStructs.FFXIV.Client.Game.InventoryType.EquippedItems);
        if (eq == null) return 0;
        var items = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
        int sum = 0, count = 0;
        uint mainIlvl = 0;
        var twoHanded = false;
        for (var i = 0; i < eq->Size; i++)
        {
            if (i == 5 || i == 13) continue; // пояс (пустой) и кристалл души
            var s = eq->GetInventorySlot(i);
            var row = s == null || s->GetBaseItemId() == 0 ? null : items.GetRowOrDefault(s->GetBaseItemId());
            if (i == 0 && row is not null)
            {
                mainIlvl = row.Value.LevelItem.RowId;
                twoHanded = row.Value.EquipSlotCategory.ValueNullable?.OffHand == -1;
            }
            if (i == 1 && twoHanded) { sum += (int)mainIlvl; count++; continue; } // двуручное считается за два слота
            sum += (int)(row?.LevelItem.RowId ?? 0);
            count++;
        }
        return count == 0 ? 0 : sum / count;
    }

    private static void NavToggle(bool compact)
    {
        var S = Theme.S;
        ImGui.SetCursorPosX(8 * S);
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImGui.GetWindowWidth() - 16 * S, 26 * S);
        if (ImGui.InvisibleButton("##navtoggle", size))
        {
            Plugin.Config.CompactSidebar = !compact;
            Plugin.Config.Save();
        }
        var hovered = ImGui.IsItemHovered();
        var color = hovered ? Theme.Text : Theme.Gray;
        ImGui.PushFont(UiBuilder.IconFont);
        var iconStr = (compact ? FontAwesomeIcon.ArrowRight : FontAwesomeIcon.ArrowLeft).ToIconString();
        var iconSize = ImGui.CalcTextSize(iconStr);
        ImGui.GetWindowDrawList().AddText(pos + new Vector2(14 * S + (18 * S - iconSize.X) / 2, (size.Y - iconSize.Y) / 2), Theme.U32(color), iconStr);
        ImGui.PopFont();
        if (!compact)
            ImGui.GetWindowDrawList().AddText(pos + new Vector2(42 * S, (size.Y - ImGui.GetTextLineHeight()) / 2), Theme.U32(color), "Свернуть меню");
        else if (hovered)
            ImGui.SetTooltip("Развернуть меню");
    }

    private static bool NavButton(string id, FontAwesomeIcon icon, string name, bool active, bool compact)
    {
        var S = Theme.S;
        ImGui.SetCursorPosX(8 * S);
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImGui.GetWindowWidth() - 16 * S, 33 * S);
        var clicked = ImGui.InvisibleButton($"##nb{id}", size);
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        if (active || hovered)
            dl.AddRectFilled(pos, pos + size, Theme.U32(active ? Theme.Alpha(Theme.Gold, 0.15f) : Theme.CardHover), 6f * S);
        if (active)
            dl.AddRectFilled(pos + new Vector2(0, 7 * S), pos + new Vector2(3 * S, size.Y - 7 * S), Theme.U32(Theme.Gold), 2f * S);
        var color = active ? Theme.Gold : hovered ? Theme.Text : Theme.TextDim;
        ImGui.PushFont(UiBuilder.IconFont);
        var iconStr = icon.ToIconString();
        var iconSize = ImGui.CalcTextSize(iconStr);
        dl.AddText(pos + new Vector2(14 * S + (18 * S - iconSize.X) / 2, (size.Y - iconSize.Y) / 2), Theme.U32(color), iconStr);
        ImGui.PopFont();
        if (compact) { if (hovered) ImGui.SetTooltip(name); }
        else
        {
            var ts = ImGui.CalcTextSize(name);
            dl.AddText(pos + new Vector2(42 * S, (size.Y - ts.Y) / 2), Theme.U32(color), name);
        }
        ImGui.Dummy(new Vector2(0, 2 * S));
        return clicked;
    }

    /// <summary>Вкладки раздела сверху. Правый клик — закрепить вкладку в меню.</summary>
    private void DrawPageTabs()
    {
        var hi = HubOf(current);
        if (hi < 0) return;
        lastPage[hi] = current;
        var hub = Hubs[hi];
        if (hub.Pages.Length < 2) return;
        var S = Theme.S;
        var dl = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var right = start.X + ImGui.GetContentRegionAvail().X;
        var x = start.X;
        var y = start.Y;
        var h = 30 * S;
        foreach (var (tab, icon, name) in hub.Pages)
        {
            ImGui.PushFont(UiBuilder.IconFont);
            var iconStr = icon.ToIconString();
            var isz = ImGui.CalcTextSize(iconStr);
            ImGui.PopFont();
            var tsz = ImGui.CalcTextSize(name);
            var w = 12 * S + isz.X + 6 * S + tsz.X + 12 * S;
            if (x + w > right && x > start.X) { x = start.X; y += h + 4 * S; }
            var pos = new Vector2(x, y);
            ImGui.SetCursorScreenPos(pos);
            if (ImGui.InvisibleButton($"##pt{tab}", new Vector2(w, h))) current = tab;
            var hovered = ImGui.IsItemHovered();
            var active = current == tab;
            if (ImGui.BeginPopupContextItem($"##ptc{tab}"))
            {
                var pinnedNow = Plugin.Config.PinnedTabs.Contains((int)tab);
                if (ImGui.Selectable(pinnedNow ? "Открепить из меню" : "Закрепить в меню"))
                {
                    if (pinnedNow) Plugin.Config.PinnedTabs.Remove((int)tab); else Plugin.Config.PinnedTabs.Add((int)tab);
                    Plugin.Config.Save();
                }
                ImGui.EndPopup();
            }
            if (hovered && !active) ImGui.SetTooltip("Правый клик — закрепить в меню");
            dl.AddRectFilled(pos, pos + new Vector2(w, h), Theme.U32(active ? Theme.Alpha(Theme.Gold, 0.18f) : hovered ? Theme.CardHover : Theme.CardBg), 8f * S);
            if (active) dl.AddRectFilled(new Vector2(pos.X + 8 * S, pos.Y + h - 3 * S), new Vector2(pos.X + w - 8 * S, pos.Y + h), Theme.U32(Theme.Gold), 2f * S);
            var col = active ? Theme.Gold : hovered ? Theme.Text : Theme.TextDim;
            ImGui.PushFont(UiBuilder.IconFont);
            dl.AddText(new Vector2(pos.X + 12 * S, pos.Y + (h - isz.Y) / 2), Theme.U32(col), iconStr);
            ImGui.PopFont();
            dl.AddText(new Vector2(pos.X + 18 * S + isz.X, pos.Y + (h - tsz.Y) / 2), Theme.U32(col), name);
            x += w + 6 * S;
        }
        ImGui.SetCursorScreenPos(new Vector2(start.X, y + h + 8 * S));
        ImGui.Dummy(Vector2.Zero);
    }

    /// <summary>Вкладка без своей прокрутки: оборачиваем, чтобы на маленьком окне всё было доступно.</summary>
    private static void Scrolled(string id, Action draw)
    {
        if (ImGui.BeginChild("##scroll_" + id, Vector2.Zero)) draw();
        ImGui.EndChild();
    }

    private void DrawPage()
    {
        switch (current)
        {
            case MainTab.Home: home.Draw(); break;
            case MainTab.Story: story.Draw(); break;
            case MainTab.Search: search.Draw(); break;
            case MainTab.Duties: duties.Draw(); break;
            case MainTab.Quests: quests.Draw(); break;
            case MainTab.Items: items.Draw(); break;
            case MainTab.Gear: gear.Draw(); break;
            case MainTab.Inventory: inventory.Draw(); break;
            case MainTab.Checklist: checklist.Draw(); break;
            case MainTab.Craft: craft.Draw(); break;
            case MainTab.Hunt: Scrolled("hunt", hunt.Draw); break;
            case MainTab.Collections: collections.Draw(); break;
            case MainTab.Settings: settings.Draw(); break;
            case MainTab.Leveling: leveling.Draw(); break;
            case MainTab.Fates: fates.Draw(); break;
            case MainTab.Gather: gather.Draw(); break;
            case MainTab.Recap: recap.Draw(); break;
            case MainTab.Deep: deep.Draw(); break;
            case MainTab.Treasure: treasure.Draw(); break;
            case MainTab.Build: build.Draw(); break;
            case MainTab.Diary: diary.Draw(); break;
            case MainTab.Achievements: achievements.Draw(); break;
            case MainTab.Bestiary: bestiary.Draw(); break;
            case MainTab.Wiki: wiki.Draw(); break;
            case MainTab.Weather: weather.Draw(); break;
            case MainTab.Timers: timers.Draw(); break;
            case MainTab.GoldSaucer: goldSaucer.Draw(); break;
            case MainTab.Tribes: tribes.Draw(); break;
            case MainTab.Glamour: glamour.Draw(); break;
            case MainTab.Shopping: shopping.Draw(); break;
            case MainTab.Voyages: voyages.Draw(); break;
            case MainTab.Island: island.Draw(); break;
            case MainTab.CollectableItems: collectables.Draw(); break;
            case MainTab.Relics: relics.Draw(); break;
            case MainTab.Ocean: ocean.Draw(); break;
            case MainTab.Unlocks: unlocks.Draw(); break;
            case MainTab.Weekly: weekly.Draw(); break;
            case MainTab.Bis: bis.Draw(); break;
            case MainTab.MountFarm: mountFarm.Draw(); break;
            case MainTab.Mounts: mounts.Draw(); break;
            case MainTab.Furniture: furniture.Draw(); break;
            case MainTab.Ventures: ventures.Draw(); break;
            case MainTab.Occult: occult.Draw(); break;
            case MainTab.Currencies: currencies.Draw(); break;
            case MainTab.Statuses: statuses.Draw(); break;
            case MainTab.Jobs: jobsTab.Draw(); break;
            case MainTab.Leves: leves.Draw(); break;
            case MainTab.SRanks: sranks.Draw(); break;
            case MainTab.Salvage: salvage.Draw(); break;
            case MainTab.ChocoboGarden: chocoGarden.Draw(); break;
            case MainTab.Progress: progress.Draw(); break;
            case MainTab.Patch: patch.Draw(); break;
            case MainTab.Mentor: mentor.Draw(); break;
            case MainTab.RecipeBooks: recipeBooks.Draw(); break;
            case MainTab.PvP: Scrolled("pvp", pvp.Draw); break;
            case MainTab.Variant: variant.Draw(); break;
            case MainTab.Cosmic: cosmic.Draw(); break;
            case MainTab.Sightseeing: sightseeing.Draw(); break;
            case MainTab.HuntingLog: huntingLog.Draw(); break;
            case MainTab.Dyes: dyes.Draw(); break;
            case MainTab.SideStories: sideStories.Draw(); break;
        }
    }

    // ───────────── Защита от падений ─────────────

    private readonly Dictionary<MainTab, string> pageErrors = new();

    /// <summary>Ошибка в одной вкладке не ломает окно: показываем сообщение и кнопку «скопировать лог».</summary>
    private unsafe void DrawPageSafe()
    {
        if (pageErrors.TryGetValue(current, out var err))
        {
            Theme.Panel("pageerr", () =>
            {
                Theme.FaIcon(FontAwesomeIcon.ExclamationTriangle, Theme.Red);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Red, "В этом разделе произошла ошибка. Остальные разделы работают.");
                ImGui.TextColored(Theme.TextDim, err.Split('\n')[0]);
                if (Theme.IconButton("errcopy", FontAwesomeIcon.Copy, "Скопировать лог")) ImGui.SetClipboardText(err);
                ImGui.SameLine();
                if (Theme.IconButton("errretry", FontAwesomeIcon.Redo, "Попробовать снова")) pageErrors.Remove(current);
            });
            return;
        }

        var window = ImGuiP.GetCurrentWindow();
        try
        {
            DrawPage();
        }
        catch (Exception e)
        {
            Plugin.Log.Error(e, $"Ошибка в разделе {current}");
            pageErrors[current] = $"{current}: {e.Message}\n{e}";
            // Восстанавливаем стек ImGui: закрываем недорисованные дочерние окна и группы
            try
            {
                var guard = 0;
                while (ImGuiP.GetCurrentWindow().Handle != window.Handle && guard++ < 32)
                {
                    ImGuiP.ErrorCheckEndWindowRecover(null);
                    ImGui.EndChild();
                }
                ImGuiP.ErrorCheckEndWindowRecover(null);
            }
            catch (Exception e2)
            {
                Plugin.Log.Warning(e2, "Не удалось восстановить стек ImGui");
            }
        }
    }

    // ───────────── Горячие клавиши ─────────────

    private void HandleHotkeys()
    {
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)) return;
        var io = ImGui.GetIO();
        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.F)) focusHeaderSearch = true;
        if (io.KeyAlt && ImGui.IsKeyPressed(ImGuiKey.LeftArrow)) WikiNav.Back();
        if (io.KeyAlt && ImGui.IsKeyPressed(ImGuiKey.RightArrow)) WikiNav.Forward();
        if (io.KeyCtrl && !io.WantTextInput)
        {
            var keys = new[] { ImGuiKey.Key1, ImGuiKey.Key2, ImGuiKey.Key3, ImGuiKey.Key4, ImGuiKey.Key5, ImGuiKey.Key6, ImGuiKey.Key7, ImGuiKey.Key8, ImGuiKey.Key9, ImGuiKey.Key0 };
            for (var i = 0; i < keys.Length && i < Hubs.Length; i++)
                if (ImGui.IsKeyPressed(keys[i]))
                    current = lastPage.TryGetValue(i, out var last) ? last : Hubs[i].Pages[0].Tab;
        }
    }

    // ───────────── Мастер первого запуска ─────────────

    private void DrawSetup()
    {
        var cfg = Plugin.Config;
        Theme.Header("Добро пожаловать", "Пара шагов — и справочник готов. Всё можно поменять потом в Настройках.");
        Theme.Panel("setup1", () =>
        {
            Theme.SubHeader("1. Офлайн-база (≈45 МБ)");
            ImGui.TextColored(Theme.TextDim, "Вики, бестиарий, источники предметов будут работать без сети и открываться мгновенно.");
            var db = OfflineDb.Instance;
            if (db.Busy) Theme.Progress(db.Status, (int)(db.ProgressValue * 100), 100, Theme.Gold);
            else if (db.Installed) ImGui.TextColored(Theme.Green, "Скачана ✓");
            else if (Theme.IconButton("setdb", FontAwesomeIcon.Download, "Скачать сейчас")) db.Update(false);
        });
        Theme.Panel("setup2", () =>
        {
            Theme.SubHeader("2. Русские названия");
            ImGui.TextColored(Theme.TextDim, "Машинный перевод названий в фоне (после загрузки базы начнётся сам). Как показывать:");
            ImGui.SetNextItemWidth(240 * Theme.S);
            var mode = cfg.NameMode;
            if (ImGui.Combo("##setnames", ref mode, "Английские\0Русские\0Русское (English)\0")) { cfg.NameMode = mode; cfg.Save(); }
        });
        Theme.Panel("setup3", () =>
        {
            Theme.SubHeader("3. Что открывать первым");
            ImGui.SetNextItemWidth(260 * Theme.S);
            var curName = AllPages.FirstOrDefault(p => p.Tab == cfg.StartTab).Name ?? "Обзор";
            if (ImGui.BeginCombo("##setstart", curName))
            {
                foreach (var pg in AllPages)
                    if (ImGui.Selectable($"{pg.Hub} → {pg.Name}", pg.Tab == cfg.StartTab)) { cfg.StartTab = pg.Tab; cfg.Save(); }
                ImGui.EndCombo();
            }
            var mini = cfg.MiniWindow;
            if (ImGui.Checkbox("Показывать мини-окно поверх игры (сюжет, дейлики, цель, таймеры)", ref mini)) Plugin.SetMiniWindow(mini);
        });
        Theme.Panel("setup4", () =>
        {
            Theme.SubHeader("4. Уведомления");
            ImGui.TextColored(Theme.TextDim, "Погода, возвращение ретейнеров и субмарин, снос дома, новые события. Куда сообщать:");
            var m = Notify.Mode(Notify.Kind.Retainer);
            ImGui.SetNextItemWidth(240 * Theme.S);
            if (ImGui.BeginCombo("##setnotify", Notify.Modes[m]))
            {
                for (var i = 0; i < Notify.Modes.Length; i++)
                    if (ImGui.Selectable(Notify.Modes[i], i == m))
                        foreach (var (k, _) in Notify.Kinds) Notify.SetMode(k, i);
                ImGui.EndCombo();
            }
        });
        Theme.Panel("setup5", () =>
        {
            ImGui.TextColored(Theme.TextDim, "Горячие клавиши в окне: Ctrl+F — поиск, Alt+←/→ — назад/вперёд, Ctrl+1…0 — разделы. Окно: Ctrl+Shift+G.");
            if (Theme.IconButton("setdone", FontAwesomeIcon.Check, "Готово"))
            {
                cfg.SetupDone = true;
                cfg.Save();
                current = cfg.StartTab;
            }
        });
    }
}
