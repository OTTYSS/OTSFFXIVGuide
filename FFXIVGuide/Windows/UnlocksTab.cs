using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>
/// Разблокировки: эмоции, причёски, барды, аксессуары, очки, рамки портретов, книги ремесла и фольклора.
/// Что собрано, чего нет, где взять (клик — карточка предмета с источниками).
/// </summary>
internal sealed unsafe class UnlocksTab
{
    private static readonly (string Name, Func<Item, bool> Match)[] Groups =
    {
        ("Эмоции", i => i.ItemAction.ValueNullable?.Action.RowId == 2633 && i.Name.ExtractText().StartsWith("Ballroom Etiquette")),
        ("Причёски", i => i.ItemAction.ValueNullable?.Action.RowId == 2633 && i.Name.ExtractText().StartsWith("Modern Aesthetics")),
        ("Барды чокобо", i => i.ItemAction.ValueNullable?.Action.RowId == 1013),
        ("Аксессуары", i => i.ItemAction.ValueNullable?.Action.RowId == 20086),
        ("Очки", i => i.ItemAction.ValueNullable?.Action.RowId == 37312),
        ("Рамки портретов", i => i.ItemAction.ValueNullable?.Action.RowId == 29459),
        ("Master Recipes", i => i.ItemAction.ValueNullable?.Action.RowId == 2136),
        ("Фольклор (сбор)", i => i.ItemAction.ValueNullable?.Action.RowId == 4107),
        ("Прочее", i => i.ItemAction.ValueNullable?.Action.RowId == 2633 && !i.Name.ExtractText().StartsWith("Ballroom Etiquette") && !i.Name.ExtractText().StartsWith("Modern Aesthetics")),
    };

    private int group;
    private int filter = 1;
    private static readonly string[] Filters = { "Все", "Нет у меня", "Есть" };
    private string search = "";
    private List<(uint Id, string Name, uint Icon)>[]? lists;
    private List<(Emote Row, string Source)>? emotes;

    private void Build()
    {
        var all = Plugin.Data.GetExcelSheet<Item>().Where(i => i.ItemAction.RowId != 0 && i.Name.ExtractText().Length > 0).ToList();
        lists = Groups.Select(g => all.Where(g.Match).Select(i => (i.RowId, i.Name.ExtractText(), (uint)i.Icon)).OrderBy(x => x.Item2).ToList()).ToArray();

        // Эмоции из квестов (не только предметами)
        var questEmote = Plugin.Data.GetExcelSheet<Quest>().Where(q => q.EmoteReward.RowId != 0)
            .GroupBy(q => q.EmoteReward.RowId).ToDictionary(g => g.Key, g => "Квест: " + g.First().Name.ExtractText());
        emotes = Plugin.Data.GetExcelSheet<Emote>()
            .Where(e => e.UnlockLink != 0 && e.Name.ExtractText().Length > 0)
            .Select(e => (e, questEmote.GetValueOrDefault(e.RowId) ?? "предмет, событие или магазин"))
            .OrderBy(x => x.e.Name.ExtractText()).ToList();
    }

    private static bool Has(uint itemId)
    {
        var row = Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId);
        return row is { } r && PlayerInfo.Ready && Plugin.Unlocks.IsItemUnlocked(r);
    }

    public void Draw()
    {
        Theme.Header("Разблокировки", "Эмоции, причёски, барды, аксессуары, очки, рамки, книги ремесла и фольклора — что собрано и где взять остальное");
        if (lists is null) Build();
        var names = Groups.Select(g => g.Name).Prepend("Все эмоции").ToArray();
        Theme.Chips("ungrp", names, ref group);
        Theme.Chips("unflt", Filters, ref filter);
        Theme.Search("##unsearch", "Название…", ref search);
        var term = search.Trim();

        if (!ImGui.BeginChild("##unl", Vector2.Zero)) { ImGui.EndChild(); return; }
        if (group == 0)
        {
            var rows = emotes!.Where(x => (term.Length < 2 || NameTranslations.Matches(x.Row.Name.ExtractText(), term))
                                          && (filter == 0 || !PlayerInfo.Ready || Plugin.Unlocks.IsEmoteUnlocked(x.Row) == (filter == 2))).ToList();
            var have = PlayerInfo.Ready ? emotes!.Count(x => Plugin.Unlocks.IsEmoteUnlocked(x.Row)) : 0;
            Theme.Progress("Эмоций открыто", have, emotes!.Count, Theme.Gold);
            var rowH = 40 * Theme.S;
            Theme.VirtualList(rows.Count, rowH, i =>
            {
                var (e, src) = rows[i];
                var ok = PlayerInfo.Ready && Plugin.Unlocks.IsEmoteUnlocked(e);
                var cmd = e.TextCommand.ValueNullable?.Command.ExtractText() ?? "";
                Theme.Card($"##em{e.RowId}", e.Icon, e.Name.ExtractText(), (cmd.Length > 0 ? cmd + " · " : "") + src, ok ? "есть" : "нет", ok ? Theme.Green : Theme.Gray, false, rowH);
            });
        }
        else
        {
            var list = lists![group - 1];
            var have = list.Count(x => Has(x.Id));
            Theme.Progress($"{Groups[group - 1].Name}: собрано", have, list.Count, Theme.Gold);
            var rows = list.Where(x => (term.Length < 2 || NameTranslations.Matches(x.Name, term)) && (filter == 0 || Has(x.Id) == (filter == 2))).ToList();
            var rowH = 40 * Theme.S;
            Theme.VirtualList(rows.Count, rowH, i =>
            {
                var (id, name, icon) = rows[i];
                var ok = Has(id);
                var inBag = PlayerInfo.ItemCount(id);
                if (Theme.Card($"##un{id}", icon, name, inBag > 0 && !ok ? "лежит в сумке — используй, чтобы открыть" : "нажми — где взять",
                        ok ? "есть" : "нет", ok ? Theme.Green : inBag > 0 ? Theme.Yellow : Theme.Gray, false, rowH))
                    WikiNav.Open(WikiKind.Item, id, name);
            });
        }
        ImGui.EndChild();
    }
}
