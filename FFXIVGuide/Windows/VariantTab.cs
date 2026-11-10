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

/// <summary>Variant и Criterion данжи: записи журнала (концовки маршрутов) с отметкой пройденных, сами бои и награды.</summary>
internal sealed unsafe class VariantTab
{
    private sealed record Entry(uint Id, string Name, string Desc, uint Icon);
    private sealed record Series(uint Id, string Name, List<Entry> Entries);

    private readonly GuideData data;
    private List<Series>? series;
    private int pick;

    public VariantTab(GuideData data) => this.data = data;

    private void Build()
    {
        series = new List<Series>();
        foreach (var s in Plugin.Data.GetExcelSheet<VVDNotebookSeries>())
        {
            var name = s.Name.ExtractText();
            if (name.Length == 0) continue;
            var entries = s.Contents.Where(c => c.RowId != 0 && c.ValueNullable is not null)
                .Select(c => new Entry(c.RowId, c.Value.Name.ExtractText(), c.Value.Description.ExtractText(), (uint)Math.Max(0, c.Value.Icon)))
                .Where(e => e.Name.Length > 0).ToList();
            series.Add(new Series(s.RowId, name, entries));
        }
    }

    private static bool Done(PlayerState* ps, uint id)
    {
        var bytes = ps->CompletedVVDNotebookContents;
        var i = (int)id;
        return i >> 3 < bytes.Length && (bytes[i >> 3] & (1 << (i & 7))) != 0;
    }

    public IEnumerable<SearchHit> Search(string term, Action<MainTab> go)
    {
        if (series is null) Build();
        for (var i = 0; i < series!.Count; i++)
        {
            var idx = i;
            foreach (var e in series[i].Entries.Where(e => e.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
                yield return new SearchHit("Записи Variant", e.Name, series[i].Name, e.Icon, () => { pick = idx; go(MainTab.Variant); });
        }
    }

    public void Draw()
    {
        Theme.Header("Variant и Criterion", "Записи журнала Variant-данжей (каждая — отдельная концовка маршрута), бои и что они дают");
        if (series is null)
        {
            try { Build(); }
            catch (Exception e) { Plugin.Log.Warning(e, "Variant: ошибка разбора"); series = new(); }
        }
        var ps = PlayerState.Instance();
        if (!PlayerInfo.Ready || ps == null) { Theme.Panel("vvnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем.")); return; }
        if (series!.Count == 0) return;

        // Бои V&C
        var duties = data.Duties.Where(d => Plugin.Data.GetExcelSheet<ContentFinderCondition>().GetRowOrDefault(d.CfcId)?.ContentType.RowId == 30).ToList();
        Theme.Panel("vvduties", () =>
        {
            Theme.SubHeader("Бои Variant и Criterion");
            foreach (var d in duties)
            {
                var ok = PlayerInfo.IsDutyCompleted(d);
                var kind = d.Name.Contains("Savage", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("Advanced", StringComparison.OrdinalIgnoreCase) ? "Criterion Savage"
                    : d.Name.StartsWith("Another", StringComparison.OrdinalIgnoreCase) ? "Criterion" : "Variant";
                ImGui.TextColored(ok ? Theme.Green : Theme.Text, $"{(ok ? "✓ " : "")}{NameTranslations.Show(d.Name)}");
                ImGui.SameLine();
                ImGui.TextColored(Theme.TextDim, $"{kind} · ур. {d.Level}" + (PlayerInfo.IsDutyUnlocked(d) ? "" : " · не открыт"));
            }
            ImGui.PushTextWrapPos(0);
            ImGui.TextColored(Theme.Gray, "Variant — для 1–4 игроков, маршрут выбирается голосованием на развилках; за новые записи журнала дают валюту для обмена на маунта, гламур и мебель. Criterion — сложная версия на 4 игроков без возрождений.");
            ImGui.PopTextWrapPos();
        });

        var labels = series.Select(s =>
        {
            var done = s.Entries.Count(e => Done(ps, e.Id));
            return $"{NameTranslations.Show(s.Name)} — {done}/{s.Entries.Count}";
        }).ToArray();
        pick = Math.Clamp(pick, 0, labels.Length - 1);
        Theme.Chips("vvser", labels, ref pick);

        var cur = series[pick];
        var doneCount = cur.Entries.Count(e => Done(ps, e.Id));
        Theme.Panel("vvprog", () =>
        {
            Theme.Progress("Записей журнала", doneCount, cur.Entries.Count, doneCount == cur.Entries.Count ? Theme.Green : Theme.Gold);
            ImGui.TextColored(Theme.Gray, "Каждая запись открывается своим маршрутом. Описание — текст записи из игры: по нему понятно, к какой концовке он ведёт.");
        });
        if (!ImGui.BeginChild("##vvlist", Vector2.Zero)) { ImGui.EndChild(); return; }
        foreach (var e in cur.Entries)
            Theme.Panel($"vv{e.Id}", () =>
            {
                var ok = Done(ps, e.Id);
                Theme.FaIcon(ok ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.Circle, ok ? Theme.Green : Theme.Gray);
                ImGui.SameLine();
                ImGui.TextColored(ok ? Theme.Green : Theme.Gold, NameTranslations.Show(e.Name));
                if (e.Desc.Length > 0)
                {
                    ImGui.PushTextWrapPos(0);
                    ImGui.TextColored(ok ? Theme.TextDim : Theme.Text, e.Desc.Replace("\n", " "));
                    ImGui.PopTextWrapPos();
                }
            });
        ImGui.EndChild();
    }
}
