using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Справочник баффов и дебаффов с поиском, плюс статусы текущей цели вживую.</summary>
internal sealed class StatusesTab
{
    private sealed record Row(uint Id, string Name, string Desc, uint Icon, byte Category, int MaxStacks, bool Dispel, bool Permanent);

    private static readonly string[] Filters = { "Все", "Баффы", "Дебаффы", "Снимаются Esuna" };
    private List<Row>? rows;
    private List<Row> filtered = new();
    private string search = "";
    private int filter;
    private string lastKey = "\0";
    private uint selected;

    private void Build()
    {
        rows = Plugin.Data.GetExcelSheet<Status>()
            .Where(s => s.Name.ExtractText().Length > 0 && s.Icon != 0)
            .Select(s => new Row(s.RowId, s.Name.ExtractText(), s.Description.ExtractText(), s.Icon, s.StatusCategory, s.MaxStacks, s.CanDispel, s.IsPermanent))
            .GroupBy(r => (r.Name, r.Desc)).Select(g => g.First()) // одинаковые копии под разные бои
            .OrderBy(r => r.Name)
            .ToList();
    }

    public void Focus(string text) { search = text; filter = 0; }

    public IEnumerable<SearchHit> Search(string term, Action<MainTab> go)
    {
        if (rows is null) Build();
        return rows!.Where(r => r.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Select(r => new SearchHit("Статусы", r.Name, (r.Category == 1 ? "бафф" : r.Category == 2 ? "дебафф" : "статус") + " — " + r.Desc.Replace("\n", " "), r.Icon,
                () => { Focus(r.Name); go(MainTab.Statuses); }));
    }

    public void Draw()
    {
        Theme.Header("Статусы", "Все баффы и дебаффы игры: что делают; статусы твоей цели — прямо сейчас");
        if (rows is null)
        {
            try { Build(); }
            catch (Exception e) { Plugin.Log.Warning(e, "Статусы: не удалось разобрать таблицу"); rows = new(); }
        }

        DrawTarget();

        ImGui.SetNextItemWidth(Math.Max(200 * Theme.S, ImGui.GetContentRegionAvail().X - 420 * Theme.S));
        ImGui.InputTextWithHint("##stsearch", "Название или текст описания…", ref search, 64);
        ImGui.SameLine();
        Theme.Chips("stf", Filters, ref filter);

        var key = search + "|" + filter;
        if (key != lastKey)
        {
            lastKey = key;
            filtered = rows!.Where(r => filter switch
                {
                    1 => r.Category == 1,
                    2 => r.Category == 2,
                    3 => r.Dispel,
                    _ => true,
                })
                .Where(r => search.Length == 0 || r.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                            || r.Desc.Contains(search, StringComparison.OrdinalIgnoreCase)
                            || NameTranslations.Show(r.Name).Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        ImGui.TextColored(Theme.TextDim, $"Найдено: {filtered.Count}. Наведи на строку — полное описание.");

        var rowH = 40 * Theme.S;
        if (!ImGui.BeginChild("##statuses", Vector2.Zero)) { ImGui.EndChild(); return; }
        Theme.VirtualList(filtered.Count, rowH, i => DrawRow(filtered[i], rowH, null));
        ImGui.EndChild();
    }

    private void DrawRow(Row r, float rowH, string? extra)
    {
        var kind = r.Category == 1 ? "бафф" : r.Category == 2 ? "дебафф" : "статус";
        var tags = new List<string> { kind };
        if (r.Dispel) tags.Add("снимается Esuna");
        if (r.MaxStacks > 1) tags.Add($"до {r.MaxStacks} стаков");
        if (extra is not null) tags.Insert(0, extra);
        var badge = r.Dispel ? "Esuna" : null;
        if (Theme.Card($"##st{r.Id}{extra}", r.Icon, NameTranslations.Show(r.Name), string.Join(" · ", tags) + " — " + r.Desc.Replace("\n", " "),
                badge, Theme.Green, selected == r.Id, rowH))
            selected = r.Id;
        if (ImGui.IsItemHovered() && r.Desc.Length > 0)
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(440 * Theme.S);
            ImGui.TextColored(Theme.Gold, r.Name);
            ImGui.TextUnformatted(r.Desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    /// <summary>Статусы цели (или себя, если цели нет) с описаниями.</summary>
    private void DrawTarget()
    {
        if (!PlayerInfo.Ready) return;
        var target = Plugin.Targets.Target as IBattleChara ?? Plugin.Objects.LocalPlayer as IBattleChara;
        if (target is null) return;
        var sheet = Plugin.Data.GetExcelSheet<Status>();
        var live = new List<(Row Row, string Extra)>();
        foreach (var s in target.StatusList)
        {
            if (s.StatusId == 0 || sheet.GetRowOrDefault(s.StatusId) is not { } st || st.Name.ExtractText().Length == 0) continue;
            var row = new Row(st.RowId, st.Name.ExtractText(), st.Description.ExtractText(), st.Icon, st.StatusCategory, st.MaxStacks, st.CanDispel, st.IsPermanent);
            var left = s.RemainingTime > 0 ? $"{s.RemainingTime:0} с" : "";
            var stacks = st.MaxStacks > 1 && s.Param > 0 ? $"×{s.Param}" : "";
            live.Add((row, string.Join(" ", new[] { left, stacks }.Where(x => x.Length > 0))));
        }
        Theme.Panel("sttarget", () =>
        {
            var who = Plugin.Targets.Target is null ? "на тебе" : $"на цели «{target.Name.TextValue}»";
            Theme.SubHeader($"Статусы {who}: {live.Count}");
            if (live.Count == 0) { ImGui.TextColored(Theme.TextDim, "Статусов нет."); return; }
            var rowH = 40 * Theme.S;
            foreach (var (row, extra) in live.Take(12)) DrawRow(row, rowH, extra.Length > 0 ? extra : "—");
        });
    }
}
