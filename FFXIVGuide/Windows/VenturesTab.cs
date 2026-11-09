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
/// Выгодные вылазки ретейнеров: прицельные вылазки для класса и уровня ретейнера, отсортированные по выручке
/// на рынке твоего мира. Если плагин видел экипировку ретейнера — сколько он принесёт сейчас и что улучшить.
/// </summary>
internal sealed unsafe class VenturesTab
{
    private sealed record Task(uint Id, uint Item, int Level, int[] Qty, int ReqStat, bool Gatherer, string ClassCats, int[] IlvlTiers, int[] PercDoL, int[] PercFsh);

    private sealed record Ret(string Key, string Name, string Abbr, int Level, RetainerSnapshot? Snap);

    private const uint ParamGathering = 72, ParamPerception = 73;

    private readonly GuideData data;
    private readonly MarketService market;
    private List<Task>? tasks;
    private readonly Dictionary<uint, string> catAbbrs = new();
    private int pick;
    private int manualJob;
    private int manualLevel = 100;
    private bool onlyMarketable = true;

    private static readonly (string Abbr, string Name)[] ManualJobs =
    {
        ("MIN", "Шахтёр"), ("BTN", "Ботаник"), ("FSH", "Рыбак"), ("GLA", "Боевой класс"),
    };

    public VenturesTab(GuideData data, MarketService market)
    {
        this.data = data;
        this.market = market;
    }

    private void Build()
    {
        tasks = new List<Task>();
        var normal = Plugin.Data.GetExcelSheet<RetainerTaskNormal>();
        foreach (var t in Plugin.Data.GetExcelSheet<RetainerTask>())
        {
            if (t.IsRandom || t.Task.RowId == 0) continue;
            var n = normal.GetRowOrDefault(t.Task.RowId);
            if (n is null || n.Value.Item.RowId == 0) continue;
            var q = n.Value.Quantity.Select(x => (int)x).ToArray();
            if (q.All(x => x == 0)) continue;
            var par = t.RetainerTaskParameter.ValueNullable;
            int[] Arr(IEnumerable<short>? a) => a?.Select(x => (int)x).ToArray() ?? Array.Empty<int>();
            var gatherer = t.RequiredGathering > 0;
            tasks.Add(new Task(t.RowId, n.Value.Item.RowId, t.RetainerLevel, q,
                gatherer ? t.RequiredGathering : t.RequiredItemLevel, gatherer, CatAbbrs(t.ClassJobCategory.RowId),
                Arr(par?.ItemLevelDoW), Arr(par?.PerceptionDoL), Arr(par?.PerceptionFSH)));
        }
    }

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

    /// <summary>Ретейнеры из текущей сессии плюс запомненные плагином раньше.</summary>
    private static List<Ret> Retainers()
    {
        var res = new List<Ret>();
        var snaps = Plugin.Config.Retainers;
        var rm = RetainerManager.Instance();
        if (rm != null)
            for (var i = 0u; i < rm->GetRetainerCount(); i++)
            {
                var r = rm->GetRetainerBySortedIndex(i);
                if (r == null || r->RetainerId == 0 || r->ClassJob == 0) continue;
                var key = r->RetainerId.ToString();
                res.Add(new Ret(key, r->NameString, GuideData.JobAbbrEnglish(r->ClassJob), r->Level, snaps.GetValueOrDefault(key)));
            }
        foreach (var (key, s) in snaps)
            if (s.ClassJob != 0 && res.All(x => x.Key != key))
                res.Add(new Ret(key, s.Name, GuideData.JobAbbrEnglish(s.ClassJob), s.Level, s));
        return res;
    }

    // ───────────── Статы ретейнера по снятой экипировке ─────────────

    private static int Stat(Item it, bool hq, uint param)
    {
        var v = 0;
        for (var i = 0; i < it.BaseParam.Count; i++)
            if (it.BaseParam[i].RowId == param) v += it.BaseParamValue[i];
        if (hq)
            for (var i = 0; i < it.BaseParamSpecial.Count; i++)
                if (it.BaseParamSpecial[i].RowId == param) v += it.BaseParamValueSpecial[i];
        return v;
    }

