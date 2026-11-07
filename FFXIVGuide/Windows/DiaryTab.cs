using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

/// <summary>Дневник прогресса: что изменилось за неделю/месяц, с графиком.</summary>
internal sealed class DiaryTab
{
    private sealed record Metric(string Name, FontAwesomeIcon Icon, Func<DiarySnapshot, double> Value, string Format);

    private static readonly Metric[] Metrics =
    {
        new("Уровни профессий (сумма)", FontAwesomeIcon.ArrowCircleUp, s => s.JobLevelsTotal, "0"),
        new("Сюжетные квесты", FontAwesomeIcon.Book, s => s.MsqDone, "0"),
        new("Пройдено данжей и боёв", FontAwesomeIcon.Dungeon, s => s.DutiesDone, "0"),
        new("Квесты-анлоки", FontAwesomeIcon.LockOpen, s => s.UnlocksDone, "0"),
        new("Средний iLvl", FontAwesomeIcon.ShieldAlt, s => s.AvgIlvl, "0"),
        new("Гил", FontAwesomeIcon.Coins, s => s.Gil, "N0"),
        new("Достижения", FontAwesomeIcon.Trophy, s => s.Achievements, "0"),
        new("Маунты", FontAwesomeIcon.Paw, s => s.Mounts, "0"),
        new("Минёны", FontAwesomeIcon.Star, s => s.Minions, "0"),
    };

    private static readonly string[] Periods = { "7 дней", "30 дней", "Всё время" };
    private static readonly int[] PeriodDays = { 7, 30, 100000 };

    private int period;
    private int metric;

    public void Draw()
    {
        Theme.Header("Дневник прогресса", "Что изменилось за неделю и месяц. Снимок записывается сам раз в 10 минут.");

        if (!PlayerInfo.Ready)
        {
            Theme.Panel("dnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем."));
            return;
        }

        var history = ProgressDiary.History;
        if (history.Count == 0)
        {
            Theme.EmptyState(FontAwesomeIcon.ChartLine, "Дневник только начался", "Первый снимок появится через минуту после входа. Чем дольше плагин включён, тем интереснее график.");
            return;
        }

        Theme.Chips("dperiod", Periods, ref period);
        var from = DateTime.Now.Date.AddDays(-PeriodDays[period]);
        var range = history.Where(s => s.Date >= from).ToList();
        if (range.Count == 0) range = history.TakeLast(1).ToList();
        var first = range.First();
        var last = range.Last();

        if (!ImGui.BeginChild("##diary", Vector2.Zero))
        {
            ImGui.EndChild();
            return;
        }

        // Карточки изменений
        Theme.Panel("dsum", () =>
        {
            Theme.SubHeader($"С {first.Date:dd.MM} по {last.Date:dd.MM}");
            var colW = (ImGui.GetContentRegionAvail().X - 16 * Theme.S) / 3;
            for (var i = 0; i < Metrics.Length; i++)
            {
                if (i % 3 != 0) ImGui.SameLine();
                var m = Metrics[i];
                var a = m.Value(first);
                var b = m.Value(last);
                if (m.Name == "Достижения" && (a < 0 || b < 0)) { StatCard(m, "открой окно достижений", "", Theme.Gray, colW, i); continue; }
                var delta = b - a;
                var deltaText = delta == 0 ? "без изменений" : $"{(delta > 0 ? "+" : "")}{delta.ToString(m.Format)}";
                StatCard(m, b.ToString(m.Format), deltaText, delta > 0 ? Theme.Green : delta < 0 ? Theme.Red : Theme.Gray, colW, i);
            }
        });

