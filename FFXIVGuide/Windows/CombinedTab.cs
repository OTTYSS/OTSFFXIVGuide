using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace FFXIVGuide.Windows;

/// <summary>Объединённая вкладка: общий заголовок, кнопки разделов сверху, внутри — бывшие отдельные вкладки.</summary>
internal sealed class CombinedTab
{
    private readonly string title, subtitle;
    private readonly (string Name, Action Draw)[] sections;
    private int section;

    public CombinedTab(string title, string subtitle, params (string Name, Action Draw)[] sections)
    {
        this.title = title;
        this.subtitle = subtitle;
        this.sections = sections;
    }

    public void Show(int s) => section = Math.Clamp(s, 0, sections.Length - 1);

    public void Draw()
    {
        Theme.Header(title, subtitle);
        var names = Array.ConvertAll(sections, x => x.Name);
        Theme.Chips("cmb" + title, names, ref section);
        if (ImGui.BeginChild("##cmb" + title, Vector2.Zero))
        {
            Theme.SuppressHeader = true;
            try { sections[section].Draw(); }
            finally { Theme.SuppressHeader = false; }
        }
        ImGui.EndChild();
    }
}