    /// <summary>Средний iLvl (как у игрока: 12 слотов, двуручное оружие считается дважды), Gathering, Perception.</summary>
    private static (int Ilvl, int Gathering, int Perception) Stats(RetainerSnapshot snap)
    {
        var items = Plugin.Data.GetExcelSheet<Item>();
        int sum = 0, g = 0, p = 0;
        Item? main = null;
        var offEmpty = true;
        for (var i = 0; i < snap.Equipped.Count && i < 13; i++)
        {
            if (i == 5) continue; // пояс
            var e = snap.Equipped[i];
            if (e.ItemId == 0 || items.GetRowOrDefault(e.ItemId) is not { } it) continue;
            if (i == 0) main = it;
            if (i == 1) offEmpty = false;
            sum += (int)it.LevelItem.RowId;
            g += Stat(it, e.Hq, ParamGathering);
            p += Stat(it, e.Hq, ParamPerception);
        }
        if (offEmpty && main is { } m && m.EquipSlotCategory.ValueNullable?.OffHand == -1) sum += (int)m.LevelItem.RowId;
        return (sum / 12, g, p);
    }

    private static int Tier(int[] thresholds, int stat)
    {
        var t = 0;
        foreach (var x in thresholds)
            if (x > 0 && stat >= x) t++;
        return t;
    }

    public void Draw()
    {
        Theme.Header("Вылазки ретейнеров", "Какие прицельные вылазки приносят больше всего гил и что улучшить ретейнеру");
        if (tasks is null)
        {
            try { Build(); }
            catch (Exception e) { Plugin.Log.Warning(e, "Вылазки: не удалось разобрать таблицы"); tasks = new(); }
        }

        var rets = Retainers();
        string abbr;
        int level;
        RetainerSnapshot? snap = null;
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
            snap = rets[pick].Snap is { Equipped.Count: > 0 } s ? s : null;
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
            ImGui.TextColored(Theme.Gray, "Ретейнеры появятся здесь, когда откроешь колокольчик (Summoning Bell).");

        var isFsh = abbr == "FSH";
        var isGath = abbr is "MIN" or "BTN" or "FSH";
        var stats = snap is null ? ((int, int, int)?)null : Stats(snap);
        if (pick < rets.Count && snap is null)
            ImGui.TextColored(Theme.Gray, "Экипировку этого ретейнера плагин ещё не видел — открой его у колокольчика, и появятся точные количества и советы по апгрейду.");

        var items = Plugin.Data.GetExcelSheet<Item>();
        var key = "," + abbr + ",";
        var list = tasks!.Where(t => t.Level <= level && t.ClassCats.Contains(key))
            .Select(t => (Task: t, Info: items.GetRowOrDefault(t.Item)))
            .Where(x => x.Info is not null)
            .Where(x => !onlyMarketable || (x.Info!.Value.ItemSearchCategory.RowId != 0 && !x.Info.Value.IsUntradable))
            .ToList();

        // Сколько принесёт при текущих статах; следующий порог
        int[] Thresholds(Task t) => isGath ? (isFsh ? t.PercFsh : t.PercDoL) : t.IlvlTiers;
        int MyStat() => stats is null ? 0 : isGath ? stats.Value.Item3 : stats.Value.Item1;
        int QtyNow(Task t)
        {
            var tier = Math.Min(Tier(Thresholds(t), MyStat()), t.Qty.Length - 1);
            return t.Qty[tier];
        }
        bool CanDo(Task t) => stats is null || !t.Gatherer || stats.Value.Item2 >= t.ReqStat;

        var region = MarketService.CurrentRegion(true);
        if (region is not null) market.Request(region, list.Select(x => x.Task.Item));
        long Price(Task t) => region is null ? 0 : market.Get(region, t.Item)?.Price ?? 0;
        long GilNow(Task t) => Price(t) * (stats is null ? t.Qty.Max() : QtyNow(t));
        var sorted = list.OrderByDescending(x => CanDo(x.Task)).ThenByDescending(x => GilNow(x.Task)).ThenByDescending(x => x.Task.Level).ToList();

        if (stats is { } st)
            DrawUpgrade(snap!, abbr, level, isGath, st, sorted.Take(5).Select(x => x.Task).ToList(), Thresholds, region);

        ImGui.TextColored(Theme.TextDim, region is null
            ? "Цены недоступны (не определён мир). Показан список вылазок без выручки."
            : $"Вылазок: {sorted.Count}. Цена — самый дешёвый лот на {region}.");

        var rowH = 44 * Theme.S;
        if (!ImGui.BeginChild("##vent", Vector2.Zero)) { ImGui.EndChild(); return; }
        Theme.VirtualList(sorted.Count, rowH, i =>
        {
            var (t, info) = sorted[i];
            var it = info!.Value;
            var name = NameTranslations.Show(it.Name.ExtractText());
            var price = Price(t);
            var need = t.ReqStat > 0 ? $" · нужно {(t.Gatherer ? "Gathering" : "iLvl")} {t.ReqStat}" : "";
            string sub, badge;
            if (stats is null)
            {
                sub = $"Ур. {t.Level} · ×{t.Qty.Where(x => x > 0).DefaultIfEmpty(0).Min()}–{t.Qty.Max()}{need}";
                badge = price > 0 ? $"до {price * t.Qty.Max():N0} гил" : "";
            }
            else if (!CanDo(t))
            {
                sub = $"Ур. {t.Level}{need} — не хватает Gathering ({stats.Value.Item2})";
                badge = "";
            }
            else
            {
                var now = QtyNow(t);
                var th = Thresholds(t);
                var tier = Tier(th, MyStat());
                var next = tier < th.Length && tier + 1 < t.Qty.Length && th[tier] > 0
                    ? $" · при {(isGath ? "Perception" : "iLvl")} {th[tier]} → ×{t.Qty[tier + 1]}" : " · максимум";
                sub = $"Ур. {t.Level} · сейчас ×{now}{next}";
                badge = price > 0 ? $"{price * now:N0} гил" : "";
            }
            if (price > 0) sub += $" · {price:N0} за шт.";
            else if (region is not null && market.IsLoading(region, t.Item)) sub += " · цена загружается…";
            if (Theme.Card($"##ve{t.Id}", it.Icon, name, sub, badge.Length > 0 ? badge : null, Theme.Gold, false, rowH))
                WikiNav.Open(WikiKind.Item, t.Item, it.Name.ExtractText());
        });
        ImGui.EndChild();
    }

