using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>
/// Выгодные вылазки ретейнеров: прицельные вылазки (Hunting/Mining/Botany/Fishing Exploration)
/// для класса и уровня ретейнера, отсортированные по выручке на рынке твоего мира.
/// </summary>
internal sealed unsafe class VenturesTab
{
    private sealed record Task(uint Id, uint Item, int Level, int MinQty, int MaxQty, int ReqStat, bool Gatherer, string ClassCats);

    private readonly MarketService market;
    private List<Task>? tasks;
    private readonly Dictionary<uint, string> catAbbrs = new(); // ClassJobCategory -> список аббревиатур
    private int pick;               // выбранный ретейнер (или ручной режим)
    private int manualJob;          // индекс в ManualJobs
    private int manualLevel = 100;
    private bool onlyMarketable = true;

    private static readonly (string Abbr, string Name)[] ManualJobs =
    {
        ("MIN", "Шахтёр"), ("BTN", "Ботаник"), ("FSH", "Рыбак"), ("GLA", "Боевой класс"),
    };

    public VenturesTab(MarketService market) => this.market = market;

    private void Build()
    {
        tasks = new List<Task>();
        var normal = Plugin.Data.GetExcelSheet<RetainerTaskNormal>();
        foreach (var t in Plugin.Data.GetExcelSheet<RetainerTask>())
        {
            if (t.IsRandom || t.Task.RowId == 0) continue;
            var n = normal.GetRowOrDefault(t.Task.RowId);
            if (n is null || n.Value.Item.RowId == 0) continue;
            var q = n.Value.Quantity.Select(x => (int)x).Where(x => x > 0).ToList();
            if (q.Count == 0) continue;
            var gatherer = t.RequiredGathering > 0;
            tasks.Add(new Task(t.RowId, n.Value.Item.RowId, t.RetainerLevel, q.Min(), q.Max(),
                gatherer ? t.RequiredGathering : t.RequiredItemLevel, gatherer, CatAbbrs(t.ClassJobCategory.RowId)));
        }
    }

    /// <summary>Аббревиатуры классов, которые входят в категорию (через свойства-флаги листа ClassJobCategory).</summary>
    private string CatAbbrs(uint cat)
    {
        if (catAbbrs.TryGetValue(cat, out var s)) return s;
        var row = Plugin.Data.GetExcelSheet<ClassJobCategory>().GetRowOrDefault(cat);
        var list = new List<string>();
        if (row is not null)
            foreach (var p in typeof(ClassJobCategory).GetProperties())
                if (p.PropertyType == typeof(bool) && p.Name.Length == 3 && p.Name.All(char.IsUpper) && p.GetValue(row.Value) is true)
                    list.Add(p.Name);
        return catAbbrs[cat] = "," + string.Join(",", list) + ",";
    }

    private static List<(string Name, string Abbr, int Level)> Retainers()
    {
        var res = new List<(string, string, int)>();
        var rm = RetainerManager.Instance();
        if (rm == null) return res;
        for (var i = 0u; i < rm->GetRetainerCount(); i++)
        {
            var r = rm->GetRetainerBySortedIndex(i);
            if (r == null || r->RetainerId == 0 || r->ClassJob == 0) continue;
            res.Add((r->NameString, GuideData.JobAbbrEnglish(r->ClassJob), r->Level));
        }
        return res;
    }

