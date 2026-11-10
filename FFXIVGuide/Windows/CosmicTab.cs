using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Cosmic Exploration: миссии для крафтеров и сборщиков — где, условия, звёзды и награды.</summary>
internal sealed class CosmicTab
{
    private sealed record Mission(uint Id, string Name, string Place, string Jobs, int LevelGroup, int Time, bool Special, string Cond,
        int Silver, int Gold, int Cosmo, int Planet, uint RewardItem, int RewardCount);

    private static readonly string[] Jobs = { "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL", "MIN", "BTN", "FSH" };
    private readonly GuideData data;
    private List<Mission>? missions;
    private int job;
    private string place = "";
    private string search = "";
    private bool onlySpecial;

    public CosmicTab(GuideData data) => this.data = data;

    private void Build()
    {
        missions = new List<Mission>();
        foreach (var m in Plugin.Data.GetExcelSheet<WKSMissionUnit>())
        {
            var name = new string(m.Name.ExtractText().Where(c => c >= ' ' && (c < '\uE000' || c > '\uF8FF')).ToArray()).Trim();
            if (name.Length == 0) continue;
            var cats = m.ClassJobCategory.Where(c => c.RowId != 0).Select(c => c.RowId).ToList();
            var jobs = string.Join(",", Jobs.Where(a => cats.Any(c => data.JobInCategory(c, a))));
            if (jobs.Length == 0) continue;
            var cond = "";
            if (m.WKSMissionLotterySpecialCond.ValueNullable is { } sc)
            {
                var parts = new List<string>();
                if (sc.WeatherRequired.RowId != 0) parts.Add("погода: " + (sc.WeatherRequired.ValueNullable?.Name.ExtractText() ?? ""));
                if (sc.StartTimeHour != sc.EndTimeHour) parts.Add($"ET {sc.StartTimeHour:00}:00–{sc.EndTimeHour:00}:00");
                cond = string.Join(", ", parts);
            }
            var rw = m.MissionReward.ValueNullable;
            missions.Add(new Mission(m.RowId, name, m.PlaceName.ValueNullable?.Name.ExtractText() ?? "", "," + jobs + ",", m.LevelGroup, m.MissionTime,
                m.IsSpecialQuest || cond.Length > 0, cond, (int)m.SilverStarRequirement, (int)m.GoldStarRequirement,
                rw?.CosmoCredits ?? 0, rw?.PlanetCredits ?? 0, rw?.Item.RowId ?? 0, rw?.ItemCount ?? 0));
        }
    }

    public IEnumerable<SearchHit> Search(string term, Action<MainTab> go)
    {
        if (missions is null) Build();
        return missions!.Where(m => m.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).Select(m => new SearchHit("Cosmic Exploration", m.Name,
            $"{m.Jobs.Trim(',')} · ступень {m.LevelGroup}", 0, () =>
            {
                var j = Array.FindIndex(Jobs, a => m.Jobs.Contains("," + a + ","));
                if (j >= 0) job = j;
                place = "";
                onlySpecial = false;
                search = m.Name;
                go(MainTab.Cosmic);
            }));
    }

    public void Draw()
    {
        Theme.Header("Cosmic Exploration", "Миссии для крафтеров и сборщиков: где, когда, сколько звёзд нужно и что дают");
        if (missions is null)
        {
            try { Build(); }
            catch (Exception e) { Plugin.Log.Warning(e, "Cosmic: ошибка разбора"); missions = new(); }
        }
        if (missions!.Count == 0) { Theme.EmptyState(FontAwesomeIcon.Rocket, "Нет данных", "Миссии Cosmic Exploration не найдены в файлах игры."); return; }

        ImGui.SetNextItemWidth(110 * Theme.S);
        if (ImGui.BeginCombo("##csjob", Jobs[job]))
        {
            for (var i = 0; i < Jobs.Length; i++)
                if (ImGui.Selectable(Jobs[i], i == job)) job = i;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        var places = new[] { "" }.Concat(missions.Select(m => m.Place).Where(p => p.Length > 0).Distinct()).ToArray();
        ImGui.SetNextItemWidth(200 * Theme.S);
        if (ImGui.BeginCombo("##csplace", place.Length == 0 ? "Все места" : place))
        {
            foreach (var p in places)
                if (ImGui.Selectable(p.Length == 0 ? "Все места" : p, p == place)) place = p;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.Checkbox("Только особые (погода/время)", ref onlySpecial);
        ImGui.SetNextItemWidth(Math.Min(420 * Theme.S, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##cssearch", "Название миссии…", ref search, 64);

        var key = "," + Jobs[job] + ",";
        var list = missions.Where(m => m.Jobs.Contains(key) && (place.Length == 0 || m.Place == place) && (!onlySpecial || m.Special))
            .Where(m => search.Length == 0 || m.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.LevelGroup).ThenBy(m => m.Id).ToList();
        ImGui.PushTextWrapPos(0);
        ImGui.TextColored(Theme.TextDim, $"Миссий: {list.Count}. Особые миссии появляются только при указанной погоде или времени Эорзеи — за них больше награда. Кредиты тратятся у торговцев на базе.");
        ImGui.PopTextWrapPos();

        var rowH = 48 * Theme.S;
        if (!ImGui.BeginChild("##cosmic", Vector2.Zero)) { ImGui.EndChild(); return; }
        Theme.VirtualList(list.Count, rowH, i =>
        {
            var m = list[i];
            var sub = new List<string> { $"ступень {m.LevelGroup} из 6" };
            if (m.Place.Length > 0) sub.Add(m.Place);
            if (m.Time > 0) sub.Add($"лимит {m.Time / 60}:{m.Time % 60:00}");
            if (m.Silver > 0) sub.Add($"очков на серебро {m.Silver} / золото {m.Gold}");
            if (m.Cond.Length > 0) sub.Add(m.Cond);
            var reward = new List<string>();
            if (m.Cosmo > 0) reward.Add($"{m.Cosmo} cosmo");
            if (m.Planet > 0) reward.Add($"{m.Planet} planet");
            if (m.RewardItem != 0) reward.Add($"{WikiView.ItemInfo(m.RewardItem).Name} ×{Math.Max(1, m.RewardCount)}");
            Theme.Card($"##cs{m.Id}", m.RewardItem != 0 ? WikiView.ItemInfo(m.RewardItem).Icon : 0, (m.Special ? "★ " : "") + m.Name,
                string.Join(" · ", sub) + (reward.Count > 0 ? " — награда: " + string.Join(", ", reward) : ""), null, Theme.Gold, false, rowH);
        });
        ImGui.EndChild();
    }
}
