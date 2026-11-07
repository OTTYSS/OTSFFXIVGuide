using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Deep Dungeons: советы, помандеры, магические камни и демиклоны (тексты из игры).</summary>
internal sealed class DeepDungeonTab
{
    private sealed record Entry(string Name, string Text, uint Icon);

    private static readonly string[] Langs = { "English", "Русский" };
    private static readonly (string Name, string Levels)[] Dungeons =
    {
        ("The Palace of the Dead", "1–60, этажи 1–200"),
        ("Heaven-on-High", "61–70, этажи 1–100"),
        ("Eureka Orthos", "81–90, этажи 1–100"),
    };

    // Советы с ffxiv.consolegameswiki.com (Heaven-on-High / Eureka Orthos), общие для всех Deep Dungeons
    private static readonly string[] Tips =
    {
        "Pomander of Sight показывает ловушки и карту этажа; Pomander of Safety убирает все ловушки этажа.",
        "Ловушки обычно у стен. Luring Trap призывает врагов, Landmine бьёт ~80% текущего HP, Otter Trap блокирует действия.",
        "Pomander of Steel: −40% получаемого урона на 8 минут — спасает против сильных врагов и при ошибках.",
        "Auto-heal Penalty отключает регенерацию HP; Pomander of Serenity снимает все наложенные эффекты этажа.",
        "Beacon of Return поднимает упавших союзников, когда активен — дождитесь его, прежде чем идти дальше.",
        "Чем глубже, тем смертельнее AoE: уклоняйтесь от телеграфов, опасных патрулей и Dread Beast лучше обходить.",
        "Item Penalty запрещает помандеры (кроме Serenity), Demiclone Penalty — демиклонов.",
    };

    private int lang = Plugin.Config.QuestTextRussian ? 1 : 0;
    private List<Entry>? pomanders, stones, clones;

    public void Draw()
    {
        Theme.Header("Deep Dungeons", "Palace of the Dead, Heaven-on-High, Eureka Orthos и другие: что делает каждый предмет и как выжить");
        Theme.Chips("ddlang", Langs, ref lang);

        pomanders ??= Plugin.Data.GetExcelSheet<DeepDungeonItem>().Where(r => !r.Name.IsEmpty)
            .Select(r => new Entry(GuideData.Capitalize(r.Name.ExtractText()), Clean(r.Tooltip.ExtractText()), r.Icon)).DistinctBy(e => e.Name).ToList();
        stones ??= Plugin.Data.GetExcelSheet<DeepDungeonMagicStone>().Where(r => !r.Name.IsEmpty)
            .Select(r => new Entry(GuideData.Capitalize(r.Name.ExtractText()), Clean(r.Tooltip.ExtractText()), r.Icon)).DistinctBy(e => e.Name).ToList();
        clones ??= Plugin.Data.GetExcelSheet<DeepDungeonDemiclone>().Where(r => !r.TitleCase.IsEmpty)
            .Select(r => new Entry(r.TitleCase.ExtractText(), Clean(r.Description.ExtractText()), r.Icon)).DistinctBy(e => e.Name).ToList();

        if (!ImGui.BeginChild("##dd", Vector2.Zero))
        {
            ImGui.EndChild();
            return;
        }

        Theme.Panel("ddlist", () =>
        {
            Theme.SubHeader("Подземелья");
            foreach (var (name, levels) in Dungeons)
            {
                Theme.FaIcon(FontAwesomeIcon.Dungeon, Theme.GoldDim);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Text, name);
                ImGui.SameLine();
                ImGui.TextColored(Theme.TextDim, $"ур. {levels}");
            }
            ImGui.TextColored(Theme.Gray, "Уровень и экипировка внутри свои (Aetherpool) — растут по мере прохождения и сохраняются между заходами.");
        });

        Theme.Panel("ddtips", () =>
        {
            Theme.SubHeader("Как выжить");
            foreach (var t in Tips)
            {
                Theme.FaIcon(FontAwesomeIcon.Star, Theme.Gold);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Text, t);
            }
        });

        Section("Помандеры (Pomanders)", pomanders);
        Section("Магические камни (Magicite и другие)", stones);
        Section("Демиклоны", clones);

        ImGui.EndChild();
    }

    private void Section(string title, List<Entry> list)
    {
        if (list.Count == 0) return;
        Theme.Panel(title, () =>
        {
            Theme.SubHeader($"{title} · {list.Count}");
            foreach (var e in list)
            {
                var h = ImGui.GetTextLineHeight() * 2.2f;
                var pos = ImGui.GetCursorScreenPos();
                Theme.GameIcon(e.Icon, pos, h, 4f);
                ImGui.SetCursorScreenPos(pos + new Vector2(h + 8 * Theme.S, 0));
                ImGui.BeginGroup();
                ImGui.TextColored(Theme.Gold, e.Name);
                var text = e.Text;
                if (lang == 1 && text.Length > 0)
                {
                    Translator.Instance.Request([text]);
                    text = Translator.Instance.Get(text) ?? text;
                }
                ImGui.TextColored(Theme.TextDim, text);
                ImGui.EndGroup();
                ImGui.SetCursorScreenPos(new Vector2(pos.X, System.Math.Max(ImGui.GetCursorScreenPos().Y, pos.Y + h) + 4 * Theme.S));
                ImGui.Dummy(Vector2.Zero);
            }
        });
    }

    private static string Clean(string s) => s.Replace("\n", " ").Trim();
}
