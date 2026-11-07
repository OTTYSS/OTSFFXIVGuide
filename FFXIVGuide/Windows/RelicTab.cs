using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Реликвийное оружие всех дополнений по шагам + справка по Eureka и Bozja.</summary>
internal sealed class RelicTab
{
    private sealed class RelicItem { public string Name { get; set; } = ""; public int Qty { get; set; } }
    private sealed class Stage { public string stage { get; set; } = ""; public string quest { get; set; } = ""; public string npc { get; set; } = ""; public string zone { get; set; } = ""; public List<RelicItem> items { get; set; } = new(); public string how { get; set; } = ""; }
    private sealed class Line { public string line { get; set; } = ""; public string expansion { get; set; } = ""; public int minLevel { get; set; } public List<Stage> stages { get; set; } = new(); }

    private List<Line>? lines;
    private int selected;
    private readonly GuideData data;
    private Dictionary<string, uint>? itemByName;

    public RelicTab(GuideData data) => this.data = data;

    private static List<Line> Load()
    {
        using var s = typeof(RelicTab).Assembly.GetManifestResourceStream("FFXIVGuide.Data.Relics.json");
        if (s is null) return new List<Line>();
        var list = JsonSerializer.Deserialize<List<Line>>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<Line>();
        return list;
    }

    private uint ItemId(string name)
    {
        itemByName ??= Plugin.Data.GetExcelSheet<Item>(Dalamud.Game.ClientLanguage.English)
            .Where(i => i.Name.ExtractText().Length > 0)
            .GroupBy(i => i.Name.ExtractText(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().RowId, StringComparer.OrdinalIgnoreCase);
        return itemByName.GetValueOrDefault(name);
    }

    public void Draw()
    {
        Theme.Header("Реликвии, Eureka и Bozja", "Реликвийное оружие по шагам: что нужно на каждом этапе и сколько у тебя уже есть");
        lines ??= Load();
        var names = lines.Select(l => $"{l.line} ({l.expansion})").Append("Eureka и Bozja").ToArray();
        Theme.Chips("relline", names, ref selected);
        if (!ImGui.BeginChild("##relic", Vector2.Zero)) { ImGui.EndChild(); return; }
        if (selected >= lines.Count) DrawFields();
        else DrawLine(lines[selected]);
        ImGui.EndChild();
    }

    private void DrawLine(Line l)
    {
        ImGui.TextColored(Theme.TextDim, $"С {l.minLevel} уровня. Наведи на предмет — подсветится в сумке; клик — где взять.");
        ImGui.TextColored(Theme.Gray, "Данные с ffxiv.consolegameswiki.com; часть шагов сокращена — подробности смотри в квестах.");
        for (var i = 0; i < l.stages.Count; i++)
        {
            var s = l.stages[i];
            Theme.Panel($"rs{l.line}{i}", () =>
            {
                Theme.InlineBadge($"{i + 1}", Theme.Gold);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Gold, s.stage);
                if (s.quest.Length > 0)
                {
                    Theme.FaIcon(FontAwesomeIcon.Scroll, Theme.GoldDim);
                    ImGui.SameLine();
                    var q = data.Quests.Values.FirstOrDefault(x => s.quest.Split(" / ").Any(n => QuestMatch(x.Name, n.Trim())));
                    if (q is not null)
                    {
                        var done = PlayerInfo.IsQuestDone(q.Id);
                        if (ImGui.Selectable($"{NameTranslations.Show(q.Name)}{(done ? " ✓" : "")}##rq{l.line}{i}", false, ImGuiSelectableFlags.None, new Vector2(ImGui.CalcTextSize(NameTranslations.Show(q.Name) + " ✓").X, 0)))
                            WikiNav.Open(WikiKind.Quest, q.Id, q.Name);
                    }
                    else ImGui.TextColored(Theme.Text, s.quest);
                }
                if (s.npc.Length > 0 || s.zone.Length > 0)
                    ImGui.TextColored(Theme.TextDim, string.Join(" · ", new[] { s.npc, s.zone }.Where(x => x.Length > 0)));
                ImGui.TextColored(Theme.Text, s.how);
                foreach (var it in s.items)
                {
                    var id = ItemId(it.Name);
                    if (id == 0) { ImGui.TextColored(Theme.TextDim, $"• {it.Qty}× {it.Name}"); continue; }
                    var have = PlayerInfo.ItemCount(id) + RetainerTracker.CountOf(id).Total;
                    WikiView.ItemLine(id, it.Qty, have >= it.Qty ? $"есть {have} ✓" : $"есть {have} из {it.Qty}", $"rel{i}");
                }
            });
        }
    }

