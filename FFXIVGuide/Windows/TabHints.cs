using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace FFXIVGuide.Windows;

/// <summary>Однострочные подсказки «что тут можно делать» — показываются при первом открытии вкладки.</summary>
internal static class TabHints
{
    private static readonly Dictionary<MainTab, string> Hints = new()
    {
        [MainTab.Home] = "Главное о персонаже. Нажми «Изменить плитки»: плитки можно перетаскивать, расширять, убирать и добавлять.",
        [MainTab.Progress] = "Процент прохождения аккаунта по категориям. Нажми на строку — откроется нужный раздел.",
        [MainTab.Patch] = "Что появилось в игре с последнего патча: новые предметы, квесты, достижения и FATE.",
        [MainTab.Mentor] = "Сколько осталось до короны ментора: требования для боевого и торгового ментора.",
        [MainTab.Search] = "Ищи всё сразу: данжи, квесты, предметы, мобов, разделы плагина. Можно по-русски. Быстрый вызов — Ctrl+K.",
        [MainTab.Story] = "Текущий сюжетный квест с флагом на карте, главы и что откроется дальше.",
        [MainTab.Quests] = "Все квесты-анлоки: фильтры, цепочки предпосылок, награды и флаг на выдающего NPC.",
        [MainTab.SideStories] = "Побочные цепочки (Hildibrand, племена, ролевые и др.): прогресс и следующий квест с флагом.",
        [MainTab.Duties] = "Данжи, триалы и рейды: как открыть, механики боссов по ролям и лут.",
        [MainTab.Recap] = "Разбор последних боёв: от чего умирали и какие механики пропустили.",
        [MainTab.Deep] = "Deep Dungeons: этажи, ловушки, помандеры и опасные мобы.",
        [MainTab.Relics] = "Реликвии всех дополнений по шагам, Eureka и Bozja.",
        [MainTab.Occult] = "Occult Crescent: фантомные профессии, критические события и Forked Tower.",
        [MainTab.Variant] = "Variant и Criterion: записи журнала и как открыть все концовки.",
        [MainTab.PvP] = "PvP: ранг, Series Malmstones и награды сезона.",
        [MainTab.Hunt] = "Охота: марки рядом и оповещения о S/A-рангах.",
        [MainTab.SRanks] = "Как вызвать каждый S-ранг и когда откроется окно респауна.",
        [MainTab.HuntingLog] = "Hunting Log: кого ещё убить для каждого ранга, с флагом на карте.",
        [MainTab.Fates] = "FATE в текущей зоне и что за них дают.",
        [MainTab.Treasure] = "Карты сокровищ: где копать и что внутри.",
        [MainTab.Items] = "Вики предметов: фильтр по категории и профессии, источники, цены, примерка. Стрелки ↑/↓ листают список.",
        [MainTab.Bestiary] = "Бестиарий: где водится моб, что с него падает и фото.",
        [MainTab.Wiki] = "Зоны, NPC, рыбалка и избранное. Звёздочка в шапке — добавить карточку в избранное.",
        [MainTab.Furniture] = "Мебель и хаузинг: где взять и сколько стоит.",
        [MainTab.Housing] = "Свободные участки под дом и стадия лотереи. ↑/↓ — выбор участка, Enter — флаг на карте, Esc — закрыть карточку.",
        [MainTab.Jobs] = "Все навыки, трейты и джоб-квесты профессий.",
        [MainTab.Statuses] = "Баффы и дебаффы: что делает статус и откуда он.",
        [MainTab.Salvage] = "Что получится при разборке предмета и что лежит в сундуках.",
        [MainTab.Dyes] = "Каталог красок: цвета, где взять и цена.",
        [MainTab.Leves] = "Левы: где взять, что нужно и сколько опыта дают.",
        [MainTab.Gear] = "Экипировка под текущую профессию и апгрейды с рынка.",
        [MainTab.Bis] = "BiS-сеты с The Balance для твоей профессии.",
        [MainTab.Build] = "Материя и еда под твою профессию.",
        [MainTab.Leveling] = "Как быстрее прокачаться на текущем уровне.",
        [MainTab.Achievements] = "Достижения: прогресс и что легко добить.",
        [MainTab.Diary] = "Дневник прогресса по дням.",
        [MainTab.Mounts] = "Все маунты: фильтр по способу получения, картинка, места и музыка. ↑/↓ — листать, Esc — закрыть карточку.",
        [MainTab.Collections] = "Коллекции: минионы, карты, причёски, оркестрион, очки, фоны и рамки портрета.",
        [MainTab.MountFarm] = "Что из коллекций ещё можно нафармить и где.",
        [MainTab.Unlocks] = "Эмоции, позы и прочие разблокировки.",
        [MainTab.Glamour] = "Гламур и комод: что можно хранить и примерить.",
        [MainTab.Inventory] = "Разбор сумок и ретейнеров: что продать, что выбросить, что дорогое.",
        [MainTab.Currencies] = "Все валюты: сколько есть, потолок и на что выгоднее потратить.",
        [MainTab.Shopping] = "Список покупок с ценами по миру.",
        [MainTab.Craft] = "Крафт: дерево материалов и цены.",
        [MainTab.RecipeBooks] = "Книги мастер-рецептов: какие есть и где купить остальные.",
        [MainTab.Cosmic] = "Cosmic Exploration: миссии и награды.",
        [MainTab.Gather] = "Сбор: точки, окна появления и цены.",
        [MainTab.CollectableItems] = "Коллекционные предметы за скрипы.",
        [MainTab.Voyages] = "Субмарины и дирижабли: маршруты и время возвращения.",
        [MainTab.Ventures] = "Вылазки ретейнеров: что выгоднее отправить и апгрейды их экипировки.",
        [MainTab.Checklist] = "Дейлики и викли: отмечай сделанное, сброс считается сам.",
        [MainTab.Timers] = "Ретейнеры, субмарины и игровые сбросы — всё по времени.",
        [MainTab.Weather] = "Погода Эорзеи: прогноз по зонам и оповещения.",
        [MainTab.GoldSaucer] = "Gold Saucer: GATE, Triple Triad и награды.",
        [MainTab.Tribes] = "Племена: квесты, ранги и награды.",
        [MainTab.Island] = "Island Sanctuary: мастерская, урожай, животные.",
        [MainTab.Ocean] = "Рыбалка в океане: ближайший рейс и что ловить.",
        [MainTab.Weekly] = "Рейды, сезонные события и поставки недели.",
        [MainTab.ChocoboGarden] = "Чокобо и сад: семена, скрещивание и обучение.",
        [MainTab.Sightseeing] = "Sightseeing Log: виды, время, погода и эмоция.",
        [MainTab.LevelPlanner] = "Выбери профессию: классовый квест, рецепты для первого крафта, чем добивать уровень и что купить (за вычетом того, что уже есть в сумках и у ретейнеров).",
        [MainTab.Aether] = "Что не открыто в каждой зоне: кристаллы, городские кристаллы и течения эфира. Нажми «Флаг» — метка на карте.",
        [MainTab.FauxHollows] = "Выбери отметку сверху и кликай по клеткам так же, как открылось в игре. Золотая рамка — куда нажать дальше.",
        [MainTab.Carnivale] = "Стадии синего мага: что пройдено, задания недели и уязвимости врагов — по ним подбирай навыки.",
        [MainTab.ClanHunt] = "Цели из взятых листов клановой охоты: сколько осталось убить и где искать.",
        [MainTab.Squadron] = "Выбери миссию и вариант требований, который видишь в игре, — подберу состав из 4 бойцов.",
        [MainTab.Trove] = "Сколько томов события есть и какие награды ещё не получены.",
        [MainTab.Ishgard] = "Выбери ремесленника — подсвечу предмет для сдачи на твоём уровне. Во вкладке Diadem — погода и точки сбора с флагом.",
        [MainTab.GearState] = "Прочность и духовная связь снаряжения, еда. Внизу — пороги напоминаний.",
        [MainTab.MiniGames] = "Гонки чокобо, Lord of Verminion и маджонг: правила коротко, твой гоночный чокобо и достижения.",
        [MainTab.GcRank] = "Что нужно для следующего ранга Grand Company, лимит печатей и что из сумок можно сдать в Expert Delivery.",
        [MainTab.Doman] = "Сколько сдано в Doman Enclave на этой неделе и какие вещи из сумок выгоднее пожертвовать, чем продать.",
        [MainTab.Companion] = "Ранг и опыт боевого чокобо, свободные очки навыков и ветки умений. Наведи на навык — описание.",
        [MainTab.FcActions] = "Какие бафы FC сейчас на тебе и сколько им осталось; ниже — все действия FC с ценой и нужным рангом.",
        [MainTab.ExpBonus] = "Что сейчас добавляет опыт текущей профессии (отдых, еда, баф FC, шкаф, снаряжение) и какие рулетки ещё не сделаны.",
        [MainTab.RetainerMarket] = "Лоты ретейнеров на рынке и кого перебили. Обновляется, когда открываешь ретейнера у колокольчика.",
        [MainTab.Settings] = "Настройки внешнего вида, мини-окна, уведомлений и базы данных.",
    };

