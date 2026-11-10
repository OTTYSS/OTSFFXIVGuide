using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Гильдейские задания (левы): у кого брать, что сдать или сделать, награда.</summary>
internal sealed class LevesTab
{
    private sealed record LeveRow(uint Id, string Name, string Desc, int Level, string Kind, string Cats, string Client, string Place,
        uint IssuerLevel, uint Gil, uint Exp, int Allowances, List<(uint Item, int Count)> Turnin, int Repeats, List<uint> Rewards, uint Icon);

    private static readonly (string Name, string[] Abbrs)[] Groups =
    {
        ("Крафт", new[] { "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL" }),
        ("Сбор", new[] { "MIN", "BTN", "FSH" }),
        ("Бой", new[] { "GLA" }),
    };

    private readonly GuideData data;
    private List<LeveRow>? leves;
    private int group;
    private string abbr = "CRP";
    private int minLevel = 1, maxLevel = 100;
    private string search = "";
    private bool myLevel = true;

    public LevesTab(GuideData data) => this.data = data;

    private void Build()
    {
        var craft = Plugin.Data.GetExcelSheet<CraftLeve>();
        var groups = Plugin.Data.GetExcelSheet<LeveRewardItemGroup>();
        var residents = Plugin.Data.GetExcelSheet<ENpcResident>();
        leves = new List<LeveRow>();
        foreach (var l in Plugin.Data.GetExcelSheet<Leve>())
        {
            var name = l.Name.ExtractText();
            if (name.Length == 0 || l.ClassJobLevel == 0) continue;
            var cat = l.ClassJobCategory.RowId;
            var cats = string.Join(",", Groups.SelectMany(g => g.Abbrs).Concat(new[] { "PLD", "WHM" }).Where(a => data.JobInCategory(cat, a)));
            var isCraft = Groups[0].Abbrs.Any(a => cats.Contains(a));
            var isGather = Groups[1].Abbrs.Any(a => cats.Contains(a));
            var kind = isCraft ? "Крафт" : isGather ? "Сбор" : "Бой";

            var turnin = new List<(uint, int)>();
            var repeats = 0;
            if (isCraft && craft.GetRowOrDefault(l.DataId.RowId) is { } c)
            {
                for (var i = 0; i < c.Item.Count; i++)
                    if (c.Item[i].RowId != 0) turnin.Add((c.Item[i].RowId, i < c.ItemCount.Count ? c.ItemCount[i] : 1));
                repeats = c.Repeats;
            }

            var rewards = new List<uint>();
            if (l.LeveRewardItem.ValueNullable is { } ri)
                foreach (var g in ri.LeveRewardItemGroup)
                    if (g.RowId != 0 && groups.GetRowOrDefault(g.RowId) is { } gr)
                        foreach (var it in gr.Item)
                            if (it.RowId != 0 && !rewards.Contains(it.RowId)) rewards.Add(it.RowId);

            var client = l.LeveClient.ValueNullable?.Name.ExtractText() ?? "";
            var place = l.PlaceNameIssued.ValueNullable?.Name.ExtractText() ?? l.PlaceNameStart.ValueNullable?.Name.ExtractText() ?? "";
            leves.Add(new LeveRow(l.RowId, name, l.Description.ExtractText(), l.ClassJobLevel, kind, "," + cats + ",", client, place,
                l.LevelLevemete.RowId, l.GilReward, l.ExpReward, l.AllowanceCost, turnin, repeats, rewards, (uint)Math.Max(0, l.IconIssuer)));
        }
    }

