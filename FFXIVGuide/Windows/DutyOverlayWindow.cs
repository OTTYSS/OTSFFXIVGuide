using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Lumina.Excel.Sheets;
using Dalamud.Interface.Windowing;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

/// <summary>
/// Небольшое окно с механиками, которое само открывается при входе в данж/триал
/// из базы и закрывается при выходе. Только текст-подсказка.
/// </summary>
public sealed class DutyOverlayWindow : Window
{
    private readonly MechanicsView view = new();
    private DutyMechanics? current;
    private string dutyName = "";

    /// <summary>Окно нужно в текущей зоне (сам показывается, если окна не объединены).</summary>
    public bool Wanted { get; private set; }

    // Подсказка по текущему касту врага: показываем ещё пару секунд после окончания каста
    private (string Ability, string Caster, MechanicEntry Mech, float Left, float Total)? casting;
    private DateTime castSeen = DateTime.MinValue;

    public DutyOverlayWindow(GuideData data) : base("Механики###FFXIVGuideOverlay")
    {
        Size = new Vector2(420, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(300, 160), MaximumSize = new Vector2(2000, 2000) };
    }

    public void OnTerritoryChanged(uint territory)
    {
        var found = MechanicsDb.FindByTerritory(territory);
        if (found is null)
        {
            current = null;
            Wanted = false;
            IsOpen = false;
            return;
        }

        current = found.Value.Mechanics;
        dutyName = found.Value.DutyName;
        WindowName = $"Механики: {dutyName}###FFXIVGuideOverlay";
        Wanted = Plugin.Config.DutyOverlay;
        if (Wanted)
            IsOpen = true;
    }

    public override void PreDraw() => Theme.Push();
    public override void PostDraw() => Theme.Pop();

    public override void Draw() => DrawContent();

    public void DrawContent()
    {
        if (current is null)
        {
            ImGui.TextColored(Theme.TextDim, "Ты не в данже из базы механик. Окно откроется само при входе.");
            return;
        }
        DrawCastAlert();
        view.Draw(current, "overlay", compact: true);
        ImGui.Dummy(new Vector2(0, 4 * Theme.S));
        ImGui.TextColored(Theme.Gray, "Отключить автопоказ: /fguide settings");
    }

    /// <summary>Если враг кастует способность из базы — крупно показываем, что делать.</summary>
    private void DrawCastAlert()
    {
        var found = FindCast();
        if (found is not null)
        {
            casting = found;
            castSeen = DateTime.Now;
        }
        else if (DateTime.Now - castSeen > TimeSpan.FromSeconds(2))
        {
            casting = null;
        }

        if (casting is not { } c) return;

        var role = MechanicsDb.CurrentRole();
        var mine = c.Mech.ForRole(role);
        Theme.Panel("cast", () =>
        {
            Theme.LargeText(mine ? Theme.Gold : Theme.Text, $"Сейчас: {c.Ability}");
            ImGui.TextColored(Theme.TextDim, $"{c.Caster} · {c.Mech.What}");
            Theme.LargeText(mine ? Theme.Gold : Theme.Green, "→ " + c.Mech.Tip);
            if (c.Total > 0)
                ImGui.ProgressBar(Math.Clamp(1f - c.Left / c.Total, 0f, 1f), new Vector2(-1, 6 * Theme.S), "");
        });
    }

    private (string, string, MechanicEntry, float, float)? FindCast()
    {
        if (current is null) return null;
        var actions = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        foreach (var obj in Plugin.Objects)
        {
            if (obj is not IBattleNpc npc || !npc.IsCasting) continue;
            var name = actions.GetRowOrDefault(npc.CastActionId)?.Name.ExtractText();
            if (string.IsNullOrEmpty(name)) continue;
            foreach (var boss in current.Bosses)
            {
                var mech = boss.Mechanics.FirstOrDefault(m => Same(m.Ability, name));
                if (mech is not null)
                    return (name, npc.Name.TextValue, mech, npc.TotalCastTime - npc.CurrentCastTime, npc.TotalCastTime);
            }
        }
        return null;
    }

    private static bool Same(string a, string b)
    {
        static string N(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var na = N(a);
        var nb = N(b);
        return na.Length > 2 && (na == nb || na.Contains(nb) && nb.Length > 4);
    }
}
