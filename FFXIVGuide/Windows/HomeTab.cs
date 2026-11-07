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
        if (Theme.IconButton("hcfg", FontAwesomeIcon.SlidersH, "Настроить плитки")) ImGui.OpenPopup("##hometiles");
        DrawTilePicker();

        if (!PlayerInfo.Ready)
        {
            Theme.Panel("hnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем — здесь появится прогресс и следующие шаги."));
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

        // ── Плитки: на широком окне 2–3 в ряд, на узком — столбиком
        var tiles = Plugin.Config.HomeTiles.Select(id => TileById(id)).Where(t => t is not null).Select(t => t!).ToArray();
        Theme.Grid("htiles", tiles.Length, 300f, i => tiles[i]());

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
        Theme.Panel(id, () =>
        {
            Theme.FaIcon(icon, Theme.Gold);
            ImGui.SameLine();
            ImGui.TextColored(Theme.GoldDim, title.ToUpperInvariant());
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            var bw = ImGui.CalcTextSize(FontAwesomeIcon.ArrowRight.ToIconString()).X + ImGui.GetStyle().FramePadding.X * 2;
            ImGui.SetCursorScreenPos(new Vector2(Theme.PanelRight - bw, ImGui.GetCursorScreenPos().Y));
            var clicked = ImGui.SmallButton($"{FontAwesomeIcon.ArrowRight.ToIconString()}##go{id}");
            ImGui.PopFont();
            if (clicked) go(target);
            Theme.Tooltip("Открыть раздел");
            ImGui.Dummy(new Vector2(0, 2 * Theme.S));
            content();
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

    private static void DrawTilePicker()
    {
        if (!ImGui.BeginPopup("##hometiles")) return;
        var list = Plugin.Config.HomeTiles;
        ImGui.TextColored(Theme.TextDim, "Галочка — показывать, стрелки — порядок");
        foreach (var (id, name) in AllTiles.OrderBy(t => list.IndexOf(t.Id) < 0 ? 100 : list.IndexOf(t.Id)))
        {
            var on = list.Contains(id);
            if (ImGui.Checkbox($"{name}##ht{id}", ref on))
            {
                if (on) list.Add(id); else list.Remove(id);
                Plugin.Config.Save();
            }
            if (!on) continue;
            var idx = list.IndexOf(id);
            ImGui.SameLine(220 * Theme.S);
            if (ImGui.SmallButton($"↑##up{id}") && idx > 0) { (list[idx], list[idx - 1]) = (list[idx - 1], list[idx]); Plugin.Config.Save(); }
            ImGui.SameLine();
            if (ImGui.SmallButton($"↓##dn{id}") && idx < list.Count - 1) { (list[idx], list[idx + 1]) = (list[idx + 1], list[idx]); Plugin.Config.Save(); }
        }
        ImGui.EndPopup();
    }

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
