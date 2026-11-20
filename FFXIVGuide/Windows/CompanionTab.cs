using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Боевой чокобо-компаньон: ранг и опыт, очки навыков, три ветки и барды.</summary>
internal sealed class CompanionTab
{
    private static readonly string[] Trees = { "Защитник (Defender)", "Атакующий (Attacker)", "Лекарь (Healer)" };

    public unsafe void Draw()
    {
        Theme.Header("Чокобо-компаньон", "Ранг, опыт, очки навыков, ветки умений и барды твоего боевого чокобо");
        Theme.Panel("cbhow", () =>
            ImGui.TextColored(Theme.TextDim, "Открывается квестом «My Little Chocobo» в своей Grand Company. Призывается Gysahl Greens и получает опыт за убийства, пока сражается рядом. " +
                                             "За каждый ранг даётся очко навыка; веток три, в каждой 10 ступеней. Предел ранга поднимают Thavnairian Onion."));
        if (!PlayerInfo.Ready) { Theme.NeedLogin("Зайди в игру персонажем: данные чокобо берутся из игры."); return; }
        var ui = UIState.Instance();
        if (ui == null) return;
        var c = ui->Buddy.CompanionInfo;
        if (c.Rank == 0)
        {
            Theme.EmptyState(FontAwesomeIcon.Feather, "Чокобо ещё нет", "Возьми квест «My Little Chocobo» у своей Grand Company.");
            return;
        }
        var ranks = Plugin.Data.GetExcelSheet<BuddyRank>();
        var need = ranks.GetRowOrDefault(c.Rank)?.ExpRequired ?? 0;
        Theme.Panel("cbstate", () =>
        {
            var name = c.NameString;
            Theme.LargeText(Theme.Gold, $"{(string.IsNullOrEmpty(name) ? "Чокобо" : name)} · ранг {c.Rank}");
            if (need > 0) Theme.Progress($"Опыт: {c.CurrentXP:N0} / {need:N0}", (int)Math.Min(int.MaxValue, c.CurrentXP), (int)need, Theme.Gold);
            ImGui.TextColored(c.SkillPoints > 0 ? Theme.Yellow : Theme.TextDim, $"Свободных очков навыков: {c.SkillPoints}" + (c.SkillPoints > 0 ? " — распредели в меню компаньона" : ""));
            if (c.TimeLeft > 0) ImGui.TextColored(Theme.Green, $"Призван: ещё {Math.Ceiling(c.TimeLeft / 60)} мин");
        });

        var skills = Plugin.Data.GetExcelSheet<BuddySkill>().Where(s => s.RowId > 0 && s.BuddyLevel > 0).OrderBy(s => s.BuddyLevel).ToList();
        var levels = c.Levels.ToArray();
        var actions = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        var tr = Plugin.Data.GetExcelSheet<ActionTransient>();
        Theme.Grid("cbtrees", 3, 260f, t =>
            Theme.Panel($"cbt{t}", () =>
            {
                var lvl = t < levels.Length ? levels[t] : 0;
                Theme.SubHeader($"{Trees[t]} · {lvl}/10");
                foreach (var s in skills)
                {
                    var id = t switch { 0 => s.Defender.RowId, 1 => s.Attacker.RowId, _ => s.Healer.RowId };
                    var act = actions.GetRowOrDefault(id);
                    if (act is not { } a) continue;
                    var have = s.BuddyLevel <= lvl;
                    ImGui.TextColored(have ? Theme.Green : Theme.TextDim, $"{s.BuddyLevel}. {a.Name.ExtractText()}" + (have ? " ✓" : ""));
                    if (ImGui.IsItemHovered() && tr.GetRowOrDefault(id) is { } d) ImGui.SetTooltip(d.Description.ExtractText());
                }
            }));

        var equip = Plugin.Data.GetExcelSheet<BuddyEquip>().Where(e => e.RowId > 0 && !e.Name.IsEmpty).ToList();
        var owned = equip.Count(e => c.IsBuddyEquipUnlocked(e.RowId));
        Theme.Panel("cbeq", () =>
        {
            Theme.Progress("Барды (броня для чокобо)", owned, equip.Count, Theme.Blue);
            ImGui.TextColored(Theme.Gray, "Полный список и где взять — Коллекции → Коллекции → Барды.");
        });
    }
}
