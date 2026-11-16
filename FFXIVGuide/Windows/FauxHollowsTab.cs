using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace FFXIVGuide.Windows;

/// <summary>
/// Подсказка для Faux Hollows: отмечаешь открытые клетки — показываем, где вероятнее мечи и сундук.
/// Мечи — фигура 2×3 или 3×2, сундук (Coffer или Gift Box) — 2×2, лиса — 1×1 и бывает не всегда. Открыть можно 11 клеток.
/// Только расчёт: в игре ничего не нажимается.
/// </summary>
internal sealed class FauxHollowsTab
{
    private enum Cell { Unknown, Empty, Blocked, Sword, Box, Fox }

    private static readonly (Cell Kind, string Name, FontAwesomeIcon Icon)[] Brushes =
    {
        (Cell.Blocked, "Закрыто", FontAwesomeIcon.Ban),
        (Cell.Empty, "Пусто", FontAwesomeIcon.Square),
        (Cell.Sword, "Мечи", FontAwesomeIcon.Khanda),
        (Cell.Box, "Сундук", FontAwesomeIcon.Gift),
        (Cell.Fox, "Лиса", FontAwesomeIcon.Paw),
        (Cell.Unknown, "Стереть", FontAwesomeIcon.Eraser),
    };

    private readonly Cell[] board = new Cell[36];
    private int brush;
    private double[]? pSword, pBox;
    private int configs;
    private bool dirty = true;

    private static IEnumerable<int[]> Rects(int w, int h)
    {
        for (var y = 0; y + h <= 6; y++)
            for (var x = 0; x + w <= 6; x++)
            {
                var cells = new int[w * h];
                var k = 0;
                for (var dy = 0; dy < h; dy++)
                    for (var dx = 0; dx < w; dx++)
                        cells[k++] = (y + dy) * 6 + x + dx;
                yield return cells;
            }
    }

    private static readonly List<int[]> SwordPlacements = Rects(3, 2).Concat(Rects(2, 3)).ToList();
    private static readonly List<int[]> BoxPlacements = Rects(2, 2).ToList();

    private void Solve()
    {
        dirty = false;
        pSword = new double[36];
        pBox = new double[36];
        configs = 0;
        bool Free(int c, Cell own) => board[c] == Cell.Unknown || board[c] == own;
        var swordMarks = Enumerable.Range(0, 36).Where(i => board[i] == Cell.Sword).ToList();
        var boxMarks = Enumerable.Range(0, 36).Where(i => board[i] == Cell.Box).ToList();
        foreach (var s in SwordPlacements)
        {
            if (!s.All(c => Free(c, Cell.Sword)) || !swordMarks.All(s.Contains)) continue;
            foreach (var b in BoxPlacements)
            {
                if (b.Any(s.Contains) || !b.All(c => Free(c, Cell.Box)) || !boxMarks.All(b.Contains)) continue;
                configs++;
                foreach (var c in s) pSword[c]++;
                foreach (var c in b) pBox[c]++;
            }
        }
        if (configs == 0) return;
        for (var i = 0; i < 36; i++) { pSword[i] /= configs; pBox[i] /= configs; }
    }

