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
/// Все маунты: что есть, чего нет и как получить. Способ получения — по предмету-свистку (магазины, обмены,
/// данжи, квесты, достижения, крафт, Mog Station, рынок) из файлов игры и офлайн-базы.
/// </summary>
internal sealed class MountsTab
{
    [Flags]
    private enum Src
    {
        None = 0, Duty = 1, Exchange = 2, GilShop = 4, Quest = 8, Achievement = 16, Craft = 32, MogStation = 64,
        Fate = 128, Coffer = 256, Market = 512, NoItem = 1024,
    }

    private static readonly (Src Flag, string Name)[] SrcNames =
    {
        (Src.Duty, "Данжи, триалы, рейды"), (Src.Exchange, "Обмен на валюту"), (Src.GilShop, "За гил у NPC"), (Src.Quest, "Квесты"),
        (Src.Achievement, "Достижения"), (Src.Craft, "Крафт"), (Src.MogStation, "Mog Station"), (Src.Fate, "FATE"),
        (Src.Coffer, "Сундуки"), (Src.Market, "Рынок"), (Src.NoItem, "Без предмета (квест, событие)"),
    };

    private sealed record MountRow(uint Id, string Name, uint Icon, uint ItemId, int Seats, bool Music, Src Sources, uint Achievement, string Desc);

    private readonly GuideData data;
    private readonly MarketService market;
    private List<MountRow>? rows;
    private int builtFor = -1;
    private int show = 1;
    private static readonly string[] ShowNames = { "Все", "Нет у меня", "Есть" };
    private int srcFilter;
    private int sort;
    private static readonly string[] Sorts = { "По имени", "Сначала многоместные", "Сначала простые в получении" };
    private string search = "";
    private uint selected;

    // Свисток маунта — предмет с действием 1322
    private const uint ActionMount = 1322;

    public MountsTab(GuideData data, MarketService market)
    {
        this.data = data;
        this.market = market;
    }

    private void Build()
    {
        builtFor = OfflineDb.Version;
        var items = Plugin.Data.GetExcelSheet<Item>();
        var whistle = new Dictionary<uint, uint>();
        foreach (var it in items)
            if (it.ItemAction.ValueNullable is { } a && a.Action.RowId == ActionMount && a.Data.Count > 0)
                whistle.TryAdd(a.Data[0], it.RowId);
        var achByItem = new Dictionary<uint, uint>();
        foreach (var a in Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Achievement>())
            if (a.Item.RowId != 0) achByItem.TryAdd(a.Item.RowId, a.RowId);
        var crafted = new HashSet<uint>(Plugin.Data.GetExcelSheet<Recipe>().Select(r => r.ItemResult.RowId));
        var gilShop = new HashSet<uint>();
        foreach (var row in Plugin.Data.GetSubrowExcelSheet<GilShopItem>())
            foreach (var s in row) gilShop.Add(s.Item.RowId);
        var ix = OfflineDb.Instance.Index;
        var transient = Plugin.Data.GetExcelSheet<MountTransient>();

        rows = new List<MountRow>();
        foreach (var m in Plugin.Data.GetExcelSheet<Mount>())
        {
            if (m.Icon == 0 || m.Order < 0 || m.Singular.IsEmpty) continue;
            var item = whistle.GetValueOrDefault(m.RowId);
            var src = Src.None;
            uint ach = 0;
            if (item == 0) src = Src.NoItem;
            else
            {
                var it = items.GetRowOrDefault(item);
                if (it is { } r && r.ItemSearchCategory.RowId != 0 && !r.IsUntradable) src |= Src.Market;
                if (crafted.Contains(item)) src |= Src.Craft;
                if (gilShop.Contains(item)) src |= Src.GilShop;
                if (achByItem.TryGetValue(item, out ach)) src |= Src.Achievement;
                if (ix is not null)
                {
                    if (ix.ItemInstances.ContainsKey(item)) src |= Src.Duty;
                    if (ix.Trades.TryGetValue(item, out var tr))
                    {
                        src |= Src.Exchange;
                        // Токен, который падает в данже, тоже считаем данжем
                        if (tr.Any(t => t.Pay.Any(p => ix.ItemInstances.ContainsKey(p.Item)))) src |= Src.Duty;
                    }
                    if (ix.Vendors.ContainsKey(item)) src |= Src.GilShop;
                    if (ix.ItemQuests.ContainsKey(item)) src |= Src.Quest;
                    if (ix.Mogstation.ContainsKey(item)) src |= Src.MogStation;
                    if (ix.FateSources.ContainsKey(item)) src |= Src.Fate;
                    if (ix.Loot.ContainsKey(item)) src |= Src.Coffer;
                }
            }
            var desc = transient.GetRowOrDefault(m.RowId)?.Description.ExtractText() ?? "";
            rows.Add(new MountRow(m.RowId, GuideData.Capitalize(m.Singular.ExtractText()), m.Icon, item, m.ExtraSeats + 1, m.RideBGM.RowId != 0, src, ach, desc));
        }
    }

