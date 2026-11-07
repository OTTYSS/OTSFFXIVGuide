using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

/// <summary>Отрисовка механик боссов: по боссам, с подсветкой того, что касается твоей роли.</summary>
internal sealed class MechanicsView
{
    private static readonly string[] FilterNames = { "Все механики", "Только для моей роли" };
    private int filter;

    public void Draw(DutyMechanics m, string id, bool compact = false)
    {
        var role = MechanicsDb.CurrentRole();
        if (role != PlayerRole.Any)
        {
            Theme.Chips($"mf{id}", FilterNames, ref filter);
            ImGui.SameLine(0, 12 * Theme.S);
            ImGui.TextColored(Theme.TextDim, $"Твоя роль: {RoleName(role)}");
        }

        foreach (var boss in m.Bosses)
        {
            var list = boss.Mechanics.Where(x => filter == 0 || x.ForRole(role)).ToList();
            if (list.Count == 0) continue;

            Theme.SubHeader(boss.Name);
            foreach (var mech in list)
            {
                var mine = role != PlayerRole.Any && mech.ForRole(role) && !mech.Roles.Contains("all");
                var (icon, color) = Icon(mech);
                Theme.FaIcon(icon, color);
                ImGui.SameLine();
                ImGui.BeginGroup();
                ImGui.TextColored(mine ? Theme.Gold : Theme.Text, mech.Ability);
                ImGui.SameLine();
                foreach (var r in mech.Roles)
                {
                    Theme.InlineBadge(RoleShort(r), RoleColor(r));
                    ImGui.SameLine();
                }
                ImGui.NewLine();
                if (!compact)
                    ImGui.TextColored(Theme.TextDim, mech.What);
                ImGui.TextColored(mine ? Theme.Gold : Theme.Green, "→ " + mech.Tip);
                ImGui.EndGroup();
                ImGui.Dummy(new Vector2(0, 2 * Theme.S));
            }
        }

        ImGui.TextColored(Theme.Gray, "Источник: ffxiv.consolegameswiki.com, кратко пересказано.");
    }

    private static (FontAwesomeIcon, Vector4) Icon(MechanicEntry m)
    {
        if (m.Roles.Contains("tank")) return (FontAwesomeIcon.ShieldAlt, Theme.Blue);
        if (m.Roles.Contains("healer")) return (FontAwesomeIcon.Star, Theme.Green);
        if (m.Roles.Contains("dps")) return (FontAwesomeIcon.Crosshairs, Theme.Red);
        return (FontAwesomeIcon.ArrowRight, Theme.Yellow);
    }

    public static string RoleName(PlayerRole r) => r switch
    {
        PlayerRole.Tank => "танк",
        PlayerRole.Healer => "хил",
        PlayerRole.Dps => "ДПС",
        _ => "любая",
    };

    private static string RoleShort(string r) => r switch
    {
        "tank" => "танк",
        "healer" => "хил",
        "dps" => "ДПС",
        _ => "все",
    };

    private static Vector4 RoleColor(string r) => r switch
    {
        "tank" => Theme.Blue,
        "healer" => Theme.Green,
        "dps" => Theme.Red,
        _ => Theme.TextDim,
    };
}