    public void Draw()
    {
        Theme.Header("Вылазки ретейнеров", "Какие прицельные вылазки приносят больше всего гил по ценам рынка твоего мира");
        if (tasks is null)
        {
            try { Build(); }
            catch (Exception e) { Plugin.Log.Warning(e, "Вылазки: не удалось разобрать таблицы"); tasks = new(); }
        }

        var rets = Retainers();
        string abbr;
        int level;
        var labels = rets.Select(r => $"{r.Name} — {r.Abbr} {r.Level}").Append("Выбрать класс и уровень вручную").ToArray();
        pick = Math.Clamp(pick, 0, labels.Length - 1);
        ImGui.SetNextItemWidth(320 * Theme.S);
        if (ImGui.BeginCombo("##vret", labels[pick]))
        {
            for (var i = 0; i < labels.Length; i++)
                if (ImGui.Selectable($"{labels[i]}##vr{i}", i == pick)) pick = i;
            ImGui.EndCombo();
        }
        if (pick < rets.Count)
        {
            abbr = rets[pick].Abbr;
            level = rets[pick].Level;
        }
        else
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(160 * Theme.S);
            if (ImGui.BeginCombo("##vjob", ManualJobs[manualJob].Name))
            {
                for (var i = 0; i < ManualJobs.Length; i++)
                    if (ImGui.Selectable(ManualJobs[i].Name, i == manualJob)) manualJob = i;
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(110 * Theme.S);
            ImGui.InputInt("Уровень##vlvl", ref manualLevel);
            manualLevel = Math.Clamp(manualLevel, 1, 100);
            abbr = ManualJobs[manualJob].Abbr;
            level = manualLevel;
        }
        ImGui.SameLine();
        ImGui.Checkbox("Только то, что продаётся", ref onlyMarketable);
        if (rets.Count == 0)
            ImGui.TextColored(Theme.Gray, "Ретейнеры появятся в списке, когда откроешь колокольчик (Summoning Bell) в этой сессии.");

        var items = Plugin.Data.GetExcelSheet<Item>();
        var key = "," + abbr + ",";
        var list = tasks!.Where(t => t.Level <= level && t.ClassCats.Contains(key))
            .Select(t => (Task: t, Info: items.GetRowOrDefault(t.Item)))
            .Where(x => x.Info is not null)
            .Where(x => !onlyMarketable || (x.Info!.Value.ItemSearchCategory.RowId != 0 && !x.Info.Value.IsUntradable))
            .ToList();

        var region = MarketService.CurrentRegion(true);
        if (region is not null) market.Request(region, list.Select(x => x.Task.Item));
        long Gil(Task t, bool max)
        {
            if (region is null) return 0;
            var p = market.Get(region, t.Item);
            return p is null ? 0 : p.Price * (max ? t.MaxQty : t.MinQty);
        }
        var sorted = list.OrderByDescending(x => Gil(x.Task, true)).ThenByDescending(x => x.Task.Level).ToList();

        ImGui.TextColored(Theme.TextDim, region is null
            ? "Цены недоступны (не определён мир). Показан список вылазок без выручки."
            : $"Вылазок: {sorted.Count}. Цена — самый дешёвый лот на {region}. Количество растёт с {(abbr is "MIN" or "BTN" or "FSH" ? "Gathering" : "iLvl")} ретейнера.");

        var rowH = 44 * Theme.S;
        if (!ImGui.BeginChild("##vent", Vector2.Zero)) { ImGui.EndChild(); return; }
        Theme.VirtualList(sorted.Count, rowH, i =>
        {
            var (t, info) = sorted[i];
            var it = info!.Value;
            var name = NameTranslations.Show(it.Name.ExtractText());
            var p = region is null ? null : market.Get(region, t.Item);
            var need = t.ReqStat > 0 ? $" · нужно {(t.Gatherer ? "Gathering" : "iLvl")} {t.ReqStat}" : "";
            var sub = $"Ур. {t.Level} · ×{t.MinQty}–{t.MaxQty}{need}";
            string? badge = null;
            if (p is not null)
            {
                sub += $" · {p.Price:N0} гил за шт.";
                badge = $"{Gil(t, false):N0}–{Gil(t, true):N0} гил";
            }
            else if (region is not null && market.IsLoading(region, t.Item)) sub += " · цена загружается…";
            if (Theme.Card($"##ve{t.Id}", it.Icon, name, sub, badge, Theme.Gold, false, rowH))
                WikiNav.Open(WikiKind.Item, t.Item, it.Name.ExtractText());
        });
        ImGui.EndChild();
    }
}
