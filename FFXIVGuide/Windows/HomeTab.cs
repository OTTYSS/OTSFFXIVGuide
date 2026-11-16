using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

/// <summary>Стартовая страница: прогресс и «что делать дальше».</summary>
internal sealed class HomeTab
{
    private readonly GuideData data;
    private readonly QuestsTab quests;
    private readonly DutiesTab duties;
    private readonly Action<MainTab> go;

    // Пересчитываем раз в пару секунд: проход по всем квестам каждый кадр не нужен.
    private DateTime nextRefresh = DateTime.MinValue;
    private Snapshot? snap;

    private sealed record Snapshot(
        int DutiesTotal, int DutiesUnlocked, int DutiesDone,
        int UnlockQuestsTotal, int UnlockQuestsDone,
        List<QuestEntry> NextQuests,
        List<(DutyEntry Duty, QuestEntry? Step)> SoonDuties,
        QuestEntry? Msq,
        int AvgIlvl);

    public HomeTab(GuideData data, QuestsTab quests, DutiesTab duties, Action<MainTab> go)
    {
        this.data = data;
        this.quests = quests;
        this.duties = duties;
        this.go = go;
    }

    public void Draw()
    {
        Theme.Header("Обзор", "Главное о персонаже и что сделать дальше. Нажми на плитку или кольцо — откроется раздел.");
        if (Theme.IconButton("hcfg", editTiles ? FontAwesomeIcon.Check : FontAwesomeIcon.SlidersH, editTiles ? "Готово" : "Изменить плитки"))
        {
            editTiles = !editTiles;
            dragId = null;
        }
        if (editTiles)
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextDim, "Тяни плитку за значок со стрелками, чтобы переставить. Кнопки справа на плитке: шире или уже и убрать.");
        }

        if (!PlayerInfo.Ready)
        {
            Theme.NeedLogin("Зайди в игру персонажем — здесь появится прогресс и следующие шаги.");
            return;
        }

        if (snap is null || DateTime.Now >= nextRefresh)
        {
            snap = Build();
            nextRefresh = DateTime.Now.AddSeconds(3);
        }

        if (!ImGui.BeginChild("##home", Vector2.Zero))
        {
            ImGui.EndChild();
            return;
        }

        // ── Плитки: на широком окне 2–3 в ряд, на узком — столбиком; широкие занимают две колонки
        DrawTiles();

        // ── Списки «что сделать» в две колонки
        var rowH = 44 * Theme.S;
        Theme.Grid("hlists", 2, 380f, i =>
        {
            if (i == 0)
            {
                Theme.Panel("hnext", () =>
                {
                    Theme.SubHeader("Доступные квесты с анлоками");
                    if (snap.NextQuests.Count == 0)
                        ImGui.TextColored(Theme.TextDim, "Всё доступное выполнено.");
                    foreach (var q in snap.NextQuests)
                    {
                        var (badge, color) = QuestsTab.StateBadge(q);
                        if (Theme.Card($"##hn{q.Id}", q.Icon, q.Name, $"Ур. {q.Level} · {q.Unlocks}", badge, color, false, rowH))
                            quests.Show(q);
                    }
                });
            }
            else
            {
                Theme.Panel("hsoon", () =>
                {
                    Theme.SubHeader("Данжи твоего уровня, ещё не открытые");
                    if (snap.SoonDuties.Count == 0)
                        ImGui.TextColored(Theme.TextDim, "Все данжи твоего уровня открыты.");
                    foreach (var (d, step) in snap.SoonDuties)
                    {
                        var sub = step is null ? $"Ур. {d.Level} · {d.Type}" : $"Сейчас: {step.Name}";
                        if (Theme.Card($"##hs{d.CfcId}", d.Icon, d.Name, sub, $"ур. {d.Level}", Theme.TextDim, false, rowH))
                            duties.Show(d);
                    }
                });
            }
        });

        ImGui.EndChild();
    }

    // ───────────── Плитки ─────────────

    /// <summary>Плитка: иконка, заголовок, кнопка-стрелка «перейти» и содержимое.</summary>
    private void Tile(string id, FontAwesomeIcon icon, string title, MainTab target, System.Action content)
    {
        var cfgId = curTile;
        Theme.Panel(id, () =>
        {
            if (editTiles && cfgId is not null)
            {
                // Ручка для перетаскивания
                ImGui.PushFont(UiBuilder.IconFont);
                var grip = FontAwesomeIcon.ArrowsAlt.ToIconString();
                var gs = ImGui.CalcTextSize(grip);
                ImGui.PopFont();
                var gp = ImGui.GetCursorScreenPos();
                ImGui.InvisibleButton($"##grip{cfgId}", gs + new Vector2(6 * Theme.S, 0));
                if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 3f)) dragId = cfgId;
                if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.GetWindowDrawList().AddText(gp, Theme.U32(dragId == cfgId ? Theme.Gold : Theme.TextDim), grip);
                ImGui.PopFont();
                ImGui.SameLine();
            }
            Theme.FaIcon(icon, Theme.Gold);
            ImGui.SameLine();
            ImGui.TextColored(Theme.GoldDim, title.ToUpperInvariant());
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            var one = ImGui.CalcTextSize(FontAwesomeIcon.ArrowRight.ToIconString()).X + ImGui.GetStyle().FramePadding.X * 2;
            if (editTiles && cfgId is not null)
            {
                var wide = Plugin.Config.WideHomeTiles.Contains(cfgId);
                ImGui.SetCursorScreenPos(new Vector2(Theme.PanelRight - one * 2 - 4 * Theme.S, ImGui.GetCursorScreenPos().Y));
                var resize = ImGui.SmallButton($"{(wide ? FontAwesomeIcon.Compress : FontAwesomeIcon.Expand).ToIconString()}##wide{cfgId}");
                var resizeHover = ImGui.IsItemHovered();
                ImGui.SameLine(0, 4 * Theme.S);
                var remove = ImGui.SmallButton($"{FontAwesomeIcon.Times.ToIconString()}##rm{cfgId}");
                var removeHover = ImGui.IsItemHovered();
                ImGui.PopFont();
                if (resizeHover) ImGui.SetTooltip(wide ? "Сделать обычной ширины" : "Сделать широкой (на две колонки)");
                if (removeHover) ImGui.SetTooltip("Убрать плитку");
                if (resize)
                {
                    if (!Plugin.Config.WideHomeTiles.Remove(cfgId)) Plugin.Config.WideHomeTiles.Add(cfgId);
                    Plugin.Config.Save();
                }
                if (remove)
                {
                    Plugin.Config.HomeTiles.Remove(cfgId);
                    Plugin.Config.Save();
                }
            }
            else
            {
                ImGui.SetCursorScreenPos(new Vector2(Theme.PanelRight - one, ImGui.GetCursorScreenPos().Y));
                var clicked = ImGui.SmallButton($"{FontAwesomeIcon.ArrowRight.ToIconString()}##go{id}");
                ImGui.PopFont();
                if (clicked) go(target);
                Theme.Tooltip("Открыть раздел");
            }
            ImGui.Dummy(new Vector2(0, 2 * Theme.S));
            content();
        });
    }

    // ───────────── Раскладка плиток и редактирование мышкой ─────────────

    private bool editTiles;
    private string? dragId;
    private string? curTile;
    private readonly Dictionary<string, (Vector2 Min, Vector2 Max)> tileRects = new();

    private void DrawTiles()
    {
        var S = Theme.S;
        var ids = Plugin.Config.HomeTiles.Where(id => TileById(id) is not null).ToList();
        var avail = ImGui.GetContentRegionAvail().X;
        var cols = Math.Clamp((int)(avail / (300f * S)), 1, 3);

        // Ряды: широкая плитка занимает 2 колонки (если колонок хватает)
        var items = ids.Select(id => (Id: id, Span: cols > 1 && Plugin.Config.WideHomeTiles.Contains(id) ? 2 : 1)).ToList();
        if (editTiles && AllTiles.Any(t => !ids.Contains(t.Id))) items.Add(("+add", 1));
        var rowsOf = new List<List<(string Id, int Span)>>();
        var row = new List<(string Id, int Span)>();
        var used = 0;
        foreach (var it in items)
        {
            if (used + it.Span > cols && row.Count > 0) { rowsOf.Add(row); row = new(); used = 0; }
            row.Add(it);
            used += it.Span;
        }
        if (row.Count > 0) rowsOf.Add(row);

        tileRects.Clear();
        for (var r = 0; r < rowsOf.Count; r++)
        {
            var cells = rowsOf[r];
            var filler = cols - cells.Sum(c => c.Span);
            if (!ImGui.BeginTable($"##hrow{r}", cells.Count + (filler > 0 ? 1 : 0), ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX)) continue;
            foreach (var c in cells) ImGui.TableSetupColumn($"##c{c.Id}", ImGuiTableColumnFlags.WidthStretch, c.Span);
            if (filler > 0) ImGui.TableSetupColumn("##fill", ImGuiTableColumnFlags.WidthStretch, filler);
            foreach (var c in cells)
            {
                ImGui.TableNextColumn();
                var start = ImGui.GetCursorScreenPos();
                var w = ImGui.GetContentRegionAvail().X;
                if (c.Id == "+add") DrawAddTile();
                else
                {
                    curTile = c.Id;
                    ImGui.PushID(c.Id);
                    TileById(c.Id)!();
                    ImGui.PopID();
                    curTile = null;
                }
                tileRects[c.Id] = (start, new Vector2(start.X + w, ImGui.GetCursorScreenPos().Y));
            }
            if (filler > 0) ImGui.TableNextColumn();
            ImGui.EndTable();
        }

        // Перетаскивание: подсветка места и перенос при отпускании кнопки
        if (dragId is null) return;
        var mouse = ImGui.GetMousePos();
        var target = tileRects.FirstOrDefault(kv => kv.Key != "+add" && mouse.X >= kv.Value.Min.X && mouse.X <= kv.Value.Max.X && mouse.Y >= kv.Value.Min.Y && mouse.Y <= kv.Value.Max.Y).Key;
        var fg = ImGui.GetForegroundDrawList();
        if (target is not null && target != dragId)
        {
            var rc = tileRects[target];
            fg.AddRect(rc.Min, rc.Max, Theme.U32(Theme.Gold), 8f * S, ImDrawFlags.None, 2.5f * S);
        }
        var name = AllTiles.FirstOrDefault(t => t.Id == dragId).Name ?? dragId;
        fg.AddRectFilled(mouse + new Vector2(14, 10) * S, mouse + new Vector2(14 * S + ImGui.CalcTextSize(name).X + 16 * S, 10 * S + ImGui.GetTextLineHeight() + 8 * S), Theme.U32(Theme.Alpha(Theme.PanelBg, 0.95f)), 6f * S);
        fg.AddText(mouse + new Vector2(22, 14) * S, Theme.U32(Theme.Gold), name);
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (target is not null && target != dragId)
            {
                var list = Plugin.Config.HomeTiles;
                var from = list.IndexOf(dragId);
                var to = list.IndexOf(target);
                if (from >= 0 && to >= 0)
                {
                    list.RemoveAt(from);
                    list.Insert(to, dragId);
                    Plugin.Config.Save();
                }
            }
            dragId = null;
        }
    }

    private static void DrawAddTile()
    {
        Theme.Panel("taddtile", () =>
        {
            Theme.FaIcon(FontAwesomeIcon.Plus, Theme.Gold);
            ImGui.SameLine();
            ImGui.TextColored(Theme.GoldDim, "ДОБАВИТЬ ПЛИТКУ");
            var shown = Plugin.Config.HomeTiles;
            foreach (var (id, name) in AllTiles.Where(t => !shown.Contains(t.Id)))
            {
                if (ImGui.SmallButton($"+ {name}##add{id}"))
                {
                    shown.Add(id);
                    Plugin.Config.Save();
                }
            }
        });
    }

    private void TileStory() => Tile("tstory", FontAwesomeIcon.Book, "Сюжет", MainTab.Story, () =>
    {
        if (snap?.Msq is not { } q)
        {
            ImGui.TextColored(Theme.Green, "Весь доступный сюжет пройден!");
            return;
        }
        Theme.LargeText(Theme.Text, q.Name);
        ImGui.TextColored(Theme.TextDim, $"{StoryHelper.ChapterName(q.CategoryId)} · ур. {q.Level}");
        ImGui.TextColored(Theme.TextDim, q.Place);
        if (Theme.IconButton("tsflag", FontAwesomeIcon.Flag, "Флаг на карте"))
            PlayerInfo.FlagQuestGiver(q);
    });

    private void TileDaily() => Tile("tdaily", FontAwesomeIcon.Tasks, "Дейлики и викли", MainTab.Checklist, () =>
    {
        var open = Checklist.Items.Where(i => !Checklist.StateOf(i).Done).ToList();
        var daily = open.Count(i => i.Period == Period.Daily);
        var weekly = open.Count(i => i.Period == Period.Weekly);
        var left = Checklist.NextReset(Period.Daily, DateTime.UtcNow) - DateTime.UtcNow;
        Theme.LargeText(daily + weekly == 0 ? Theme.Green : Theme.Text,
            daily + weekly == 0 ? "Всё сделано" : $"Осталось: {daily} + {weekly}");
        ImGui.TextColored(Theme.TextDim, $"дневных + недельных · сброс через {left.Hours} ч {left.Minutes} мин");
        foreach (var i in open.Take(3))
        {
            Theme.FaIcon(FontAwesomeIcon.ArrowRight, Theme.GoldDim);
            ImGui.SameLine();
            ImGui.TextColored(Theme.Text, i.Name);
        }
    });

    private void TileCharacter() => Tile("tchar", FontAwesomeIcon.ShieldAlt, "Персонаж", MainTab.Gear, () =>
    {
        if (Plugin.PlayerState.ClassJob.ValueNullable is not { } job) return;
        var pos = ImGui.GetCursorScreenPos();
        Theme.GameIcon(62100 + job.RowId, pos, 44 * Theme.S, 22f);
        ImGui.SetCursorScreenPos(pos + new Vector2(54 * Theme.S, 0));
        ImGui.BeginGroup();
        Theme.LargeText(Theme.Text, $"{GuideData.Capitalize(job.Name.ExtractText())} {Plugin.PlayerState.Level}");
        ImGui.TextColored(Theme.Gold, $"Средний iLvl {snap?.AvgIlvl}");
        ImGui.EndGroup();
        ImGui.SetCursorScreenPos(new Vector2(pos.X, Math.Max(ImGui.GetCursorScreenPos().Y, pos.Y + 50 * Theme.S)));

        var level = Plugin.PlayerState.Level;
        var expNow = Plugin.PlayerState.GetClassJobExperience(job);
        var grow = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.ParamGrow>().GetRowOrDefault((uint)level);
        var expMax = grow?.ExpToNext ?? 0;
        if (expMax > 0)
            Theme.Progress("Опыт до следующего уровня", expNow, expMax, Theme.Gold);
        else
            ImGui.TextColored(Theme.Green, "Максимальный уровень");
    });

    private void TileProgress() => Tile("tprog", FontAwesomeIcon.ChartLine, "Прогресс", MainTab.Duties, () =>
    {
        if (snap is null) return;
        var items = new (string Label, int Done, int Total, Vector4 Color, System.Action Open)[]
        {
            ("Открыто", snap.DutiesUnlocked, snap.DutiesTotal, Theme.Yellow, () => duties.ShowRemaining(1)),
            ("Пройдено", snap.DutiesDone, snap.DutiesTotal, Theme.Green, () => duties.ShowRemaining(3)),
            ("Анлоки", snap.UnlockQuestsDone, snap.UnlockQuestsTotal, Theme.Gold, () => quests.ShowRemainingUnlocks()),
        };
        var w = ImGui.GetContentRegionAvail().X;
        var cell = w / items.Length;
        var r = Math.Min(30 * Theme.S, cell / 2 - 10 * Theme.S);
        var start = ImGui.GetCursorScreenPos();
        for (var i = 0; i < items.Length; i++)
        {
            var it = items[i];
            var c = start + new Vector2(cell * i + cell / 2, r + 6 * Theme.S);
            var frac = it.Total == 0 ? 0f : (float)it.Done / it.Total;
            ImGui.SetCursorScreenPos(c - new Vector2(r + 6 * Theme.S));
            if (ImGui.InvisibleButton($"ring{i}", new Vector2((r + 6 * Theme.S) * 2)))
                it.Open();
            var hovered = ImGui.IsItemHovered();
            Theme.Ring(c, r, frac, hovered ? Theme.Text : it.Color, $"{frac * 100:0}%", $"home{i}");
            if (hovered) ImGui.SetTooltip($"{it.Label}: {it.Done} из {it.Total}\nНажми — покажу, что осталось");
            var ls = ImGui.CalcTextSize(it.Label);
            ImGui.GetWindowDrawList().AddText(new Vector2(c.X - ls.X / 2, c.Y + r + 10 * Theme.S), Theme.U32(Theme.TextDim), it.Label);
        }
        ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y + r * 2 + 34 * Theme.S));
        ImGui.Dummy(Vector2.Zero);
        ImGui.TextColored(Theme.Gray, "Данжи и бои с квестом открытия · квесты-анлоки");
    });

    // ───────────── Настройка плиток ─────────────

    private static readonly (string Id, string Name)[] AllTiles =
    {
        ("story", "Сюжет"), ("daily", "Дейлики и викли"), ("character", "Персонаж"), ("progress", "Прогресс"),
        ("week", "За неделю"), ("timers", "Таймеры"), ("ocean", "Океаническая рыбалка"), ("event", "Сезонное событие"), ("weather", "Погода"),
    };

    private System.Action? TileById(string id) => id switch
    {
        "story" => TileStory,
        "daily" => TileDaily,
        "character" => TileCharacter,
        "progress" => TileProgress,
        "week" => TileWeek,
        "timers" => TileTimers,
        "ocean" => TileOcean,
        "event" => TileEvent,
        "weather" => TileWeather,
        _ => null,
    };

    private void TileTimers() => Tile("ttimers", FontAwesomeIcon.Stopwatch, "Таймеры", MainTab.Timers, () =>
    {
        var now = EorzeaWeather.Now;
        var list = Plugin.Config.Voyages.Values.Where(v => v.Return > 0).OrderBy(v => v.Return).Take(4).ToList();
        if (list.Count == 0) { ImGui.TextColored(Theme.TextDim, "Подойди к колокольчику ретейнеров или зайди в мастерскую — запомню время."); return; }
        foreach (var v in list)
        {
            Theme.FaIcon(v.Kind == "retainer" ? FontAwesomeIcon.UserTie : FontAwesomeIcon.Ship, Theme.GoldDim);
            ImGui.SameLine();
            ImGui.TextColored(Theme.Text, v.Name);
            ImGui.SameLine();
            ImGui.TextColored(v.Return <= now ? Theme.Green : Theme.TextDim, v.Return <= now ? "вернулся ✓" : $"через {EorzeaWeather.Until(v.Return)}");
        }
    });

    private void TileOcean() => Tile("tocean", FontAwesomeIcon.Water, "Океан", MainTab.Ocean, () =>
    {
        var (start, route, boarding) = OceanTab.Next();
        Theme.LargeText(boarding ? Theme.Green : Theme.Text, boarding ? "Посадка открыта!" : $"Через {EorzeaWeather.Until(start)}");
        ImGui.TextColored(Theme.TextDim, route);
        ImGui.TextColored(Theme.Gray, "Limsa Lominsa Lower Decks");
    });

    private unsafe void TileEvent() => Tile("tevent", FontAwesomeIcon.Gift, "Событие", MainTab.Weekly, () =>
    {
        var gm = FFXIVClientStructs.FFXIV.Client.Game.GameMain.Instance();
        var active = new List<uint>();
        if (gm != null) foreach (var f in gm->ActiveFestivals) if (f.Id != 0) active.Add(f.Id);
        if (active.Count == 0) { ImGui.TextColored(Theme.TextDim, "Сейчас сезонных событий нет."); return; }
        var quests = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Quest>().Where(q => q.Festival.RowId != 0 && active.Contains(q.Festival.RowId) && q.Name.ExtractText().Length > 0).ToList();
        var left = quests.Count(q => !PlayerInfo.IsQuestDone(q.RowId));
        Theme.LargeText(left > 0 ? Theme.Gold : Theme.Green, left > 0 ? $"Квестов осталось: {left}" : "Все квесты сделаны ✓");
        ImGui.TextColored(Theme.TextDim, $"Идёт событий: {active.Count}");
    });

    private void TileWeather() => Tile("tweather", FontAwesomeIcon.CloudSun, "Погода", MainTab.Weather, () =>
    {
        var (h, m) = EorzeaWeather.EorzeaTime(EorzeaWeather.Now);
        Theme.LargeText(Theme.Text, $"{h:00}:{m:00} ET");
        ImGui.TextColored(Theme.TextDim, WeatherTab.NextAlert() ?? "Напоминаний нет — добавь в разделе «Погода».");
    });

    private void TileWeek() => Tile("tweek", FontAwesomeIcon.History, "За неделю", MainTab.Diary, () =>
    {
        var hist = ProgressDiary.History.Where(s => s.Date >= DateTime.Now.Date.AddDays(-7)).ToList();
        if (hist.Count < 2)
        {
            ImGui.TextColored(Theme.TextDim, "Дневник собирает данные — сводка появится на второй день.");
            return;
        }
        var a = hist.First();
        var b = hist.Last();
        Delta("Уровни профессий", b.JobLevelsTotal - a.JobLevelsTotal);
        Delta("Сюжетные квесты", b.MsqDone - a.MsqDone);
        Delta("Данжи и бои", b.DutiesDone - a.DutiesDone);
        Delta("Средний iLvl", b.AvgIlvl - a.AvgIlvl);

        // Мини-график суммы уровней
        var pos = ImGui.GetCursorScreenPos() + new Vector2(0, 4 * Theme.S);
        var size = new Vector2(ImGui.GetContentRegionAvail().X, 36 * Theme.S);
        var vals = hist.Select(s => (float)s.JobLevelsTotal).ToList();
        var min = vals.Min();
        var max = Math.Max(vals.Max(), min + 1);
        var dl = ImGui.GetWindowDrawList();
        for (var i = 1; i < vals.Count; i++)
        {
            Vector2 P(int k) => pos + new Vector2(size.X * k / (vals.Count - 1), size.Y * (1 - (vals[k] - min) / (max - min)));
            dl.AddLine(P(i - 1), P(i), Theme.U32(Theme.Gold), 2f * Theme.S);
        }
        ImGui.Dummy(size + new Vector2(0, 6 * Theme.S));
    });

    private static void Delta(string label, int value)
    {
        ImGui.TextColored(Theme.TextDim, label);
        var txt = value == 0 ? "—" : $"{(value > 0 ? "+" : "")}{value}";
        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(Theme.PanelRight - ImGui.CalcTextSize(txt).X, ImGui.GetCursorScreenPos().Y));
        ImGui.TextColored(value > 0 ? Theme.Green : value < 0 ? Theme.Red : Theme.Gray, txt);
    }

    private Snapshot Build()
    {
        var level = (int)Plugin.PlayerState.Level;

        var trackable = data.Duties.Where(d => d.InstanceContentId != 0 && d.UnlockQuestId != 0).ToList();
        var unlocked = trackable.Count(PlayerInfo.IsDutyUnlocked);
        var done = trackable.Count(PlayerInfo.IsDutyCompleted);

        var unlockQuests = data.Quests.Values.Where(q => q.Unlocks.Length > 0).ToList();
        var questsDone = unlockQuests.Count(q => PlayerInfo.IsQuestDone(q.Id));

        var next = unlockQuests
            .Where(q => q.Level <= level)
            .Select(q => (q, s: PlayerInfo.GetQuestState(q)))
            .Where(x => x.s is QuestState.Available or QuestState.Accepted)
            .OrderBy(x => x.s == QuestState.Accepted ? 0 : 1)
            .ThenByDescending(x => x.q.Level)
            .Take(8)
            .Select(x => x.q)
            .ToList();

        var soon = trackable
            .Where(d => d.Level <= level && !PlayerInfo.IsDutyUnlocked(d))
            .OrderByDescending(d => d.Level)
            .Take(8)
            .Select(d => (d, PlayerInfo.NextStepTowards(data, d.UnlockQuestId)))
            .ToList();

        return new Snapshot(trackable.Count, unlocked, done, unlockQuests.Count, questsDone, next, soon,
            StoryHelper.CurrentMsq(data), ProgressDiary.AverageItemLevel());
    }
}