    public void Draw()
    {
        Theme.Header("Левы", "Гильдейские задания: у кого брать, что сдать, сколько дают; удобно для прокачки крафта и сбора");
        if (leves is null)
        {
            try { Build(); }
            catch (Exception e) { Plugin.Log.Warning(e, "Левы: не удалось разобрать таблицы"); leves = new(); }
        }

        var groupNames = Groups.Select(g => g.Name).ToArray();
        var prev = group;
        Theme.Chips("lvgrp", groupNames, ref group);
        if (prev != group || !Groups[group].Abbrs.Contains(abbr)) abbr = Groups[group].Abbrs[0];
        if (Groups[group].Abbrs.Length > 1)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(110 * Theme.S);
            if (ImGui.BeginCombo("##lvjob", abbr))
            {
                foreach (var a in Groups[group].Abbrs)
                    if (ImGui.Selectable(a, a == abbr)) abbr = a;
                ImGui.EndCombo();
            }
        }
        ImGui.SameLine();
        ImGui.Checkbox("Под мой уровень", ref myLevel);
        if (myLevel)
        {
            var lvl = PlayerLevel(abbr);
            minLevel = Math.Max(1, lvl - 9);
            maxLevel = Math.Max(1, lvl);
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextDim, lvl > 0 ? $"(ур. {lvl}: левы {minLevel}–{maxLevel})" : "(уровень неизвестен)");
        }
        else
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(160 * Theme.S);
            ImGui.DragIntRange2("Уровни##lvr", ref minLevel, ref maxLevel, 0.3f, 1, 100);
        }
        ImGui.SetNextItemWidth(Math.Min(420 * Theme.S, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##lvsearch", "Поиск: название, предмет, место…", ref search, 64);

        var key = "," + (group == 2 ? "PLD" : abbr) + ",";
        var list = leves!.Where(l => l.Kind == Groups[group].Name && l.Cats.Contains(key) && l.Level >= minLevel && l.Level <= maxLevel)
            .Where(l => search.Length == 0 || l.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || l.Place.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || l.Turnin.Any(t => WikiView.ItemInfo(t.Item).Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                                             || NameTranslations.Show(WikiView.ItemInfo(t.Item).Name).Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(l => l.Level).ThenByDescending(l => l.Exp).ToList();
        ImGui.TextColored(Theme.TextDim, $"Лев: {list.Count}. На одну леву уходит Allowance (их 100 максимум, +3 каждые 12 часов).");

        if (!ImGui.BeginChild("##leves", Vector2.Zero)) { ImGui.EndChild(); return; }
        foreach (var l in list.Take(150))
            Theme.Panel($"lv{l.Id}", () =>
            {
                ImGui.TextColored(Theme.Gold, $"Ур. {l.Level} · {NameTranslations.Show(l.Name)}");
                ImGui.SameLine();
                ImGui.TextColored(Theme.TextDim, $"{l.Exp:N0} опыта · {l.Gil:N0} гил" + (l.Allowances > 1 ? $" · {l.Allowances} Allowance" : ""));
                ImGui.TextColored(Theme.Text, $"{(l.Client.Length > 0 ? NameTranslations.Show(l.Client) + " · " : "")}{l.Place}");
                if (l.IssuerLevel != 0)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Флаг на левемейта##lvf{l.Id}")) ItemSources.FlagLevel(l.IssuerLevel);
                }
                if (l.Turnin.Count > 0)
                    foreach (var (item, n) in l.Turnin)
                        WikiView.ItemLine(item, n, (l.Repeats > 0 ? $"сдать, повторов до {l.Repeats + 1} · " : "сдать · ") + $"есть {PlayerInfo.ItemCount(item)}", $"lvt{l.Id}");
                else if (l.Desc.Length > 0)
                    ImGui.TextColored(Theme.Gray, l.Desc.Replace("\n", " "));
                if (l.Rewards.Count > 0)
                {
                    ImGui.TextColored(Theme.TextDim, "Возможные награды:");
                    WikiView.ItemGrid(l.Rewards, $"lvr{l.Id}");
                }
            });
        if (list.Count > 150) ImGui.TextColored(Theme.Gray, $"Показаны первые 150 из {list.Count} — уточни поиск или уровни.");
        ImGui.EndChild();
    }

    private static int PlayerLevel(string jobAbbr)
    {
        if (!PlayerInfo.Ready) return 0;
        var cj = Plugin.Data.GetExcelSheet<ClassJob>().FirstOrDefault(c => GuideData.JobAbbrEnglish(c.RowId) == jobAbbr);
        if (cj.RowId == 0 || cj.ExpArrayIndex < 0) return 0;
        return Plugin.PlayerState.GetClassJobLevel(cj);
    }
}
