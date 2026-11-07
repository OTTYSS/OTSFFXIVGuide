using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Utility;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>FATE текущей зоны и прогресс атэритов (Aether Currents) по зонам.</summary>
internal sealed class FateTab
{
    private static readonly string[] Parts = { "FATE в зоне", "Атэриты (полёт)" };
    private int part;
    private bool onlyMissing = true;

    public void Draw()
    {
        Theme.Header("FATE и атэриты", "Активные FATE рядом и сколько Aether Currents собрано для полёта");
        if (!PlayerInfo.Ready)
        {
            Theme.Panel("fnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем."));
            return;
        }

        Theme.Chips("fpart", Parts, ref part);
        if (!ImGui.BeginChild("##fate", Vector2.Zero))
        {
            ImGui.EndChild();
            return;
        }

        if (part == 0) DrawFates();
        else DrawCurrents();

        ImGui.EndChild();
    }

    private static void DrawFates()
    {
        var level = (int)Plugin.PlayerState.Level;
        var fates = Plugin.Fates.Where(f => f.State == FateState.Running).OrderBy(f => f.TimeRemaining).ToList();
        var mapId = Plugin.ClientState.MapId;
        var map = Plugin.Data.GetExcelSheet<Map>().GetRowOrDefault(mapId);

        Theme.Panel("flist", () =>
        {
            Theme.SubHeader($"Активные FATE · {fates.Count}");
            if (fates.Count == 0)
            {
                ImGui.TextColored(Theme.TextDim, "В этой зоне сейчас нет FATE.");
                return;
            }

            foreach (var f in fates)
            {
                ImGui.PushID(f.FateId);
                var pos = ImGui.GetCursorScreenPos();
                var h = ImGui.GetTextLineHeight() * 2.2f;
                Theme.GameIcon(f.IconId, pos, h, 4f);
                ImGui.SetCursorScreenPos(pos + new Vector2(h + 8 * Theme.S, 0));
                ImGui.BeginGroup();
                ImGui.TextColored(Theme.Text, f.Name.TextValue);
                ImGui.SameLine();
                var lvlColor = level > f.MaxLevel ? Theme.Gray : level >= f.Level ? Theme.Green : Theme.Red;
                Theme.InlineBadge($"ур. {f.Level}–{f.MaxLevel}", lvlColor);
                if (f.HasBonus) { ImGui.SameLine(); Theme.InlineBadge("бонус опыта", Theme.Gold); }

                var left = TimeSpan.FromSeconds(Math.Max(0, f.TimeRemaining));
                ImGui.TextColored(Theme.TextDim, $"Прогресс {f.Progress}% · осталось {(int)left.TotalMinutes}:{left.Seconds:00}");
                ImGui.SameLine();
                if (ImGui.SmallButton("Флаг") && map is not null)
                {
                    var p = MapUtil.WorldToMap(new Vector2(f.Position.X, f.Position.Z), map.Value);
                    Plugin.GameGui.OpenMapWithMapLink(new MapLinkPayload(Plugin.ClientState.TerritoryType, mapId, p.X, p.Y, 0.05f));
                }
                ImGui.EndGroup();
                ImGui.SetCursorScreenPos(new Vector2(pos.X, Math.Max(ImGui.GetCursorScreenPos().Y, pos.Y + h) + 4 * Theme.S));
                ImGui.Dummy(Vector2.Zero);
                ImGui.PopID();
            }

            ImGui.TextColored(Theme.Gray, "Если уровень выше FATE — используй Level Sync, иначе опыта почти не будет.");
        });
    }

    private void DrawCurrents()
    {
        var unlocks = Plugin.Unlocks;
        var currentTerr = Plugin.ClientState.TerritoryType;
        ImGui.Checkbox("Только зоны, где собрано не всё", ref onlyMissing);

        foreach (var set in Plugin.Data.GetExcelSheet<AetherCurrentCompFlgSet>())
        {
            var terr = set.Territory.ValueNullable;
            if (terr is null || set.Territory.RowId == 0) continue;
            var zone = terr.Value.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
            if (zone.Length == 0) continue;

            var currents = set.AetherCurrents.Where(c => c.RowId != 0 && c.ValueNullable is not null).Select(c => c.Value).ToList();
            if (currents.Count == 0) continue;
            var have = currents.Count(c => unlocks.IsAetherCurrentUnlocked(c));
            if (onlyMissing && have == currents.Count) continue;

            var here = set.Territory.RowId == currentTerr;
            Theme.Panel($"ac{set.RowId}", () =>
            {
                Theme.Progress(here ? $"{zone} (ты здесь)" : zone, have, currents.Count, have == currents.Count ? Theme.Green : here ? Theme.Gold : Theme.Blue);
                var missingQuests = currents.Where(c => !unlocks.IsAetherCurrentUnlocked(c) && c.Quest.RowId != 0)
                    .Select(c => c.Quest.ValueNullable?.Name.ExtractText() ?? "").Where(n => n.Length > 0).ToList();
                var missingField = currents.Count(c => !unlocks.IsAetherCurrentUnlocked(c) && c.Quest.RowId == 0);
                if (missingField > 0)
                    ImGui.TextColored(Theme.TextDim, $"В поле осталось найти: {missingField} (синие точки, видны рядом)");
                foreach (var q in missingQuests)
                {
                    Theme.FaIcon(FontAwesomeIcon.Scroll, Theme.GoldDim);
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.Text, $"Квест: {q}");
                }
            });
        }
    }
}
