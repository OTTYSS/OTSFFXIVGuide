using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;

using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

/// <summary>Тема в духе «справочника»: тёмно-синий фон, золотой акцент, карточки.</summary>
internal static class Theme
{
    public static readonly Vector4 Bg = Rgb(0x14, 0x17, 0x22);
    public static readonly Vector4 PanelBg = Rgb(0x1B, 0x20, 0x2E);
    public static readonly Vector4 CardBg = Rgb(0x22, 0x28, 0x39);
    public static readonly Vector4 CardHover = Rgb(0x2B, 0x33, 0x48);
    /// <summary>Фон выбранной карточки — оттенок акцентного цвета.</summary>
    public static Vector4 CardSelected => Mix(CardBg, Gold, 0.18f);
    public static readonly Vector4 Border = Rgb(0x34, 0x3C, 0x52);
    // Акцентный цвет темы выбирается в настройках (по умолчанию золотой)
    public static readonly (string Name, Vector4 Color)[] Accents =
    {
        ("Золотой", Rgb(0xE8, 0xC1, 0x6A)),
        ("Синий", Rgb(0x6F, 0xB4, 0xFF)),
        ("Зелёный", Rgb(0x6C, 0xD6, 0x8E)),
        ("Фиолетовый", Rgb(0xB4, 0x8C, 0xFF)),
        ("Розовый", Rgb(0xF0, 0x8C, 0xC0)),
        ("Красный", Rgb(0xF0, 0x7A, 0x6A)),
    };

    public static Vector4 Gold => Accents[Math.Clamp(Plugin.Config.AccentColor, 0, Accents.Length - 1)].Color;
    public static Vector4 GoldDim => Mix(Gold, Rgb(0x14, 0x17, 0x22), 0.35f);

    public static Vector4 Mix(Vector4 a, Vector4 b, float t) => a + (b - a) * t;
    public static readonly Vector4 Text = Rgb(0xE6, 0xE8, 0xEE);
    public static readonly Vector4 TextDim = Rgb(0x8E, 0x96, 0xA8);
    public static readonly Vector4 Green = Rgb(0x5F, 0xD0, 0x7A);
    public static readonly Vector4 Yellow = Rgb(0xF0, 0xC8, 0x50);
    public static readonly Vector4 Blue = Rgb(0x6F, 0xB4, 0xFF);
    public static readonly Vector4 Red = Rgb(0xF0, 0x6E, 0x64);
    public static readonly Vector4 Gray = Rgb(0x6E, 0x75, 0x85);

    private const int ColorCount = 22;
    private const int VarCount = 9;

    public static float S => ImGuiHelpers.GlobalScale * Plugin.Config.UiScale;

    private static float savedFontScale = 1f;

    // ───────────── Шрифты: настоящие крупные размеры вместо растягивания ─────────────
    private static IFontHandle? titleFont, largeFont;

    /// <summary>Иконка текущего раздела — рисуется в заголовке.</summary>
    public static FontAwesomeIcon CurrentIcon { get; set; } = FontAwesomeIcon.Compass;