    private static bool Owned(uint mountId) =>
        PlayerInfo.Ready && Plugin.Data.GetExcelSheet<Mount>().GetRowOrDefault(mountId) is { } r && Plugin.Unlocks.IsMountUnlocked(r);

    private static string SourceText(Src s) =>
        string.Join(", ", SrcNames.Where(n => (s & n.Flag) != 0).Select(n => n.Name.ToLowerInvariant()));

    /// <summary>Чем ниже, тем проще: рынок, гил, крафт, обмен — проще, чем дроп и Mog Station.</summary>
    private static int Difficulty(Src s) =>
        (s & (Src.Market | Src.GilShop)) != 0 ? 0 : (s & (Src.Craft | Src.Exchange | Src.Quest)) != 0 ? 1 : (s & (Src.Duty | Src.Fate | Src.Coffer | Src.Achievement)) != 0 ? 2 : 3;

    public IEnumerable<SearchHit> Search(string term, Action<MainTab> go)
    {
        if (rows is null) Build();
        return rows!.Where(r => NameTranslations.Matches(r.Name, term))
            .Select(r => new SearchHit("Маунты", NameTranslations.Show(r.Name), SourceText(r.Sources), r.Icon, () => { selected = r.Id; search = ""; show = 0; srcFilter = 0; go(MainTab.Mounts); }));
    }

    public void Draw()
    {
        Theme.Header("Маунты", "Все маунты: что есть, чего не хватает и как получить");
        if (rows is null || builtFor != OfflineDb.Version)
        {
            try { Build(); }
            catch (Exception e) { Plugin.Log.Warning(e, "Маунты: ошибка разбора"); rows = new(); }
        }
        if (!PlayerInfo.Ready) { Theme.Panel("mtnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем, чтобы увидеть свои маунты.")); return; }

        var have = rows!.Count(r => Owned(r.Id));
        Theme.Panel("mtsum", () =>
        {
            Theme.Progress("Маунтов собрано", have, rows.Count, Theme.Gold);
            if (OfflineDb.Instance.Index is null)
                ImGui.TextColored(Theme.Gold, "Без офлайн-базы (Настройки → «База данных») не видно дропа в данжах, обменов, квестов и Mog Station.");
        });