    // ───────────── Апгрейд экипировки ─────────────

    private static readonly (int Slot, GearSlot Gear, string Name)[] Slots =
    {
        (0, GearSlot.MainHand, "Оружие"), (1, GearSlot.OffHand, "Вторая рука"), (2, GearSlot.Head, "Голова"), (3, GearSlot.Body, "Тело"),
        (4, GearSlot.Hands, "Руки"), (6, GearSlot.Legs, "Ноги"), (7, GearSlot.Feet, "Обувь"), (8, GearSlot.Ears, "Серьги"),
        (9, GearSlot.Neck, "Ожерелье"), (10, GearSlot.Wrists, "Браслеты"), (11, GearSlot.Ring, "Кольцо"), (12, GearSlot.Ring, "Кольцо"),
    };

    private void DrawUpgrade(RetainerSnapshot snap, string abbr, int level, bool isGath, (int Ilvl, int Gathering, int Perception) st,
        List<Task> top, Func<Task, int[]> thresholds, string? region)
    {
        Theme.Panel("vupg", () =>
        {
            Theme.SubHeader("Экипировка и апгрейд");
            ImGui.TextColored(Theme.Text, isGath
                ? $"Gathering {st.Gathering} · Perception {st.Perception} · iLvl {st.Ilvl}"
                : $"Средний iLvl {st.Ilvl}");
            ImGui.TextColored(Theme.Gray, $"Снято {snap.Updated:dd.MM HH:mm}. {(isGath ? "Gathering открывает вылазки, Perception увеличивает количество." : "iLvl увеличивает количество.")}");

            // Ближайший порог среди лучших вылазок
            var stat = isGath ? st.Perception : st.Ilvl;
            var goals = top.Select(t => thresholds(t).Where(x => x > stat).DefaultIfEmpty(0).Min()).Where(x => x > 0).ToList();
            if (goals.Count > 0)
                ImGui.TextColored(Theme.Gold, $"Следующий порог для выгодных вылазок: {(isGath ? "Perception" : "iLvl")} {goals.Min()} (сейчас {stat}).");
            else
                ImGui.TextColored(Theme.Green, "Для лучших вылазок уже максимальное количество.");

            ImGui.Dummy(new Vector2(0, 3 * Theme.S));
            ImGui.TextColored(Theme.TextDim, $"Лучшее, что можно надеть на ур. {level} (рынок или NPC):");
            var cacheKey = $"{snap.Updated.Ticks}|{abbr}|{level}";
            if (upgradeKey != cacheKey) { upgradeKey = cacheKey; upgradeCache = ComputeUpgrades(snap, abbr, level, isGath); }
            foreach (var (slot, name, id, gain, curName) in upgradeCache)
            {
                var extra = $"{name} · +{gain} {(isGath ? "Gathering+Perception" : "iLvl")}" + (curName.Length > 0 ? $" (сейчас {NameTranslations.Show(curName)})" : " (пусто)");
                if (region is not null && WikiView.ItemInfo(id).Name.Length > 0)
                {
                    market.Request(region, new[] { id });
                    if (market.Get(region, id) is { } pr) extra += $" · от {pr.Price:N0} гил";
                }
                WikiView.ItemLine(id, 0, extra, "vup" + slot);
            }
            if (upgradeCache.Count == 0) ImGui.TextColored(Theme.Green, "Ретейнер одет в лучшее для своего уровня.");
            else ImGui.TextColored(Theme.Gray, "Для сборщиков HQ-версия даёт больше статов — оценка считает HQ для вещей с рынка.");
        });
    }

