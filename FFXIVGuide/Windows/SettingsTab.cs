using Dalamud.Interface;
using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

internal sealed class SettingsTab
{
    private static readonly (MainTab Tab, string Name)[] StartTabs =
    {
        (MainTab.Home, "Обзор"), (MainTab.Search, "Поиск"), (MainTab.Story, "Сюжет"), (MainTab.Duties, "Данжи и бои"),
        (MainTab.Gear, "Экипировка"), (MainTab.Checklist, "Дейлики и викли"),
    };

    private string newTermEn = "";
    private string backupStatus = "";
    private string newTermRu = "";

    public void Draw()
    {
        Theme.Header("Настройки", "Сохраняются автоматически");
        var cfg = Plugin.Config;
        var changed = false;

        // Блоки настроек — в две колонки на широком окне
        var panels = new System.Collections.Generic.List<System.Action>();
        panels.Add(() => Theme.Panel("sgen", () =>
        {
            Theme.SubHeader("Общее");
            var idx = Math.Max(0, Array.FindIndex(StartTabs, t => t.Tab == cfg.StartTab));
            ImGui.SetNextItemWidth(220 * Theme.S);
            if (ImGui.BeginCombo("Раздел при открытии", StartTabs[idx].Name))
            {
                foreach (var (tab, name) in StartTabs)
                    if (ImGui.Selectable(name, cfg.StartTab == tab)) { cfg.StartTab = tab; changed = true; }
                ImGui.EndCombo();
            }

            var ru = cfg.QuestTextRussian;
            if (ImGui.Checkbox("Тексты квестов на русском по умолчанию", ref ru)) { cfg.QuestTextRussian = ru; changed = true; }

            var overlay = cfg.DutyOverlay;
            if (ImGui.Checkbox("Показывать окно механик при входе в данж", ref overlay)) { cfg.DutyOverlay = overlay; changed = true; }
            ImGui.TextColored(Theme.Gray, $"В базе механик: {MechanicsDb.Count} данжей и триалов. Окно вручную: /fguide overlay");

            ImGui.SetNextItemWidth(220 * Theme.S);
            var peek = cfg.ItemPeekMode;
            if (ImGui.Combo("Подсказка при наведении на предмет в игре", ref peek, "Выключена\0С зажатым Shift\0Всегда\0"))
            { cfg.ItemPeekMode = peek; changed = true; }
            ImGui.TextColored(Theme.Gray, "Где взять, на что меняется, цена. Полная карточка: /fguide peek");


            var hl = cfg.HighlightInGame;
            if (ImGui.Checkbox("Подсвечивать выбранный предмет в окнах игры", ref hl)) { cfg.HighlightInGame = hl; changed = true; }
            ImGui.TextColored(Theme.Gray, "Сумки, броне-шкаф, седельная сумка, ретейнер — золотая рамка вокруг слота.");
        }));

        panels.Add(() => Theme.Panel("slook", () =>
        {
            Theme.SubHeader("Внешний вид");
            ImGui.TextColored(Theme.TextDim, "Цвет акцента:");
            for (var i = 0; i < Theme.Accents.Length; i++)
            {
                var (name, color) = Theme.Accents[i];
                if (i > 0) ImGui.SameLine();
                var pos = ImGui.GetCursorScreenPos();
                var size = new System.Numerics.Vector2(26 * Theme.S);
                if (ImGui.InvisibleButton($"##acc{i}", size)) { cfg.AccentColor = i; changed = true; }
                var dl = ImGui.GetWindowDrawList();
                dl.AddCircleFilled(pos + size / 2, size.X / 2 - 2 * Theme.S, Theme.U32(color));
                if (cfg.AccentColor == i)
                    dl.AddCircle(pos + size / 2, size.X / 2, Theme.U32(Theme.Text), 0, 2f * Theme.S);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(name);
            }

            var scale = cfg.UiScale;
            ImGui.SetNextItemWidth(220 * Theme.S);
            if (ImGui.SliderFloat("Размер окна и текста", ref scale, 0.8f, 1.4f, "%.2f×"))
            {
                cfg.UiScale = scale;
                changed = true;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Сброс")) { cfg.UiScale = 1f; changed = true; }
        }));

        panels.Add(() => Theme.Panel("smini", () =>
        {
            Theme.SubHeader("Блоки мини-окна");
            foreach (var (key, label) in new[] { ("story", "Сюжет"), ("daily", "Дейлики"), ("fate", "FATE"), ("target", "Цель"), ("hunt", "Марки"), ("timers", "Таймеры"), ("ocean", "Океан"), ("weather", "Погода") })
            {
                var on = cfg.MiniBlocks.Contains(key);
                if (ImGui.Checkbox($"{label}##mb{key}", ref on)) { if (on) cfg.MiniBlocks.Add(key); else cfg.MiniBlocks.Remove(key); changed = true; }
                if (key != "weather") ImGui.SameLine();
            }

            Theme.SubHeader("Мини-окно и горячая клавиша");
            var mini = cfg.MiniWindow;
            if (ImGui.Checkbox("Мини-окно поверх игры (сюжет, дейлики, FATE, марки)", ref mini))
                Plugin.SetMiniWindow(mini);
            ImGui.TextColored(Theme.Gray, "Также: /fguide mini");

            var hk = cfg.HotkeyEnabled;
            if (ImGui.Checkbox("Горячая клавиша для окна", ref hk)) { cfg.HotkeyEnabled = hk; changed = true; }
            var ctrl = cfg.HotkeyCtrl;
            ImGui.SameLine();
            if (ImGui.Checkbox("Ctrl", ref ctrl)) { cfg.HotkeyCtrl = ctrl; changed = true; }
            var shift = cfg.HotkeyShift;
            ImGui.SameLine();
            if (ImGui.Checkbox("Shift", ref shift)) { cfg.HotkeyShift = shift; changed = true; }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(70 * Theme.S);
            var letter = ((char)cfg.HotkeyKey).ToString();
            if (ImGui.BeginCombo("##hkkey", letter))
            {
                for (var c = 'A'; c <= 'Z'; c++)
                    if (ImGui.Selectable(c.ToString(), cfg.HotkeyKey == c)) { cfg.HotkeyKey = c; changed = true; }
                ImGui.EndCombo();
            }
            ImGui.TextColored(Theme.Gray, "По умолчанию Ctrl+Shift+G. Не выбирай сочетание, занятое в самой игре.");
        }));

        panels.Add(() => Theme.Panel("sinv", () =>
        {
            Theme.SubHeader("Разбор инвентаря");
            var t = (int)cfg.ValuableThreshold;
            ImGui.SetNextItemWidth(160 * Theme.S);
            if (ImGui.InputInt("«Дорогое» от, гил", ref t, 1000, 10000)) { cfg.ValuableThreshold = Math.Max(0, t); changed = true; }

            ImGui.TextColored(Theme.TextDim, $"Исключённые вещи (не предлагать): {cfg.ExcludedItems.Count}");
            uint? remove = null;
            var sheet = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            foreach (var id in cfg.ExcludedItems.ToList())
            {
                ImGui.TextColored(Theme.Text, sheet.GetRowOrDefault(id)?.Name.ExtractText() ?? $"#{id}");
                ImGui.SameLine();
                if (ImGui.SmallButton($"Вернуть##ex{id}")) remove = id;
            }
            if (remove is not null) { cfg.ExcludedItems.Remove(remove.Value); changed = true; }
        }));

        panels.Add(() => Theme.Panel("sdict", () =>
        {
            Theme.SubHeader("Словарь перевода");
            var use = cfg.UseTermDictionary;
            if (ImGui.Checkbox("Использовать словарь имён и терминов", ref use)) { cfg.UseTermDictionary = use; changed = true; }
            ImGui.TextColored(Theme.Gray, $"Встроено терминов: {TermDictionary.BuiltInCount}. Перед переводом они заменяются на русские, чтобы Google их не коверкал.");

            ImGui.SetNextItemWidth(200 * Theme.S);
            ImGui.InputTextWithHint("##ten", "English (как в игре)", ref newTermEn, 64);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200 * Theme.S);
            ImGui.InputTextWithHint("##tru", "Как переводить", ref newTermRu, 64);
            ImGui.SameLine();
            if (ImGui.SmallButton("Добавить") && newTermEn.Trim().Length > 0 && newTermRu.Trim().Length > 0)
            {
                cfg.CustomTerms[newTermEn.Trim()] = newTermRu.Trim();
                newTermEn = newTermRu = "";
                changed = true;
                Translator.Instance.ClearCache();
            }

            string? del = null;
            foreach (var (en, ruTerm) in cfg.CustomTerms)
            {
                ImGui.TextColored(Theme.Text, $"{en} → {ruTerm}");
                ImGui.SameLine();
                if (ImGui.SmallButton($"Удалить##t{en}")) del = en;
            }
            if (del is not null) { cfg.CustomTerms.Remove(del); changed = true; Translator.Instance.ClearCache(); }
        }));

        panels.Add(() => Theme.Panel("snotify", () =>
        {
            Theme.SubHeader("Уведомления");
            ImGui.TextColored(Theme.TextDim, "Куда сообщать о событиях. Всплывающие — окна Dalamud в углу экрана.");
            foreach (var (kind, name) in Notify.Kinds)
            {
                var m = Notify.Mode(kind);
                ImGui.SetNextItemWidth(190 * Theme.S);
                if (ImGui.BeginCombo($"{name}##nm{kind}", Notify.Modes[m]))
                {
                    for (var i = 0; i < Notify.Modes.Length; i++)
                        if (ImGui.Selectable(Notify.Modes[i], i == m)) Notify.SetMode(kind, i);
                    ImGui.EndCombo();
                }
            }
            if (ImGui.SmallButton("Проверить уведомление")) Notify.Send(Notify.Kind.Weather, "Проверка", "Так будут выглядеть уведомления.");
        }));

        panels.Add(() => Theme.Panel("smap", () =>
        {
            Theme.SubHeader("Метки на карте игры");
            ImGui.TextColored(Theme.TextDim, "Иконки на карте зоны (окно карты, клавиша M). Только показывают.");
            foreach (var (key, name, _) in MapMarkers.Kinds)
            {
                var on = cfg.MapMarkerKinds.Contains(key);
                if (ImGui.Checkbox($"{name}##mm{key}", ref on))
                {
                    if (on) cfg.MapMarkerKinds.Add(key); else cfg.MapMarkerKinds.Remove(key);
                    MapMarkers.Invalidate();
                    changed = true;
                }
            }
            ImGui.TextColored(Theme.Gray, "Рыбалка и FATE берутся из офлайн-базы. Изменения видны после переоткрытия карты.");
        }));

        panels.Add(() => Theme.Panel("sdb", () =>
        {
            Theme.SubHeader("База данных (офлайн)");
            var db = OfflineDb.Instance;
            if (db.Installed)
            {
                ImGui.TextColored(Theme.Green, $"Скачана {db.Info.Updated:dd.MM.yyyy HH:mm} · {db.SizeOnDisk / 1024.0 / 1024.0:0} МБ");
                if (db.MissingFiles) ImGui.TextColored(Theme.Yellow, "Для новых разделов нужны ещё файлы — нажми «Обновить».");
                ImGui.TextColored(Theme.TextDim, db.Loading ? "Загружаю в память…" : db.Index is null ? "Не загружена в память" : "Работает: вики открывается без сети.");
            }
            else
                ImGui.TextColored(Theme.Yellow, "Не скачана: вики берёт данные из сети при открытии карточки.");
            ImGui.TextColored(Theme.Gray, "Источник — открытые файлы Teamcraft (GitHub) и индексы Garland Tools, около 45 МБ.");

            if (db.Busy)
            {
                Theme.Progress(db.Status, (int)(db.ProgressValue * 100), 100, Theme.Gold);
                if (ImGui.SmallButton("Отменить##dbc")) db.Cancel();
            }
            else
            {
                if (Theme.IconButton("dbget", FontAwesomeIcon.Download, db.Installed ? "Обновить" : "Скачать всё"))
                    db.Update(false);
                Theme.Tooltip(db.Installed ? "Скачает только файлы, изменившиеся после патча" : "Разовая загрузка, потом всё работает без сети");
                if (db.Installed)
                {
                    ImGui.SameLine();
                    if (Theme.IconButton("dbforce", FontAwesomeIcon.Redo, "Скачать заново")) db.Update(true);
                    ImGui.SameLine();
                    if (Theme.IconButton("dbdel", FontAwesomeIcon.Trash, "Удалить")) db.Delete();
                }
                if (db.Status.Length > 0) ImGui.TextColored(Theme.TextDim, db.Status);
            }
            var online = cfg.WikiUseOnline;
            if (ImGui.Checkbox("Дополнять базу данными Garland Tools", ref online)) { cfg.WikiUseOnline = online; changed = true; }
            ImGui.TextColored(Theme.Gray, "Дроп с мобов и лут по каждому боссу у Garland полнее. Без галочки — полностью офлайн.");
        }));

        panels.Add(() => Theme.Panel("sru", () =>
        {
            Theme.SubHeader("Русские названия");
            var tr = NameTranslations.Instance;
            ImGui.SetNextItemWidth(240 * Theme.S);
            var mode = cfg.NameMode;
            if (ImGui.Combo("Показывать названия##nm", ref mode, "Английские\0Русские\0Русское (English)\0")) { cfg.NameMode = mode; changed = true; }
            ImGui.TextColored(Theme.TextDim, $"Переведено названий: {tr.Count:N0}. Поиск по-русски работает по всем переведённым.");
            if (tr.Running)
            {
                Theme.Progress(tr.Status, tr.Done, Math.Max(1, tr.Total), Theme.Blue);
                if (ImGui.SmallButton(tr.Paused ? "Продолжить##trp" : "Пауза##trp")) tr.Paused = !tr.Paused;
                ImGui.SameLine();
                if (ImGui.SmallButton("Остановить##trs")) tr.Stop();
            }
            else
            {
                if (Theme.IconButton("trgo", FontAwesomeIcon.Language, tr.Count == 0 ? "Перевести названия" : "Перевести новые / продолжить"))
                    tr.Start();
                Theme.Tooltip("Машинный перевод (Google) в фоне: предметы, мобы, NPC, квесты, данжи, зоны, FATE. 20–40 минут, можно играть.");
                if (tr.Count > 0)
                {
                    ImGui.SameLine();
                    if (Theme.IconButton("trclr", FontAwesomeIcon.Trash, "Удалить переводы")) tr.Clear();
                }
                if (tr.Status.Length > 0) ImGui.TextColored(Theme.TextDim, tr.Status);
            }
            ImGui.TextColored(Theme.Gray, $"Длинные тексты (квесты, описания) переводятся при открытии и сохраняются: {Translator.Instance.Count:N0}.");
            ImGui.TextColored(Theme.Gray, "Перевод машинный. Неудачные термины поправь в «Словаре перевода» — применяется к новым переводам.");
        }));

        panels.Add(() => Theme.Panel("swiki", () =>
        {
            Theme.SubHeader("Вики (Garland Tools)");
            ImGui.TextColored(Theme.TextDim, "Мобы, лут и источники предметов грузятся с garlandtools.org (хранятся неделю), фото мобов — с consolegameswiki.");
            ImGui.TextColored(Theme.TextDim, $"Кеш: {WikiService.Instance.CacheSizeBytes() / 1024.0 / 1024.0:0.0} МБ");
            if (Theme.IconButton("wclear", FontAwesomeIcon.Trash, "Очистить кеш вики"))
            {
                WikiService.Instance.ClearCache();
                MobImages.Instance.ClearCache();
            }
        }));

        panels.Add(() => Theme.Panel("sbackup", () =>
        {
            Theme.SubHeader("Резервная копия");
            ImGui.TextColored(Theme.TextDim, "Все настройки, исключения, словарь, отметки дейликов и снимки ретейнеров.");
            if (ImGui.Button("Скопировать в буфер обмена"))
            {
                ImGui.SetClipboardText(System.Text.Json.JsonSerializer.Serialize(cfg));
                backupStatus = "Скопировано. Сохрани текст в файл (например, Блокнот).";
            }
            ImGui.SameLine();
            if (ImGui.Button("Восстановить из буфера"))
                backupStatus = Restore(ImGui.GetClipboardText());
            if (backupStatus.Length > 0)
                ImGui.TextColored(backupStatus.StartsWith("Ошибка") ? Theme.Red : Theme.Green, backupStatus);
        }));

        Theme.Grid("sgrid", panels.Count, 420f, i => panels[i]());

        if (changed) cfg.Save();
    }

    /// <summary>Загрузить настройки из JSON (скопированного кнопкой выше).</summary>
    private static string Restore(string json)
    {
        try
        {
            var loaded = System.Text.Json.JsonSerializer.Deserialize<Configuration>(json ?? "");
            if (loaded is null) return "Ошибка: в буфере нет настроек.";
            foreach (var prop in typeof(Configuration).GetProperties())
                if (prop.CanWrite) prop.SetValue(Plugin.Config, prop.GetValue(loaded));
            Plugin.Config.Save();
            return "Настройки восстановлены.";
        }
        catch (System.Exception e)
        {
            return "Ошибка: " + e.Message;
        }
    }
}
