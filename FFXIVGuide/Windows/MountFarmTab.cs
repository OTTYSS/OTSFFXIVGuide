using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>
/// Фарм коллекций: какие маунты, минёны, ролы оркестриона, карты, броня чокобо, аксессуары, причёски и т.д.
/// падают в данжах, триалах и рейдах (или меняются на токены), что у тебя уже есть и сколько токенов набрано.
/// </summary>
internal sealed class MountFarmTab
{
    private sealed record Reward(uint Item, string Name, uint Icon, int Kind, uint Token, int TokenCost);

    /// <summary>Виды наград: тип действия предмета (ItemAction.Action) и подписи.</summary>
    private static readonly (uint Action, string Plural, string Single)[] Kinds =
    {
        (1322, "Маунты", "маунт"), (853, "Минёны", "минён"), (25183, "Оркестрион", "оркестрион"), (3357, "Карты", "карта TT"),
        (1013, "Броня чокобо", "броня чокобо"), (20086, "Аксессуары", "аксессуар"), (2633, "Причёски, эмоции", "открывает причёску/эмоцию"),
        (37312, "Очки", "очки"), (29459, "Framer's kits", "Framer's kit"),
    };
    private sealed record DutyRewards(uint InstanceId, string Name, string Type, uint Icon, List<Reward> Rewards);

    private readonly GuideData data;
    private List<DutyRewards>? duties;
    private int filter = 1;
    private static readonly string[] Filters = { "Все", "Есть что фармить", "Всё собрано" };
    private readonly bool[] shown = Kinds.Select(k => k.Action is 1322 or 853).ToArray();
    private string search = "";
    private int builtFor = -1;

    public MountFarmTab(GuideData data) => this.data = data;

    private static bool Owned(uint item)
    {
        var row = Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(item);
        return row is { } r && PlayerInfo.Ready && Plugin.Unlocks.IsItemUnlocked(r);
    }

    private List<DutyRewards> Build(OfflineIndex ix)
    {
        var items = Plugin.Data.GetExcelSheet<Item>();
        var kindOf = Kinds.Select((k, i) => (k.Action, i)).ToDictionary(x => x.Action, x => x.i);
        var collectibles = items
            .Where(i => i.ItemAction.ValueNullable is { } a && kindOf.ContainsKey(a.Action.RowId))
            .ToDictionary(i => i.RowId, i => kindOf[i.ItemAction.Value.Action.RowId]);

        var byDuty = new Dictionary<uint, List<Reward>>();
        void Add(uint duty, Reward r)
        {
            if (!byDuty.TryGetValue(duty, out var l)) byDuty[duty] = l = new List<Reward>();
            if (!l.Any(x => x.Item == r.Item)) l.Add(r);
        }

        foreach (var (item, kind) in collectibles)
        {
            var info = WikiView.ItemInfo(item);
            // Падает прямо в данже
            if (ix.ItemInstances.TryGetValue(item, out var inst))
                foreach (var d in inst) Add(d, new Reward(item, info.Name, info.Icon, kind, 0, 0));
            // Меняется на токен, который падает в данже
            if (ix.Trades.TryGetValue(item, out var trades))
                foreach (var t in trades)
                    foreach (var pay in t.Pay)
                        if (ix.ItemInstances.TryGetValue(pay.Item, out var tokInst) && tokInst.Count <= 3)
                            foreach (var d in tokInst) Add(d, new Reward(item, info.Name, info.Icon, kind, pay.Item, pay.Amount));
        }

        var cfcs = Plugin.Data.GetExcelSheet<ContentFinderCondition>();
        var result = new List<DutyRewards>();
        foreach (var (instId, rewards) in byDuty)
        {
            var cfc = cfcs.FirstOrDefault(c => c.ContentLinkType == 1 && c.Content.RowId == instId);
            if (cfc.RowId == 0) continue;
            var duty = data.Duties.FirstOrDefault(d => d.CfcId == cfc.RowId);
            result.Add(new DutyRewards(instId, GuideData.Capitalize(cfc.Name.ExtractText()), cfc.ContentType.ValueNullable?.Name.ExtractText() ?? "",
                duty?.Icon ?? 0, rewards.OrderBy(r => r.Kind).ThenBy(r => r.Name).ToList()));
        }
        return result.OrderBy(d => d.Type).ThenBy(d => d.Name).ToList();
    }

    public void Draw()
    {
        Theme.Header("Фарм коллекций", "Что из коллекций падает в данжах, триалах и рейдах, что у тебя уже есть и сколько токенов набрано");
        var ix = OfflineDb.Instance.Index;
        if (ix is null)
        {
            Theme.EmptyState(FontAwesomeIcon.Horse, "Нужна офлайн-база", "Источники маунтов берутся из офлайн-базы — скачай её в Настройках → «База данных».");
            return;
        }
        if (duties is null || builtFor != OfflineDb.Version) { duties = Build(ix); builtFor = OfflineDb.Version; }

        Theme.Chips("mffilter", Filters, ref filter);
        for (var k = 0; k < Kinds.Length; k++)
        {
            ImGui.Checkbox($"{Kinds[k].Plural}##mfk{k}", ref shown[k]);
            if (k < Kinds.Length - 1 && ImGui.GetContentRegionAvail().X > 160 * Theme.S) ImGui.SameLine();
        }
        ImGui.SetNextItemWidth(260 * Theme.S);
        ImGui.InputTextWithHint("##mfsearch", "Данж или награда…", ref search, 64);

        var term = search.Trim();
        var rows = duties.Select(d => (d, list: d.Rewards.Where(r => shown[r.Kind]).ToList()))
            .Where(x => x.list.Count > 0)
            .Where(x => term.Length < 2 || NameTranslations.Matches(x.d.Name, term) || x.list.Any(r => NameTranslations.Matches(r.Name, term)))
            .Where(x => filter == 0 || (filter == 1 ? x.list.Any(r => !Owned(r.Item)) : x.list.All(r => Owned(r.Item))))
            .ToList();
        var all = rows.SelectMany(x => x.list).Select(r => r.Item).Distinct().ToList();
        ImGui.TextColored(Theme.TextDim, $"Контента: {rows.Count} · наград: {all.Count}, собрано {all.Count(Owned)}");

        if (!ImGui.BeginChild("##mf", Vector2.Zero)) { ImGui.EndChild(); return; }
        Theme.Grid("mfgrid", rows.Count, 400f, i =>
        {
            var (d, list) = rows[i];
            Theme.Panel($"mf{d.InstanceId}", () =>
            {
                var duty = data.Duties.FirstOrDefault(x => x.InstanceContentId == d.InstanceId);
                WikiView.DutyLine(d.InstanceId, d.Name, d.Type, "mf");
                if (duty is not null)
                {
                    var (badge, color) = DutiesTab.StatusBadge(duty);
                    if (badge is not null) Theme.InlineBadge(badge, color);
                }
                foreach (var r in list)
                {
                    var own = Owned(r.Item);
                    var extra = own ? "есть ✓" : Kinds[r.Kind].Single;
                    if (r.Token != 0)
                    {
                        var have = PlayerInfo.ItemCount(r.Token);
                        extra += $" · за {r.TokenCost}× {NameTranslations.Show(WikiView.ItemInfo(r.Token).Name)} (есть {have})";
                    }
                    WikiView.ItemLine(r.Item, 0, extra, $"mfr{d.InstanceId}");
                }
            });
        });
        ImGui.EndChild();
    }
}