    public static void Draw(MainTab tab)
    {
        if (!Hints.TryGetValue(tab, out var text) || Plugin.Config.HiddenHints.Contains((int)tab)) return;
        var S = Theme.S;
        var dl = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var w = ImGui.GetContentRegionAvail().X;
        var lineH = ImGui.GetTextLineHeight();
        var h = lineH + 12 * S;
        dl.AddRectFilled(start, start + new Vector2(w, h), Theme.U32(Theme.Alpha(Theme.Blue, 0.12f)), 6f * S);
        dl.AddRectFilled(start, start + new Vector2(3 * S, h), Theme.U32(Theme.Blue), 2f * S);
        ImGui.PushFont(UiBuilder.IconFont);
        var icon = FontAwesomeIcon.Lightbulb.ToIconString();
        dl.AddText(start + new Vector2(12 * S, 6 * S), Theme.U32(Theme.Blue), icon);
        var iw = ImGui.CalcTextSize(icon).X;
        ImGui.PopFont();
        var clip = new Vector4(start.X, start.Y, start.X + w - 34 * S, start.Y + h);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), start + new Vector2(20 * S + iw, 6 * S), Theme.U32(Theme.Text), text, 0f, ref clip);
        if (ImGui.CalcTextSize(text).X > w - 60 * S && ImGui.IsMouseHoveringRect(start, start + new Vector2(w, h))) ImGui.SetTooltip(text);

        // Кнопка «закрыть насовсем»
        ImGui.SetCursorScreenPos(start + new Vector2(w - 28 * S, (h - 22 * S) / 2));
        if (ImGui.InvisibleButton($"##hintx{tab}", new Vector2(22 * S)))
        {
            Plugin.Config.HiddenHints.Add((int)tab);
            Plugin.Config.Save();
        }
        var hov = ImGui.IsItemHovered();
        ImGui.PushFont(UiBuilder.IconFont);
        var x = FontAwesomeIcon.Times.ToIconString();
        var xs = ImGui.CalcTextSize(x);
        dl.AddText(ImGui.GetItemRectMin() + (new Vector2(22 * S) - xs) / 2, Theme.U32(hov ? Theme.Text : Theme.TextDim), x);
        ImGui.PopFont();
        if (hov) ImGui.SetTooltip("Скрыть подсказку (вернуть можно в Настройках)");
        ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y + h + 8 * S));
        ImGui.Dummy(Vector2.Zero);
    }
}