    public static void InitFonts()
    {
        var atlas = Plugin.PluginInterface.UiBuilder.FontAtlas;
        titleFont = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(26f)));
        largeFont = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(20f)));
    }

    public static void DisposeFonts()
    {
        titleFont?.Dispose();
        largeFont?.Dispose();
    }

    /// <summary>Крупный текст (имя квеста, предмета, данжа).</summary>
    public static void LargeText(Vector4 color, string text)
    {
        text = NameTranslations.Show(text);
        if (largeFont is { Available: true })
            using (largeFont.Push()) ImGui.TextColored(color, text);
        else
        {
            ImGui.SetWindowFontScale(1.2f);
            ImGui.TextColored(color, text);
            ImGui.SetWindowFontScale(1f);
        }
    }

    /// <summary>Крупный текст по центру точки (буква оценки и т.п.).</summary>
    public static void LargeTextAt(Vector2 center, string text, Vector4 color)
    {
        var dl = ImGui.GetWindowDrawList();
        if (titleFont is { Available: true })
        {
            using (titleFont.Push())
            {
                var ts = ImGui.CalcTextSize(text);
                dl.AddText(center - ts / 2, U32(color), text);
            }
        }
        else
        {
            var ts = ImGui.CalcTextSize(text);
            dl.AddText(center - ts / 2, U32(color), text);
        }
    }

    private static void TitleText(Vector4 color, string text)
    {
        if (titleFont is { Available: true })
            using (titleFont.Push()) ImGui.TextColored(color, text);
        else
        {
            ImGui.SetWindowFontScale(1.45f);
            ImGui.TextColored(color, text);
            ImGui.SetWindowFontScale(1f);
        }
    }

    public static Vector4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);
    public static Vector4 Alpha(Vector4 c, float a) => c with { W = a };
    public static uint U32(Vector4 c) => ImGui.GetColorU32(c);

    public static void Push()
    {
        // Масштаб текста только для окон плагина: возвращается в Pop()
        var io = ImGui.GetIO();
        savedFontScale = io.FontGlobalScale;
        io.FontGlobalScale = savedFontScale * Plugin.Config.UiScale;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, Alpha(Bg, 0.97f));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, PanelBg);
        ImGui.PushStyleColor(ImGuiCol.Border, Border);
        ImGui.PushStyleColor(ImGuiCol.Text, Text);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, TextDim);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, PanelBg);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, PanelBg);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, PanelBg);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, CardBg);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, CardHover);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, CardHover);
        ImGui.PushStyleColor(ImGuiCol.Button, CardBg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, CardHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Alpha(Gold, 0.35f));
        ImGui.PushStyleColor(ImGuiCol.Header, Alpha(Gold, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Alpha(Gold, 0.25f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Alpha(Gold, 0.35f));
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Gold);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Border);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Gold);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f * S);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f * S);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f * S);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 6f * S);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 6f * S);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 6f * S);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 10f * S);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 6) * S);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6) * S);
    }

    public static void Pop()
    {
        ImGui.GetIO().FontGlobalScale = savedFontScale;
        ImGui.PopStyleVar(VarCount);
        ImGui.PopStyleColor(ColorCount);
    }

    // ───────────── виджеты ─────────────

    public static void FaIcon(FontAwesomeIcon icon, Vector4 color)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(color, icon.ToIconString());
        ImGui.PopFont();
    }

    public static void GameIcon(uint iconId, Vector2 pos, float size, float rounding = 4f)
    {
        if (iconId == 0) return;
        var wrap = Plugin.Textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
        ImGui.GetWindowDrawList().AddImageRounded(wrap.Handle, pos, pos + new Vector2(size), Vector2.Zero, Vector2.One, 0xFFFFFFFF, rounding * S);
    }

    /// <summary>Картинка из игры по ширине области, с сохранением пропорций.</summary>
    public static void Banner(uint iconId, float width)
    {
        if (iconId == 0) return;
        var wrap = Plugin.Textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
        if (wrap.Width <= 1) return;
        var h = width * wrap.Height / wrap.Width;
        var pos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddImageRounded(wrap.Handle, pos, pos + new Vector2(width, h), Vector2.Zero, Vector2.One, 0xFFFFFFFF, 8f * S);
        ImGui.Dummy(new Vector2(width, h));
    }

    /// <summary>Цветная «таблетка» со статусом. Рисуется в заданной точке, возвращает ширину.</summary>
    public static float Badge(Vector2 pos, string text, Vector4 color)
    {
        var size = ImGui.CalcTextSize(text);
        var pad = new Vector2(8, 2) * S;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + size + pad * 2, U32(Alpha(color, 0.18f)), 20f * S);
        dl.AddRect(pos, pos + size + pad * 2, U32(Alpha(color, 0.55f)), 20f * S);
        dl.AddText(pos + pad, U32(color), text);
        return size.X + pad.X * 2;
    }

    public static Vector2 BadgeSize(string text) => ImGui.CalcTextSize(text) + new Vector2(16, 4) * S;

    /// <summary>Badge в потоке вёрстки.</summary>
    public static void InlineBadge(string text, Vector4 color)
    {
        var pos = ImGui.GetCursorScreenPos();
        Badge(pos, text, color);
        ImGui.Dummy(BadgeSize(text));
    }

    /// <summary>
    /// Карточка-строка списка: иконка, заголовок, подзаголовок, бейдж справа.
    /// Возвращает true при клике.
    /// </summary>
    public static bool Card(string id, uint icon, string title, string subtitle, string? badge, Vector4 badgeColor, bool selected, float height)
    {
        title = NameTranslations.Show(title);
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var size = new Vector2(width, height);

        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        var hoverT = Anim("h" + id, hovered ? 1f : 0f, 14f);
        var bg = selected ? CardSelected : Mix(CardBg, CardHover, hoverT);
        dl.AddRectFilled(pos + new Vector2(0, 2 * S), pos + size + new Vector2(0, 2 * S), U32(Alpha(Rgb(0, 0, 0), 0.25f)), 8f * S);
        dl.AddRectFilled(pos, pos + size, U32(bg), 8f * S);
        dl.AddLine(pos + new Vector2(8 * S, 1), pos + new Vector2(size.X - 8 * S, 1), U32(Alpha(Text, hovered ? 0.10f : 0.05f)));
        if (hovered && !selected)
            dl.AddRect(pos, pos + size, U32(Alpha(Gold, 0.35f)), 8f * S);
        if (selected)
        {
            dl.AddRect(pos, pos + size, U32(Alpha(Gold, 0.7f)), 8f * S);
            dl.AddRectFilled(pos + new Vector2(0, 8 * S), pos + new Vector2(3 * S, height - 8 * S), U32(Gold), 2f * S);
        }

        var pad = 8f * S;
        var iconSize = height - pad * 2;
        var x = pos.X + pad;
        if (icon != 0)
        {
            GameIcon(icon, new Vector2(x, pos.Y + pad), iconSize);
            x += iconSize + pad * 1.5f;
        }

        var lineH = ImGui.GetTextLineHeight();
        var badgeW = 0f;
        if (badge is not null)
        {
            var bs = BadgeSize(badge);
            badgeW = bs.X + pad;
            Badge(new Vector2(pos.X + width - bs.X - pad, pos.Y + (height - bs.Y) / 2), badge, badgeColor);
        }

        var textMaxX = pos.X + width - badgeW - pad;
        var clip = new Vector4(x, pos.Y, textMaxX, pos.Y + height);
        var font = ImGui.GetFont();
        var fs = ImGui.GetFontSize();
        var top = subtitle.Length > 0 ? pos.Y + height / 2 - lineH - 1 * S : pos.Y + (height - lineH) / 2;
        dl.AddText(font, fs, new Vector2(x, top), U32(selected ? Gold : Text), title, 0f, ref clip);
        if (subtitle.Length > 0)
            dl.AddText(font, fs, new Vector2(x, pos.Y + height / 2 + 1 * S), U32(TextDim), subtitle, 0f, ref clip);

        return clicked;
    }

    /// <summary>Заголовок раздела: крупный текст + серый подзаголовок.</summary>
    public static void Header(string title, string subtitle)
    {
        var start = ImGui.GetCursorScreenPos();
        ImGui.PushFont(UiBuilder.IconFont);
        var iconStr = CurrentIcon.ToIconString();
        var iconSize = ImGui.CalcTextSize(iconStr);
        ImGui.PopFont();

        // Иконка в круге
        var circle = 34f * S;
        var dl = ImGui.GetWindowDrawList();
        var c = start + new Vector2(circle / 2, circle / 2 + 2 * S);
        dl.AddCircleFilled(c, circle / 2, U32(Alpha(Gold, 0.16f)));
        dl.AddCircle(c, circle / 2, U32(Alpha(Gold, 0.55f)), 0, 1.2f * S);
        ImGui.PushFont(UiBuilder.IconFont);
        dl.AddText(c - iconSize / 2, U32(Gold), iconStr);
        ImGui.PopFont();

        ImGui.SetCursorScreenPos(start + new Vector2(circle + 12 * S, 0));
        ImGui.BeginGroup();
        TitleText(Text, title);
        if (subtitle.Length > 0)
            ImGui.TextColored(TextDim, subtitle);
        ImGui.EndGroup();

        // Золотая линия, затухающая вправо
        var y = Math.Max(ImGui.GetItemRectMax().Y, start.Y + circle) + 6 * S;
        var w = ImGui.GetContentRegionAvail().X;
        dl.AddRectFilledMultiColor(new Vector2(start.X, y), new Vector2(start.X + w, y + 2 * S),
            U32(Alpha(Gold, 0.8f)), U32(Alpha(Gold, 0f)), U32(Alpha(Gold, 0f)), U32(Alpha(Gold, 0.8f)));
        ImGui.SetCursorScreenPos(new Vector2(start.X, y + 10 * S));
        ImGui.Dummy(Vector2.Zero);
    }

    /// <summary>Небольшой заголовок блока внутри раздела.</summary>
    public static void SubHeader(string text)
    {
        ImGui.Dummy(new Vector2(0, 4 * S));
        var mp = ImGui.GetCursorScreenPos();
        var lh = ImGui.GetTextLineHeight();
        ImGui.GetWindowDrawList().AddRectFilled(mp + new Vector2(0, 3 * S), mp + new Vector2(3 * S, lh - 3 * S), U32(Gold), 1.5f * S);
        ImGui.SetCursorScreenPos(mp + new Vector2(9 * S, 0));
        ImGui.TextColored(GoldDim, text.ToUpperInvariant());
        var p = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(p, p + new Vector2(ImGui.GetContentRegionAvail().X, 0), U32(Border), 1f);
        ImGui.Dummy(new Vector2(0, 4 * S));
    }

    /// <summary>Чипы-переключатели (вместо выпадающего списка). Возвращает true при смене.</summary>
    public static bool Chips(string id, string[] options, ref int selected)
    {
        var changed = false;
        ImGui.PushID(id);
        for (var i = 0; i < options.Length; i++)
        {
            var size = BadgeSize(options[i]) + new Vector2(8, 6) * S;
            if (i > 0)
            {
                ImGui.SameLine();
                if (ImGui.GetContentRegionAvail().X < size.X) ImGui.NewLine();
            }

            var pos = ImGui.GetCursorScreenPos();
            if (ImGui.InvisibleButton($"##chip{i}", size))
            {
                selected = i;
                changed = true;
            }

            var active = selected == i;
            var hovered = ImGui.IsItemHovered();
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(pos, pos + size, U32(active ? Alpha(Gold, 0.22f) : hovered ? CardHover : CardBg), 20f * S);
            dl.AddRect(pos, pos + size, U32(active ? Gold : Border), 20f * S);
            var ts = ImGui.CalcTextSize(options[i]);
            dl.AddText(pos + (size - ts) / 2, U32(active ? Gold : TextDim), options[i]);
        }
        ImGui.PopID();
        return changed;
    }

    /// <summary>Поле поиска с иконкой лупы.</summary>
    public static void Search(string id, string hint, ref string text)
    {
        FaIcon(FontAwesomeIcon.Search, TextDim);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint(id, hint, ref text, 128);
    }

    /// <summary>Полоса прогресса с подписью.</summary>
    public static void Progress(string label, int done, int total, Vector4 color)
    {
        var f = Anim("p" + label, total == 0 ? 0f : (float)done / total);
        ImGui.TextColored(Text, label);
        ImGui.SameLine();
        ImGui.TextColored(TextDim, $"{done} / {total}");
        var pos = ImGui.GetCursorScreenPos();
        var w = Math.Min(ImGui.GetContentRegionAvail().X, MaxBarWidth);
        var h = 8f * S;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(w, h), U32(CardBg), 4f * S);
        if (f > 0)
            dl.AddRectFilled(pos, pos + new Vector2(Math.Max(w * f, h), h), U32(color), 4f * S);
        ImGui.Dummy(new Vector2(w, h + 4 * S));
    }

    /// <summary>Ширина левой колонки «список | карточка» (пользователь двигает разделитель, запоминается).</summary>
    public static float SplitWidth(string id, float total, float def)
    {
        var r = Plugin.Config.SplitRatios.TryGetValue(id, out var v) ? v : def;
        return Math.Clamp(r, 0.2f, 0.8f) * total;
    }

    /// <summary>Перетаскиваемый разделитель между списком и карточкой. Вызывать вместо SameLine после списка.</summary>
    public static void Splitter(string id, float total, float def)
    {
        ImGui.SameLine(0, 0);
        var h = Math.Max(1, ImGui.GetContentRegionAvail().Y);
        var pos = ImGui.GetCursorScreenPos();
        var w = 10 * S;
        ImGui.InvisibleButton($"##split{id}", new Vector2(w, h));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        if (hovered || active) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
        if (active && Math.Abs(ImGui.GetIO().MouseDelta.X) > 0)
        {
            var r = Plugin.Config.SplitRatios.TryGetValue(id, out var v) ? v : def;
            Plugin.Config.SplitRatios[id] = Math.Clamp(r + ImGui.GetIO().MouseDelta.X / Math.Max(1, total), 0.2f, 0.8f);
        }
        if (ImGui.IsItemDeactivated()) Plugin.Config.Save();
        if (ImGui.IsItemHovered() && !active) ImGui.SetTooltip("Потяни, чтобы изменить ширину списка. Двойной клик — сбросить.");
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { Plugin.Config.SplitRatios.Remove(id); Plugin.Config.Save(); }
        var x = pos.X + w / 2;
        ImGui.GetWindowDrawList().AddLine(new Vector2(x, pos.Y + 6 * S), new Vector2(x, pos.Y + h - 6 * S),
            U32(active ? Gold : hovered ? Alpha(Gold, 0.6f) : Alpha(Border, 0.6f)), (active ? 3f : 1.5f) * S);
        ImGui.SameLine(0, 0);
    }

    /// <summary>Полосы прогресса не растягиваются на всё окно — так их легче читать.</summary>
    public static float MaxBarWidth => 620f * S;

    // ───────────── Плавные анимации ─────────────
    private static readonly Dictionary<string, float> animated = new();

    /// <summary>Плавно приближает значение к цели (для заполнения полос и подсветки).</summary>
    public static float Anim(string id, float target, float speed = 6f)
    {
        var dt = Math.Min(ImGui.GetIO().DeltaTime, 0.1f);
        if (!animated.TryGetValue(id, out var cur)) cur = 0f; // появление: полосы плавно заполняются с нуля
        cur += (target - cur) * Math.Min(1f, dt * speed);
        if (Math.Abs(target - cur) < 0.001f) cur = target;
        animated[id] = cur;
        return cur;
    }

    // ───────────── Сетка ─────────────

    /// <summary>
    /// Раскладывает ячейки в колонки по ширине окна: на широком — 2–3 колонки, на узком — одна.
    /// </summary>
    public static void Grid(string id, int count, float minCellWidth, Action<int> drawCell)
    {
        if (count == 0) return;
        var avail = ImGui.GetContentRegionAvail().X;
        var cols = Math.Clamp((int)(avail / (minCellWidth * S)), 1, Math.Min(count, 3));
        if (!ImGui.BeginTable(id, cols, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX)) return;
        for (var i = 0; i < count; i++)
        {
            ImGui.TableNextColumn();
            ImGui.PushID(i);
            drawCell(i);
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    // ───────────── Кольцо прогресса ─────────────

    public static void Ring(Vector2 center, float radius, float frac, Vector4 color, string label, string id)
    {
        var dl = ImGui.GetWindowDrawList();
        var f = Anim("r" + id, Math.Clamp(frac, 0f, 1f));
        dl.AddCircle(center, radius, U32(CardBg), 48, 6f * S);
        var segs = Math.Max(1, (int)(48 * f));
        for (var i = 0; i < segs; i++)
        {
            var a1 = -MathF.PI / 2 + i * 2 * MathF.PI * f / segs;
            var a2 = -MathF.PI / 2 + (i + 1) * 2 * MathF.PI * f / segs;
            dl.AddLine(center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius,
                center + new Vector2(MathF.Cos(a2), MathF.Sin(a2)) * radius, U32(color), 6f * S);
        }
        var ts = ImGui.CalcTextSize(label);
        dl.AddText(center - ts / 2, U32(Text), label);
    }

    // ───────────── Пустое состояние ─────────────

    /// <summary>Крупная иконка и текст по центру вместо серой строки.</summary>
    public static void EmptyState(FontAwesomeIcon icon, string title, string text)
    {
        Panel("empty" + title, () =>
        {
            var w = ImGui.GetContentRegionAvail().X;
            ImGui.Dummy(new Vector2(0, 10 * S));
            ImGui.PushFont(UiBuilder.IconFont);
            var iconStr = icon.ToIconString();
            var isz = ImGui.CalcTextSize(iconStr);
            ImGui.PopFont();
            var pos = ImGui.GetCursorScreenPos();
            var c = new Vector2(pos.X + w / 2, pos.Y + 26 * S);
            var dl = ImGui.GetWindowDrawList();
            dl.AddCircleFilled(c, 26 * S, U32(Alpha(Gold, 0.12f)));
            ImGui.PushFont(UiBuilder.IconFont);
            dl.AddText(c - isz / 2, U32(Gold), iconStr);
            ImGui.PopFont();
            ImGui.Dummy(new Vector2(w, 58 * S));
            var ts = ImGui.CalcTextSize(title);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (w - ts.X) / 2));
            ImGui.TextColored(Text, title);
            ImGui.TextColored(TextDim, text);
            ImGui.Dummy(new Vector2(0, 6 * S));
        });
    }

    /// <summary>Строка прогресса, на которую можно нажать.</summary>
    public static bool ProgressButton(string id, string label, int done, int total, Vector4 color)
    {
        var pos = ImGui.GetCursorScreenPos();
        var w = Math.Min(ImGui.GetContentRegionAvail().X, MaxBarWidth);
        var lineH = ImGui.GetTextLineHeight();
        var barH = 8f * S;
        var pad = 6f * S;
        var size = new Vector2(w, pad + lineH + 4 * S + barH + pad);

        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        if (hovered)
            dl.AddRectFilled(pos, pos + size, U32(CardHover), 6f * S);

        var x = pos.X + pad;
        dl.AddText(new Vector2(x, pos.Y + pad), U32(hovered ? Gold : Text), label);
        var count = $"{done} / {total}";
        var cs = ImGui.CalcTextSize(count);
        dl.AddText(new Vector2(pos.X + w - pad - cs.X, pos.Y + pad), U32(TextDim), count);

        var barY = pos.Y + pad + lineH + 4 * S;
        var barW = w - pad * 2;
        dl.AddRectFilled(new Vector2(x, barY), new Vector2(x + barW, barY + barH), U32(CardBg), 4f * S);
        var f = Anim("pb" + id, total == 0 ? 0f : (float)done / total);
        if (f > 0)
            dl.AddRectFilled(new Vector2(x, barY), new Vector2(x + Math.Max(barW * f, barH), barY + barH), U32(color), 4f * S);
        return clicked;
    }

    /// <summary>Кнопка с иконкой FontAwesome и текстом.</summary>
    public static bool IconButton(string id, FontAwesomeIcon icon, string text)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        var iconStr = icon.ToIconString();
        var iconSize = ImGui.CalcTextSize(iconStr);
        ImGui.PopFont();
        var textSize = ImGui.CalcTextSize(text);
        var pad = new Vector2(10, 5) * S;
        var size = new Vector2(iconSize.X + 6 * S + textSize.X, Math.Max(iconSize.Y, textSize.Y)) + pad * 2;

        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + size, U32(hovered ? Alpha(Gold, 0.25f) : CardBg), 6f * S);
        dl.AddRect(pos, pos + size, U32(hovered ? Gold : Border), 6f * S);

        ImGui.PushFont(UiBuilder.IconFont);
        dl.AddText(pos + new Vector2(pad.X, (size.Y - iconSize.Y) / 2), U32(Gold), iconStr);
        ImGui.PopFont();
        dl.AddText(pos + new Vector2(pad.X + iconSize.X + 6 * S, (size.Y - textSize.Y) / 2), U32(Text), text);
        return clicked;
    }

    /// <summary>Блок с фоном-панелью вокруг произвольного содержимого.</summary>
    /// <summary>Правый край содержимого текущей панели (в экранных координатах).</summary>
    public static float PanelRight { get; private set; }

    public static void Panel(string id, Action content)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        var pad = 10f * S;
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorScreenPos(start + new Vector2(pad));
        ImGui.PushID(id);
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - pad * 2);
        var prevRight = PanelRight;
        PanelRight = start.X + width - pad;
        try
        {
            content();
        }
        catch
        {
            // Ошибка внутри панели: аккуратно закрываем её стек, чтобы окно не сломалось, и пробрасываем дальше
            PanelRight = prevRight;
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();
            ImGui.PopID();
            dl.ChannelsMerge();
            throw;
        }
        PanelRight = prevRight;
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        ImGui.PopID();
        var end = new Vector2(start.X + width, ImGui.GetItemRectMax().Y + pad);
        dl.ChannelsSetCurrent(0);
        dl.AddRectFilled(start + new Vector2(0, 3 * S), end + new Vector2(0, 3 * S), U32(Alpha(Rgb(0, 0, 0), 0.22f)), 8f * S);
        dl.AddRectFilled(start, end, U32(PanelBg), 8f * S);
        dl.AddRect(start, end, U32(Border), 8f * S);
        dl.AddLine(start + new Vector2(10 * S, 1), new Vector2(end.X - 10 * S, start.Y + 1), U32(Alpha(Text, 0.06f)));
        dl.ChannelsMerge();
        ImGui.SetCursorScreenPos(new Vector2(start.X, end.Y + 6 * S));
        ImGui.Dummy(Vector2.Zero);
    }

    public static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    /// <summary>
    /// Виртуальный список: рисует только видимые строки фиксированной высоты.
    /// </summary>
    // ───────────── Размер картинок в списках ─────────────

    private static readonly string[] PreviewNames = { "Мелкие", "Средние", "Крупные", "Плитки" };

    /// <summary>Высота строки списка по настройке размера картинок.</summary>
    public static float PreviewRowHeight => Plugin.Config.PreviewSize switch { 0 => 40f, 2 => 96f, _ => 62f } * S;

    /// <summary>Включены ли плитки с большими картинками (только там, где они поддерживаются).</summary>
    public static bool PreviewTiles(bool supported) => supported && Plugin.Config.PreviewSize >= 3;

    /// <summary>Переключатель размера картинок; tiles — показывать ли вариант «Плитки».</summary>
    public static void PreviewSizeChips(string id, bool tiles)
    {
        var names = tiles ? PreviewNames : PreviewNames[..3];
        var v = Math.Min(Plugin.Config.PreviewSize, names.Length - 1);
        ImGui.TextColored(TextDim, "Картинки:");
        ImGui.SameLine();
        if (Chips(id, names, ref v)) { Plugin.Config.PreviewSize = v; Plugin.Config.Save(); }
    }

    /// <summary>Сетка плиток с большими картинками; рисует только видимые ряды.</summary>
    public static void TileGrid(int count, float tileWidth, Func<int, (string Id, uint Image, uint Fallback, string Title, string? Badge, Vector4 BadgeColor, bool Selected)> item, Action<int> clicked)
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var gap = 8f * S;
        var cols = Math.Max(1, (int)((avail + gap) / (tileWidth + gap)));
        var w = (avail - gap * (cols - 1)) / cols;
        var h = w + ImGui.GetTextLineHeight() * 2 + 14 * S;
        var rows = (count + cols - 1) / cols;
        VirtualList(rows, h, r =>
        {
            for (var c = 0; c < cols; c++)
            {
                var i = r * cols + c;
                if (i >= count) break;
                if (c > 0) ImGui.SameLine(0, gap);
                var t = item(i);
                if (Tile(t.Id, t.Image, t.Fallback, t.Title, t.Badge, t.BadgeColor, t.Selected, w, h)) clicked(i);
            }
        });
    }

    private static bool Tile(string id, uint image, uint fallback, string title, string? badge, Vector4 badgeColor, bool selected, float w, float h)
    {
        title = NameTranslations.Show(title);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(w, h), U32(selected ? CardSelected : hovered ? CardHover : CardBg), 8f * S);
        if (selected || hovered) dl.AddRect(pos, pos + new Vector2(w, h), U32(Alpha(Gold, selected ? 0.7f : 0.35f)), 8f * S);
        var pad = 6f * S;
        var img = w - pad * 2;
        var wrap = Plugin.Textures.GetFromGameIcon(new GameIconLookup(image)).GetWrapOrEmpty();
        if (wrap.Width <= 1 && fallback != 0) wrap = Plugin.Textures.GetFromGameIcon(new GameIconLookup(fallback)).GetWrapOrEmpty();
        dl.AddImageRounded(wrap.Handle, pos + new Vector2(pad), pos + new Vector2(pad + img), Vector2.Zero, Vector2.One, 0xFFFFFFFF, 6f * S);
        if (badge is not null) Badge(pos + new Vector2(pad + 4 * S, pad + 4 * S), badge, badgeColor);
        // Название — до двух строк под картинкой
        var text = title;
        var maxW = w - pad * 2;
        var lines = new List<string>();
        while (text.Length > 0 && lines.Count < 2)
        {
            var n = text.Length;
            while (n > 1 && ImGui.CalcTextSize(text[..n]).X > maxW) n--;
            if (n < text.Length && lines.Count == 1 && n > 1) { lines.Add(text[..(n - 1)] + "…"); break; }
            var cut = n < text.Length ? text.LastIndexOf(' ', n - 1) : -1;
            if (cut <= 0) cut = n;
            lines.Add(text[..cut].Trim());
            text = text[cut..].Trim();
        }
        var y = pos.Y + pad + img + 4 * S;
        foreach (var l in lines)
        {
            var sz = ImGui.CalcTextSize(l);
            dl.AddText(new Vector2(pos.X + (w - sz.X) / 2, y), U32(hovered ? Gold : Text), l);
            y += ImGui.GetTextLineHeight();
        }
        if (hovered) ImGui.SetTooltip(title);
        return clicked;
    }

    public static void VirtualList(int count, float rowHeight, Action<int> drawRow)
    {
        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        var step = rowHeight + spacing;
        var scroll = ImGui.GetScrollY();
        var visible = ImGui.GetWindowHeight();
        var first = Math.Max(0, (int)(scroll / step) - 1);
        var last = Math.Min(count, first + (int)(visible / step) + 3);

        if (first > 0)
            ImGui.Dummy(new Vector2(0, first * step - spacing));
        for (var i = first; i < last; i++)
            drawRow(i);
        if (last < count)
            ImGui.Dummy(new Vector2(0, (count - last) * step - spacing));
    }
}
