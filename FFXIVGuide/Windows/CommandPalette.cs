using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace FFXIVGuide.Windows;

/// <summary>
/// Командная палитра (Ctrl+K): быстрый переход к любой вкладке, недавней карточке вики или действию с клавиатуры.
/// </summary>
internal sealed class CommandPalette
{
    public sealed record Command(string Title, string Sub, FontAwesomeIcon Icon, Action Run, string Keywords = "");

    private readonly Func<IEnumerable<Command>> source;
    private readonly Action<string> search;
    private string query = "";
    private int selected;
    private bool openRequested, focus;
    private List<Command> all = new();

    public CommandPalette(Func<IEnumerable<Command>> source, Action<string> search)
    {
        this.source = source;
        this.search = search;
    }

    public bool IsOpen { get; private set; }

    public void Open()
    {
        openRequested = true;
        query = "";
        selected = 0;
    }

    // Набрал в английской раскладке вместо русской (и наоборот) — тоже найдём
    private const string En = "qwertyuiop[]asdfghjkl;'zxcvbnm,.`";
    private const string Ru = "йцукенгшщзхъфывапролджэячсмитьбюё";

    private static string Swap(string s) => new(s.Select(c =>
    {
        var i = En.IndexOf(c);
        if (i >= 0) return Ru[i];
        i = Ru.IndexOf(c);
        return i >= 0 ? En[i] : c;
    }).ToArray());

    private static int Score(Command c, string q)
    {
        if (q.Length == 0) return 1;
        var title = c.Title.ToLowerInvariant();
        var hay = (c.Title + " " + c.Sub + " " + c.Keywords).ToLowerInvariant();
        if (title.StartsWith(q)) return 100;
        if (title.Contains(q)) return 80;
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.All(hay.Contains)) return 60;
        // Буквы по порядку («сбр» → «Сбор»)
        var i = 0;
        foreach (var ch in title)
            if (i < q.Length && ch == q[i]) i++;
        return i == q.Length && q.Length >= 2 ? 30 : 0;
    }

    private List<Command> Results()
    {
        var q = query.Trim().ToLowerInvariant();
        var alt = Swap(q);
        var res = all.Select(c => (c, s: Math.Max(Score(c, q), alt == q ? 0 : Score(c, alt) - 5)))
            .Where(x => x.s > 0).OrderByDescending(x => x.s).Select(x => x.c).Take(40).ToList();
        if (q.Length > 0)
            res.Add(new Command($"Искать «{query.Trim()}» в справочнике", "Данжи, квесты, предметы, мобы…", FontAwesomeIcon.Search, () => search(query.Trim())));
        return res;
    }

    public void Draw()
    {
        var S = Theme.S;
        if (openRequested)
        {
            openRequested = false;
            all = source().ToList();
            ImGui.OpenPopup("##cmdpalette");
            focus = true;
        }

        var vp = ImGui.GetWindowPos();
        var vs = ImGui.GetWindowSize();
        var w = Math.Min(620 * S, vs.X - 40 * S);
        ImGui.SetNextWindowPos(new Vector2(vp.X + (vs.X - w) / 2, vp.Y + 60 * S));
        ImGui.SetNextWindowSize(new Vector2(w, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 10) * S);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.PanelBg);
        IsOpen = ImGui.BeginPopup("##cmdpalette", ImGuiWindowFlags.NoMove);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        if (!IsOpen) return;

        Theme.FaIcon(FontAwesomeIcon.Search, Theme.Gold);
        ImGui.SameLine();
        if (focus) { ImGui.SetKeyboardFocusHere(); focus = false; }
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##cmdq", "Куда перейти или что сделать… (вкладка, карточка, действие)", ref query, 128)) selected = 0;

        var results = Results();
        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow)) selected = Math.Min(selected + 1, results.Count - 1);
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow)) selected = Math.Max(selected - 1, 0);
        var run = ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter);
        if (ImGui.IsKeyPressed(ImGuiKey.Escape)) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }

        ImGui.Dummy(new Vector2(0, 2 * S));
        var rowH = 34 * S;
        var listH = Math.Min(results.Count, 10) * rowH + 4 * S;
        Command? chosen = null;
        if (ImGui.BeginChild("##cmdlist", new Vector2(0, Math.Max(listH, rowH))))
        {
            var dl = ImGui.GetWindowDrawList();
            for (var i = 0; i < results.Count; i++)
            {
                var c = results[i];
                var pos = ImGui.GetCursorScreenPos();
                var size = new Vector2(ImGui.GetContentRegionAvail().X, rowH);
                if (ImGui.InvisibleButton($"##cmd{i}", size)) chosen = c;
                var hov = ImGui.IsItemHovered();
                if (hov && ImGui.GetIO().MouseDelta != Vector2.Zero) selected = i;
                var sel = i == selected;
                if (sel) ImGui.SetScrollHereY();
                if (sel || hov) dl.AddRectFilled(pos, pos + size, Theme.U32(sel ? Theme.Alpha(Theme.Gold, 0.16f) : Theme.CardHover), 6f * S);
                ImGui.PushFont(UiBuilder.IconFont);
                var ic = c.Icon.ToIconString();
                var isz = ImGui.CalcTextSize(ic);
                dl.AddText(pos + new Vector2(10 * S + (18 * S - isz.X) / 2, (rowH - isz.Y) / 2), Theme.U32(sel ? Theme.Gold : Theme.TextDim), ic);
                ImGui.PopFont();
                var ty = pos.Y + (rowH - ImGui.GetTextLineHeight()) / 2;
                dl.AddText(new Vector2(pos.X + 38 * S, ty), Theme.U32(Theme.Text), c.Title);
                var subW = ImGui.CalcTextSize(c.Sub).X;
                dl.AddText(new Vector2(pos.X + size.X - subW - 10 * S, ty), Theme.U32(Theme.Gray), c.Sub);
            }
            if (results.Count == 0) ImGui.TextColored(Theme.TextDim, "Ничего не нашлось");
        }
        ImGui.EndChild();
        ImGui.TextColored(Theme.Gray, "↑/↓ — выбор · Enter — открыть · Esc — закрыть");

        if (run && selected < results.Count) chosen = results[selected];
        if (chosen is not null)
        {
            ImGui.CloseCurrentPopup();
            chosen.Run();
        }
        ImGui.EndPopup();
    }
}