        // Уровни по профессиям
        var grown = last.Jobs.Where(kv => kv.Value > first.Jobs.GetValueOrDefault(kv.Key)).ToList();
        if (grown.Count > 0)
            Theme.Panel("djobs", () =>
            {
                Theme.SubHeader("Прокачались");
                foreach (var (job, lvl) in grown.OrderByDescending(kv => kv.Value - first.Jobs.GetValueOrDefault(kv.Key)))
                {
                    var was = first.Jobs.GetValueOrDefault(job);
                    Theme.InlineBadge(job, Theme.Gold);
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.Text, $"{was} → {lvl}");
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.Green, $"+{lvl - was}");
                }
            });

        // График
        Theme.Panel("dchart", () =>
        {
            Theme.SubHeader("График");
            var names = Metrics.Select(m => m.Name).ToArray();
            ImGui.SetNextItemWidth(260 * Theme.S);
            if (ImGui.BeginCombo("##dmetric", names[metric]))
            {
                for (var i = 0; i < names.Length; i++)
                    if (ImGui.Selectable(names[i], metric == i)) metric = i;
                ImGui.EndCombo();
            }
            Chart(range, Metrics[metric]);
        });

        ImGui.EndChild();
    }

    private static void StatCard(Metric m, string value, string delta, Vector4 deltaColor, float width, int i)
    {
        var S = Theme.S;
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, 58 * S);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + size, Theme.U32(Theme.CardBg), 8f * S);
        ImGui.PushFont(UiBuilder.IconFont);
        dl.AddText(pos + new Vector2(10 * S, 8 * S), Theme.U32(Theme.GoldDim), m.Icon.ToIconString());
        ImGui.PopFont();
        dl.AddText(pos + new Vector2(34 * S, 7 * S), Theme.U32(Theme.TextDim), m.Name);
        dl.AddText(pos + new Vector2(10 * S, 8 * S + ImGui.GetTextLineHeight() + 4 * S), Theme.U32(Theme.Text), value);
        if (delta.Length > 0)
        {
            var ds = ImGui.CalcTextSize(delta);
            dl.AddText(pos + new Vector2(width - ds.X - 10 * S, 8 * S + ImGui.GetTextLineHeight() + 4 * S), Theme.U32(deltaColor), delta);
        }
        ImGui.Dummy(size);
    }

    private static void Chart(List<DiarySnapshot> range, Metric m)
    {
        var S = Theme.S;
        var values = range.Select(m.Value).ToList();
        if (m.Name == "Достижения") values = values.Select(v => Math.Max(0, v)).ToList();
        var pos = ImGui.GetCursorScreenPos() + new Vector2(0, 6 * S);
        var size = new Vector2(ImGui.GetContentRegionAvail().X, 160 * S);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + size, Theme.U32(Theme.CardBg), 8f * S);

        if (values.Count < 2)
        {
            dl.AddText(pos + new Vector2(12 * S), Theme.U32(Theme.TextDim), "Нужно хотя бы два дня записей, чтобы нарисовать график.");
            ImGui.Dummy(size + new Vector2(0, 10 * S));
            return;
        }

        var min = values.Min();
        var max = values.Max();
        if (Math.Abs(max - min) < 1e-6) { max += 1; min -= 1; }
        var pad = new Vector2(44 * S, 16 * S);
        var area = size - pad * 2;
        Vector2 Point(int i) => pos + pad + new Vector2(area.X * i / (values.Count - 1), area.Y * (float)(1 - (values[i] - min) / (max - min)));

        // Сетка и подписи
        for (var g = 0; g <= 2; g++)
        {
            var y = pos.Y + pad.Y + area.Y * g / 2;
            dl.AddLine(new Vector2(pos.X + pad.X, y), new Vector2(pos.X + size.X - pad.X, y), Theme.U32(Theme.Alpha(Theme.Border, 0.6f)));
            var v = max - (max - min) * g / 2;
            dl.AddText(new Vector2(pos.X + 6 * S, y - ImGui.GetTextLineHeight() / 2), Theme.U32(Theme.Gray), v.ToString(m.Format == "N0" ? "N0" : "0"));
        }

        for (var i = 1; i < values.Count; i++)
            dl.AddLine(Point(i - 1), Point(i), Theme.U32(Theme.Gold), 2.5f * S);
        for (var i = 0; i < values.Count; i++)
            dl.AddCircleFilled(Point(i), 3.5f * S, Theme.U32(Theme.Gold));

        dl.AddText(pos + new Vector2(pad.X, size.Y - pad.Y + 2 * S), Theme.U32(Theme.Gray), range.First().Date.ToString("dd.MM"));
        var endLabel = range.Last().Date.ToString("dd.MM");
        dl.AddText(pos + new Vector2(size.X - pad.X - ImGui.CalcTextSize(endLabel).X, size.Y - pad.Y + 2 * S), Theme.U32(Theme.Gray), endLabel);

        ImGui.Dummy(size + new Vector2(0, 10 * S));
    }
}