        Theme.Chips("mtshow", ShowNames, ref show);
        var srcLabels = new[] { "Любой способ" }.Concat(SrcNames.Select(n => n.Name)).ToArray();
        ImGui.SetNextItemWidth(230 * Theme.S);
        if (ImGui.BeginCombo("##mtsrc", srcLabels[srcFilter]))
        {
            for (var i = 0; i < srcLabels.Length; i++)
                if (ImGui.Selectable(srcLabels[i], srcFilter == i)) srcFilter = i;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(230 * Theme.S);
        if (ImGui.BeginCombo("##mtsort", Sorts[sort]))
        {
            for (var i = 0; i < Sorts.Length; i++)
                if (ImGui.Selectable(Sorts[i], sort == i)) sort = i;
            ImGui.EndCombo();
        }
        Theme.Search("##mtsearch", "Название маунта…", ref search);
        Theme.PreviewSizeChips("mtsize", true);

        var term = search.Trim();
        IEnumerable<MountRow> q = rows.Where(r => show == 0 || Owned(r.Id) == (show == 2))
            .Where(r => srcFilter == 0 || (r.Sources & SrcNames[srcFilter - 1].Flag) != 0)
            .Where(r => term.Length < 2 || NameTranslations.Matches(r.Name, term));
        q = sort switch
        {
            1 => q.OrderByDescending(r => r.Seats).ThenBy(r => r.Name),
            2 => q.OrderBy(r => Difficulty(r.Sources)).ThenBy(r => r.Name),
            _ => q.OrderBy(r => NameTranslations.Show(r.Name)),
        };
        var list = q.ToList();
        ImGui.TextColored(Theme.TextDim, $"Показано: {list.Count}");

        var avail = ImGui.GetContentRegionAvail();
        var rowH = Theme.PreviewRowHeight;
        if (ImGui.BeginChild("##mtlist", new Vector2(Theme.SplitWidth("mounts", avail.X, 0.5f), 0)))
        {
            if (Theme.PreviewTiles(true))
                Theme.TileGrid(list.Count, 150 * Theme.S,
                    i => ($"##mtt{list[i].Id}", list[i].Icon + 64000, list[i].Icon, list[i].Name, Owned(list[i].Id) ? "есть" : null, Theme.Green, selected == list[i].Id),
                    i => selected = list[i].Id);
            else Theme.VirtualList(list.Count, rowH, i =>
            {
                var r = list[i];
                var own = Owned(r.Id);
                var sub = SourceText(r.Sources) + (r.Seats > 1 ? $" · мест: {r.Seats}" : "");
                if (Theme.Card($"##mt{r.Id}", r.Icon, NameTranslations.Show(r.Name), sub, own ? "есть" : null, Theme.Green, selected == r.Id, rowH))
                    selected = r.Id;
            });
        }
        ImGui.EndChild();
        Theme.Splitter("mounts", avail.X, 0.5f);
        if (ImGui.BeginChild("##mtdet", Vector2.Zero)) DrawDetails(list.FirstOrDefault(r => r.Id == selected) ?? rows.FirstOrDefault(r => r.Id == selected));
        ImGui.EndChild();
    }

    private void DrawDetails(MountRow? r)
    {
        if (r is null) { Theme.EmptyState(FontAwesomeIcon.Horse, "Выбери маунта", "Слева все маунты игры. Фильтр сверху — по тому, как их получают."); return; }
        Theme.Panel("mtd", () =>
        {
            Theme.Banner(r.Icon + 64000, Math.Min(ImGui.GetContentRegionAvail().X, 384 * Theme.S));
            Theme.LargeText(Theme.Gold, NameTranslations.Show(r.Name));
            var own = Owned(r.Id);
            Theme.InlineBadge(own ? "есть у тебя" : "нет", own ? Theme.Green : Theme.TextDim);
            ImGui.SameLine();
            Theme.InlineBadge(r.Seats > 1 ? $"мест: {r.Seats}" : "одноместный", Theme.Blue);
            if (r.Music) { ImGui.SameLine(); Theme.InlineBadge("своя музыка", Theme.Blue); }
            if (r.Desc.Length > 0)
            {
                Translator.Instance.Request(new[] { r.Desc });
                ImGui.PushTextWrapPos(0);
                ImGui.TextColored(Theme.Text, (Translator.Instance.Get(r.Desc) ?? r.Desc).Replace("\n", " "));
                ImGui.PopTextWrapPos();
            }
        });

        Theme.Panel("mtsrcp", () =>
        {
            Theme.SubHeader("Как получить");
            if (r.ItemId == 0)
            {
                ImGui.PushTextWrapPos(0);
                ImGui.TextColored(Theme.TextDim, "Этот маунт выдаётся не предметом: обычно это награда за квест, сюжет, событие или особый контент.");
                ImGui.PopTextWrapPos();
                return;
            }
            foreach (var (flag, name) in SrcNames)
                if ((r.Sources & flag) != 0) ImGui.BulletText(name);
            if (r.Achievement != 0 && Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Achievement>().GetRowOrDefault(r.Achievement) is { } a)
            {
                ImGui.PushTextWrapPos(0);
                ImGui.TextColored(Theme.Text, $"Достижение «{NameTranslations.Show(a.Name.ExtractText())}»: {a.Description.ExtractText()}");
                ImGui.PopTextWrapPos();
            }
            if ((r.Sources & Src.Market) != 0 && MarketService.CurrentRegion(true) is { } region)
            {
                market.Request(region, new[] { r.ItemId });
                if (market.Get(region, r.ItemId) is { } p) ImGui.TextColored(Theme.Gold, $"На рынке ({region}): от {p.Price:N0} гил");
            }
            ImGui.Dummy(new Vector2(0, 4 * Theme.S));
            WikiView.ItemLine(r.ItemId, 0, "нажми — все источники: NPC, данжи, цены", $"mtitem{r.Id}");
        });
    }
}