    public void Draw()
    {
        Theme.Header("Faux Hollows", "Еженедельная мини-игра Unreal-триала: отмечай открытые клетки — подскажу, куда нажать дальше");
        if (dirty) Solve();

        Theme.Panel("fhhelp", () =>
        {
            ImGui.TextColored(Theme.TextDim, "Мечи — фигура 2×3 (стоят вдоль или поперёк), сундук (Coffer или Gift Box) — 2×2, лиса — одна клетка и появляется не всегда. Открыть можно 11 клеток.");
            ImGui.TextColored(Theme.TextDim, "1) Отметь все заблокированные клетки. 2) Нажимай в игре клетку с золотой рамкой и отмечай здесь, что открылось.");
        });

        // Кисти
        for (var i = 0; i < Brushes.Length; i++)
        {
            if (i > 0) ImGui.SameLine();
            var sel = brush == i;
            if (sel) ImGui.PushStyleColor(ImGuiCol.Button, Theme.Alpha(Theme.Gold, 0.35f));
            if (Theme.IconButton($"fhb{i}", Brushes[i].Icon, Brushes[i].Name)) brush = i;
            if (sel) ImGui.PopStyleColor();
        }
        ImGui.SameLine();
        if (Theme.IconButton("fhreset", FontAwesomeIcon.Redo, "Новая доска")) { Array.Clear(board); dirty = true; }

        var revealed = board.Count(c => c is Cell.Empty or Cell.Sword or Cell.Box or Cell.Fox);
        var blocked = board.Count(c => c == Cell.Blocked);

        // Лучший ход: максимум вероятности найти мечи или сундук
        var best = -1;
        var bestP = 0.0;
        if (configs > 0)
            for (var i = 0; i < 36; i++)
            {
                if (board[i] != Cell.Unknown) continue;
                var p = pSword![i] + pBox![i];
                if (p > bestP + 1e-9) { bestP = p; best = i; }
            }

        var S = Theme.S;
        var cell = Math.Min(64 * S, (ImGui.GetContentRegionAvail().X - 20 * S) / 6);
        var origin = ImGui.GetCursorScreenPos() + new Vector2(0, 6 * S);
        var dl = ImGui.GetWindowDrawList();
        for (var i = 0; i < 36; i++)
        {
            var x = i % 6;
            var y = i / 6;
            var pos = origin + new Vector2(x * (cell + 4 * S), y * (cell + 4 * S));
            ImGui.SetCursorScreenPos(pos);
            if (ImGui.InvisibleButton($"##fh{i}", new Vector2(cell)))
            {
                board[i] = board[i] == Brushes[brush].Kind ? Cell.Unknown : Brushes[brush].Kind;
                dirty = true;
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) { board[i] = Cell.Unknown; dirty = true; }
            var hov = ImGui.IsItemHovered();
            var bg = board[i] switch
            {
                Cell.Empty => Theme.CardBg,
                Cell.Blocked => Theme.Alpha(Theme.Gray, 0.5f),
                Cell.Sword => Theme.Alpha(Theme.Blue, 0.55f),
                Cell.Box => Theme.Alpha(Theme.Gold, 0.55f),
                Cell.Fox => Theme.Alpha(Theme.Red, 0.55f),
                _ => Theme.Mix(Theme.PanelBg, Theme.Gold, configs > 0 ? (float)Math.Min(1, (pSword![i] + pBox![i]) * 0.9) * 0.45f : 0f),
            };
            dl.AddRectFilled(pos, pos + new Vector2(cell), Theme.U32(hov ? Theme.Mix(bg, Theme.Text, 0.12f) : bg), 6f * S);
            dl.AddRect(pos, pos + new Vector2(cell), Theme.U32(i == best ? Theme.Gold : Theme.Border), 6f * S, ImDrawFlags.None, i == best ? 3f * S : 1f);
            string label;
            if (board[i] == Cell.Unknown)
                label = configs > 0 ? $"{(pSword![i] + pBox![i]) * 100:0}%" : "";
            else
            {
                ImGui.PushFont(UiBuilder.IconFont);
                label = Brushes.First(b => b.Kind == board[i]).Icon.ToIconString();
                var ts0 = ImGui.CalcTextSize(label);
                dl.AddText(pos + (new Vector2(cell) - ts0) / 2, Theme.U32(Theme.Text), label);
                ImGui.PopFont();
                label = "";
            }
            if (label.Length > 0)
            {
                var ts = ImGui.CalcTextSize(label);
                dl.AddText(pos + (new Vector2(cell) - ts) / 2, Theme.U32(i == best ? Theme.Gold : Theme.TextDim), label);
            }
            if (hov && board[i] == Cell.Unknown && configs > 0)
                ImGui.SetTooltip($"Мечи: {pSword![i] * 100:0}% · сундук: {pBox![i] * 100:0}%");
        }
        ImGui.SetCursorScreenPos(origin + new Vector2(0, 6 * (cell + 4 * S) + 8 * S));

        Theme.Panel("fhstat", () =>
        {
            ImGui.TextColored(revealed >= 11 ? Theme.Red : Theme.Text, $"Открыто клеток: {revealed} из 11");
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextDim, $"· закрытых отмечено: {blocked}");
            if (configs == 0)
                ImGui.TextColored(Theme.Red, "Такой доски не бывает — проверь отметки (мечи 2×3, сундук 2×2).");
            else if (best >= 0)
                ImGui.TextColored(Theme.Gold, $"Дальше открой: ряд {best / 6 + 1}, столбец {best % 6 + 1} ({bestP * 100:0}% найти мечи или сундук). Вариантов доски: {configs}.");
            else
                ImGui.TextColored(Theme.Green, "Мечи и сундук найдены ✓ Остальные открытия — на удачу: лиса может прятаться в любой свободной клетке.");
            ImGui.TextColored(Theme.Gray, "ЛКМ — поставить выбранную отметку, ПКМ — стереть. Плагин только считает, в игре ничего не нажимает.");
        });
    }
}