    private static HashSet<uint>? gilShop;

    /// <summary>Предметы, которые продают NPC за гил.</summary>
    private static HashSet<uint> GilShop => gilShop ??= Plugin.Data.GetSubrowExcelSheet<GilShopItem>()
        .SelectMany(r => r).Select(x => x.Item.RowId).Where(x => x != 0).ToHashSet();

    private string upgradeKey = "";
    private List<(int Slot, string Name, uint Id, int Gain, string Cur)> upgradeCache = new();

    private List<(int, string, uint, int, string)> ComputeUpgrades(RetainerSnapshot snap, string abbr, int level, bool isGath)
    {
        var items = Plugin.Data.GetExcelSheet<Item>();
        var res = new List<(int, string, uint, int, string)>();
        var usedRing = new HashSet<uint>();
        {
            foreach (var (slot, gear, name) in Slots)
            {
                var cur = slot < snap.Equipped.Count ? snap.Equipped[slot] : new StoredItem();
                var curItem = cur.ItemId != 0 ? items.GetRowOrDefault(cur.ItemId) : null;
                int Score(Item it, bool hq) => isGath ? Stat(it, hq, ParamGathering) + Stat(it, hq, ParamPerception) : (int)it.LevelItem.RowId;
                var curScore = curItem is { } ci ? Score(ci, cur.Hq) : 0;
                var best = data.Gear
                    .Where(g => g.Slot == gear && g.EquipLevel <= level && !g.Pvp && data.JobInCategory(g.ClassJobCategory, abbr) && !usedRing.Contains(g.Id))
                    .Select(g => (G: g, It: items.GetRowOrDefault(g.Id)))
                    .Where(x => x.It is not null && (x.G.Marketable || GilShop.Contains(x.G.Id)))
                    .Select(x => (x.G, Score: Score(x.It!.Value, x.G.Marketable)))
                    .Where(x => x.Score > curScore)
                    .OrderByDescending(x => x.Score).ThenBy(x => x.G.ItemLevel)
                    .FirstOrDefault();
                if (best.G is null) continue;
                if (gear == GearSlot.Ring) usedRing.Add(best.G.Id);
                res.Add((slot, name, best.G.Id, best.Score - curScore, curItem?.Name.ExtractText() ?? ""));
            }
        }
        return res;
    }
}
