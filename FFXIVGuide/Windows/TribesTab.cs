using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Племена (allied societies): ранги, репутация, разрешения на квесты.</summary>
internal sealed unsafe class TribesTab
{
    private bool hideMax;

    public void Draw()
    {
        Theme.Header("Племена", "Ранг и репутация у каждого племени, сколько квестов можно взять сегодня");
        if (!PlayerInfo.Ready) { Theme.Panel("tbnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем.")); return; }

        var ps = PlayerState.Instance();
        var qm = QuestManager.Instance();
        if (ps == null || qm == null) return;

        Theme.Panel("tbtop", () =>
        {
            var allowances = qm->GetBeastTribeAllowance();
            Theme.FaIcon(FontAwesomeIcon.Ticket, Theme.Gold);
            ImGui.SameLine();
            ImGui.TextColored(Theme.Text, $"Разрешений на квесты племён сегодня: {allowances}");
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextDim, "(общие на все племена, сброс ежедневно)");
            ImGui.Checkbox("Скрыть племена с максимальным рангом", ref hideMax);
        });

        var ranks = Plugin.Data.GetExcelSheet<BeastReputationRank>();
        var tribes = Plugin.Data.GetExcelSheet<BeastTribe>()
            .Where(t => t.RowId > 0 && t.Name.ExtractText().Length > 0)
            .OrderBy(t => t.Expansion.RowId).ThenBy(t => t.DisplayOrder)
            .ToList();

        if (!ImGui.BeginChild("##tribes", Vector2.Zero)) { ImGui.EndChild(); return; }
        var rows = tribes.Select(t =>
        {
            var id = (byte)t.RowId;
            var rank = ps->GetBeastTribeRank(id);
            return (t, rank, cur: ps->GetBeastTribeCurrentReputation(id), need: ps->GetBeastTribeNeededReputation(id));
        }).Where(x => !hideMax || x.rank < x.t.MaxRank).ToList();

        Theme.Grid("tbgrid", rows.Count, 360f, i =>
        {
            var (t, rank, cur, need) = rows[i];
            Theme.Panel($"tb{t.RowId}", () =>
            {
                var pos = ImGui.GetCursorScreenPos();
                Theme.GameIcon(t.Icon, pos, 36 * Theme.S, 6f);
                ImGui.SetCursorScreenPos(pos + new Vector2(44 * Theme.S, 0));
                ImGui.BeginGroup();
                ImGui.TextColored(Theme.Gold, GuideData.Capitalize(t.Name.ExtractText()));
                ImGui.TextColored(Theme.TextDim, t.Expansion.ValueNullable?.Name.ExtractText() ?? "");
                ImGui.EndGroup();
                ImGui.SetCursorScreenPos(new Vector2(pos.X, Math.Max(ImGui.GetCursorScreenPos().Y, pos.Y + 40 * Theme.S)));

                if (rank == 0)
                {
                    ImGui.TextColored(Theme.Gray, "Не открыто — нужна цепочка квестов племени.");
                    return;
                }
                var rankName = ranks.GetRowOrDefault(rank)?.Name.ExtractText() ?? $"ранг {rank}";
                var max = rank >= t.MaxRank;
                if (max)
                    ImGui.TextColored(Theme.Green, $"{rankName} — максимальный ранг ✓");
                else
                    Theme.Progress($"{rankName} ({rank}/{t.MaxRank})", cur, Math.Max(cur, need), Theme.Blue);
                if (t.CurrencyItem.RowId != 0)
                {
                    var have = PlayerInfo.ItemCount(t.CurrencyItem.RowId);
                    WikiView.ItemLine(t.CurrencyItem.RowId, 0, $"у тебя {have}", $"tbc{t.RowId}");
                }
            });
        });
        ImGui.EndChild();
    }
}