    /// <summary>Повторяемые квесты в игре начинаются со значка, а у реликвий ARR в скобках имя оружия.</summary>
    private static bool QuestMatch(string game, string wanted)
    {
        var clean = new string(game.Where(c => c < '' || c > '').ToArray()).Trim();
        return clean.Equals(wanted, StringComparison.OrdinalIgnoreCase) || clean.StartsWith(wanted + " (", StringComparison.OrdinalIgnoreCase);
    }

    private List<(string Cat, string Name, uint Icon, string Desc)>? lost;
    private List<(string Name, uint Icon, string Desc, int Uses)>? logos;

    private void DrawFields()
    {
        lost ??= Plugin.Data.GetExcelSheet<MYCTemporaryItem>()
            .Where(m => m.Action.RowId != 0 && m.Action.ValueNullable is { } a && a.Name.ExtractText().Length > 0)
            .Select(m => (m.Category.ValueNullable?.Name.ExtractText() ?? "", m.Action.Value.Name.ExtractText(), (uint)m.Action.Value.Icon,
                Plugin.Data.GetExcelSheet<ActionTransient>().GetRowOrDefault(m.Action.RowId)?.Description.ExtractText() ?? ""))
            .ToList();
        logos ??= Plugin.Data.GetExcelSheet<EurekaMagiaAction>()
            .Where(m => m.Action.RowId != 0 && m.Action.ValueNullable is { } a && a.Name.ExtractText().Length > 0)
            .Select(m => (m.Action.Value.Name.ExtractText(), (uint)m.Action.Value.Icon,
                Plugin.Data.GetExcelSheet<ActionTransient>().GetRowOrDefault(m.Action.RowId)?.Description.ExtractText() ?? "", (int)m.MaxUses))
            .ToList();

        Theme.Grid("fields", 2, 420f, i =>
        {
            if (i == 0)
                Theme.Panel("eureka", () =>
                {
                    Theme.LargeText(Theme.Gold, "Eureka");
                    ImGui.TextColored(Theme.Text, "Уровень стихий растёт за мобов и Notorious Monsters (NM). Цепочки мобов — больше опыта.");
                    ImGui.TextColored(Theme.TextDim, "Anemos → Pagos → Pyros → Hydatos → Baldesion Arsenal. Вход — квест «And We Shall Call It Eureka» (Kugane, Rabbit Hole).");
                    ImGui.TextColored(Theme.TextDim, "Кристаллы для реликвии падают с NM; фарм — в группах через трекеры NM (ffxiv-eureka.com).");
                    Theme.SubHeader($"Logos Actions (Pyros/Hydatos) · {logos.Count}");
                    foreach (var (name, icon, desc, uses) in logos)
                    {
                        var p = ImGui.GetCursorScreenPos();
                        Theme.GameIcon(icon, p, 22 * Theme.S, 4f);
                        ImGui.Dummy(new Vector2(22 * Theme.S));
                        ImGui.SameLine();
                        ImGui.TextColored(Theme.Text, name);
                        if (uses > 0) { ImGui.SameLine(); ImGui.TextColored(Theme.TextDim, $"зарядов {uses}"); }
                        if (ImGui.IsItemHovered() && desc.Length > 0) ImGui.SetTooltip(desc);
                    }
                });
            else
                Theme.Panel("bozja", () =>
                {
                    Theme.LargeText(Theme.Gold, "Bozja и Zadnor");
                    ImGui.TextColored(Theme.Text, "Ранг сопротивления растёт за стычки (FATE) и Critical Engagements (CE). Lost Actions — с NM и стычек, хранятся в Holster.");
                    ImGui.TextColored(Theme.TextDim, "Вход — квест «Hail to the Queen» (Gangos). Castrum Lacus Litore — ранг 10, Delubrum Reginae — ранг 11+, Zadnor и The Dalriada — после квестов 5.55.");
                    foreach (var g in lost.GroupBy(x => x.Cat))
                    {
                        Theme.SubHeader($"{(g.Key.Length > 0 ? g.Key : "Lost Actions")} · {g.Count()}");
                        foreach (var (_, name, icon, desc) in g)
                        {
                            var p = ImGui.GetCursorScreenPos();
                            Theme.GameIcon(icon, p, 22 * Theme.S, 4f);
                            ImGui.Dummy(new Vector2(22 * Theme.S));
                            ImGui.SameLine();
                            ImGui.TextColored(Theme.Text, name);
                            if (ImGui.IsItemHovered() && desc.Length > 0) ImGui.SetTooltip(desc);
                        }
                    }
                });
        });
    }
}
