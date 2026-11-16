using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>
/// Планировщик прокачки ремесленников и собирателей: классовый квест, рецепты для первого крафта,
/// чем добивать уровень, список покупок с учётом инвентаря и ретейнеров и цены рынка.
/// </summary>
internal sealed class LevelPlannerTab
{
    private readonly MarketService market;
    private int job = 8;             // ClassJob: 8–15 ремесленники, 16–18 собиратели
    private int grindCount = 10;
    private bool includeFirst = true;
    private bool includeRetainers = true;
    private int levelOverride;       // 0 — текущий уровень персонажа

    public LevelPlannerTab(MarketService market) => this.market = market;

    private sealed record Ing(uint Item, string Name, int Amount);
    private sealed record Rec(uint Id, uint Item, string Name, uint Icon, int Level, int Yield, string Category, List<Ing> Ings, bool AllMarket);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, List<Rec>> recipeCache = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Task> recipeTasks = new();

    /// <summary>Готовы ли рецепты профессии; если нет — запускает сборку в фоне (чтобы окно не подвисало).</summary>
    private static bool RecipesReady(int craftType)
    {
        if (recipeCache.ContainsKey(craftType)) return true;
        recipeTasks.GetOrAdd(craftType, ct => Task.Run(() => Recipes(ct)));
        return false;
    }
    private static Dictionary<uint, Rec>? byItemCache;
    private static int byItemJob = -1;

    private static List<Rec> Recipes(int craftType)
    {
        if (recipeCache.TryGetValue(craftType, out var c)) return c;
        var items = Plugin.Data.GetExcelSheet<Item>();
        var list = new List<Rec>();
        foreach (var r in Plugin.Data.GetExcelSheet<Recipe>())
        {
            if (r.CraftType.RowId != (uint)craftType || r.ItemResult.RowId == 0) continue;
            if (r.IsExpert || r.IsSpecializationRequired || r.SecretRecipeBook.RowId != 0 || r.Quest.RowId != 0) continue;
            if (r.RecipeLevelTable.ValueNullable is not { } lt || lt.Stars > 0) continue;
            if (items.GetRowOrDefault(r.ItemResult.RowId) is not { } it || it.Name.IsEmpty || it.IsCollectable) continue;
            var ings = new List<Ing>();
            var allMarket = true;
            for (var i = 0; i < r.Ingredient.Count; i++)
            {
                var id = r.Ingredient[i].RowId;
                var amt = r.AmountIngredient[i];
                if (id == 0 || amt == 0) continue;
                var ii = items.GetRowOrDefault(id);
                ings.Add(new Ing(id, ii?.Name.ExtractText() ?? $"#{id}", amt));
                if (ii is not { } iv || iv.ItemSearchCategory.RowId == 0) allMarket = false;
            }
            list.Add(new Rec(r.RowId, it.RowId, it.Name.ExtractText(), it.Icon, lt.ClassJobLevel, Math.Max(1, (int)r.AmountResult),
                it.ItemUICategory.ValueNullable?.Name.ExtractText() ?? "", ings, allMarket));
        }
        list.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : a.Id.CompareTo(b.Id));
        recipeCache[craftType] = list;
        return list;
    }

    private static Dictionary<uint, Rec> ByItem(int craftType)
    {
        if (byItemCache is not null && byItemJob == craftType) return byItemCache;
        byItemCache = new();
        foreach (var r in Recipes(craftType)) byItemCache.TryAdd(r.Item, r);
        byItemJob = craftType;
        return byItemCache;
    }

    private static readonly string[] Bad = { "Component", "Materials", "Dye", "Orchestrion", "Vintage", "Oddly Specific", "Deluxe", "Radical", "Fence", "Window", "Wall", "Flooring", "Pillar" };

    /// <summary>Самый простой рецепт уровня: доски/слитки и т.п., всё с рынка, мало ингредиентов.</summary>
    private static Rec? GrindRecipe(List<Rec> all, int level)
    {
        for (var l = level; l >= Math.Max(1, level - 3); l--)
        {
            var cands = all.Where(r => r.Level == l && r.AllMarket && !Bad.Any(b => r.Name.Contains(b))
                                       && !r.Ings.Any(i => i.Name.Contains("Dynamis") || i.Name.Contains("Oddly"))).ToList();
            if (cands.Count == 0) continue;
            return cands.OrderBy(r => r.Category is "Lumber" or "Metal" or "Cloth" or "Leather" or "Bone" or "Reagent" or "Ingredient" ? 0 : 1)
                .ThenBy(r => r.Ings.Where(i => !i.Name.Contains("Shard") && !i.Name.Contains("Crystal")).Sum(i => i.Amount)).First();
        }
        return null;
    }

    private static bool Completed(uint recipeId)
    {
        try { return QuestManager.IsRecipeComplete(recipeId); }
        catch { return false; }
    }

    private static readonly int[] Jobs = { 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };

    public void Draw()
    {
        Theme.Header("Прокачка ремесла и сбора", "Что делать на твоём уровне: классовый квест, первые крафты, чем добивать уровень и что купить с учётом инвентаря");
        if (!PlayerInfo.Ready) { Theme.NeedLogin("Зайди в игру персонажем: уровни профессий и инвентарь берутся из игры."); return; }

        var cj = Plugin.Data.GetExcelSheet<ClassJob>();
        // Выбор профессии значками
        Theme.Panel("lpjobs", () =>
        {
            foreach (var j in Jobs)
            {
                var row = cj.GetRow((uint)j);
                var lvl = Plugin.PlayerState.GetClassJobLevel(row);
                var pos = ImGui.GetCursorScreenPos();
                var size = 40 * Theme.S;
                if (ImGui.InvisibleButton($"##lpj{j}", new Vector2(size))) { job = j; levelOverride = 0; }
                var dl = ImGui.GetWindowDrawList();
                if (job == j) dl.AddRectFilled(pos - new Vector2(2 * Theme.S), pos + new Vector2(size + 2 * Theme.S), Theme.U32(Theme.Alpha(Theme.Gold, 0.3f)), 6f * Theme.S);
                Theme.GameIcon(62100u + (uint)j, pos, size, 6f);
                var t = lvl.ToString();
                dl.AddText(pos + new Vector2(size - ImGui.CalcTextSize(t).X - 2 * Theme.S, size - ImGui.GetTextLineHeight()), Theme.U32(Theme.Text), t);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{GuideData.Capitalize(row.Name.ExtractText())} · ур. {lvl}");
                ImGui.SameLine();
            }
            ImGui.NewLine();
        });

        var jobRow = cj.GetRow((uint)job);
        var realLevel = Plugin.PlayerState.GetClassJobLevel(jobRow);
        var level = levelOverride > 0 ? levelOverride : Math.Max(1, (int)realLevel);
        var jobName = GuideData.Capitalize(jobRow.Name.ExtractText());

        Theme.Panel("lpstate", () =>
        {
            Theme.LargeText(Theme.Gold, $"{jobName} · ур. {realLevel}");
            var exp = Plugin.PlayerState.GetClassJobExperience(jobRow);
            var need = Plugin.Data.GetExcelSheet<ParamGrow>().GetRowOrDefault((uint)Math.Max(1, (int)realLevel))?.ExpToNext ?? 0;
            if (realLevel == 0) ImGui.TextColored(Theme.Yellow, "Профессия ещё не открыта: возьми первый квест в её гильдии.");
            else if (need > 0) Theme.Progress($"Опыт: {exp:N0} / {need:N0} (осталось {need - exp:N0})", exp, need, Theme.Gold);
            ImGui.SetNextItemWidth(120 * Theme.S);
            var lo = level;
            if (ImGui.InputInt("Планировать для уровня##lplvl", ref lo)) levelOverride = Math.Clamp(lo, 1, 100);
            if (levelOverride > 0) { ImGui.SameLine(); if (ImGui.SmallButton("Мой уровень##lpreset")) levelOverride = 0; }
        });

        DrawClassQuest(jobRow, (int)realLevel);
        if (job >= 16) DrawGatherer(level);
        else DrawCrafter(level);
    }

    private static readonly Dictionary<uint, List<Quest>> questCache = new();

    private static void DrawClassQuest(ClassJob jobRow, int realLevel)
    {
        if (!questCache.TryGetValue(jobRow.RowId, out var quests))
            questCache[jobRow.RowId] = quests = Plugin.Data.GetExcelSheet<Quest>().Where(q => q.ClassJobRequired.RowId == jobRow.RowId && !q.Name.IsEmpty)
                .OrderBy(q => q.ClassJobLevel[0]).ToList();
        if (quests.Count == 0) return;
        var next = quests.FirstOrDefault(q => !PlayerInfo.IsQuestDone(q.RowId));
        Theme.Panel("lpquest", () =>
        {
            Theme.SubHeader("Классовый квест");
            if (next.RowId == 0) { ImGui.TextColored(Theme.Green, "Все классовые квесты выполнены ✓"); return; }
            var lvl = next.ClassJobLevel[0];
            var ready = realLevel >= lvl;
            ImGui.TextColored(ready ? Theme.Green : Theme.TextDim, $"{next.Name.ExtractText()} — ур. {lvl}");
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextDim, ready ? "можно брать сейчас" : $"откроется на {lvl} уровне");
            if (next.IssuerLocation.RowId != 0)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Флаг на NPC##lpqflag")) ItemSources.FlagLevel(next.IssuerLocation.RowId);
            }
            var done = quests.Count(q => PlayerInfo.IsQuestDone(q.RowId));
            ImGui.TextColored(Theme.Gray, $"Выполнено {done} из {quests.Count}. Предметы для сдачи — в Artisan → Premade Lists или во вкладке «Крафт».");
        });
    }

    private void DrawCrafter(int level)
    {
        var craftType = job - 8;
        if (!RecipesReady(craftType)) { Theme.Skeleton("lprec", 6, "Собираю рецепты профессии…"); return; }
        var all = Recipes(craftType);
        var byItem = ByItem(craftType);
        var firsts = all.Where(r => r.Level <= level && r.Level >= Math.Max(1, level - 4) && !Completed(r.Id)).ToList();
        var grind = GrindRecipe(all, level);

        Theme.Grid("lpcols", 2, 380f, col =>
        {
            if (col == 0)
                Theme.Panel("lpfirst", () =>
                {
                    Theme.SubHeader($"Первый крафт: {firsts.Count} рецептов (ур. {Math.Max(1, level - 4)}–{level})");
                    ImGui.TextColored(Theme.TextDim, "За первый крафт рецепта игра даёт бонус опыта. Ниже — рецепты, которые ты ещё не делал.");
                    if (firsts.Count == 0) ImGui.TextColored(Theme.Green, "Все рецепты этих уровней уже сделаны ✓");
                    foreach (var r in firsts.Take(40))
                    {
                        Theme.GameIcon(r.Icon, ImGui.GetCursorScreenPos(), 22 * Theme.S, 4f);
                        ImGui.Dummy(new Vector2(24 * Theme.S, 22 * Theme.S));
                        ImGui.SameLine();
                        if (ImGui.Selectable($"{r.Name}##lpf{r.Id}")) WikiNav.Open(WikiKind.Item, r.Item, r.Name);
                        ImGui.SameLine();
                        ImGui.TextColored(Theme.Gray, $"ур. {r.Level}");
                    }
                    if (firsts.Count > 40) ImGui.TextColored(Theme.Gray, $"…и ещё {firsts.Count - 40}");
                });
            else
                Theme.Panel("lpgrind", () =>
                {
                    Theme.SubHeader("Чем добивать уровень");
                    if (grind is null) { ImGui.TextColored(Theme.TextDim, "Не нашлось простого рецепта с материалами с рынка."); return; }
                    Theme.GameIcon(grind.Icon, ImGui.GetCursorScreenPos(), 40 * Theme.S, 6f);
                    ImGui.Dummy(new Vector2(44 * Theme.S, 40 * Theme.S));
                    ImGui.SameLine();
                    ImGui.BeginGroup();
                    if (ImGui.Selectable($"{grind.Name}##lpg")) WikiNav.Open(WikiKind.Item, grind.Item, grind.Name);
                    ImGui.TextColored(Theme.TextDim, $"ур. {grind.Level} · " + string.Join(", ", grind.Ings.Select(i => $"{i.Amount}× {i.Name}")));
                    ImGui.EndGroup();
                    ImGui.SetNextItemWidth(120 * Theme.S);
                    ImGui.InputInt("Сколько раз##lpgc", ref grindCount);
                    grindCount = Math.Clamp(grindCount, 0, 999);
                    ImGui.TextColored(Theme.Gray, "Делай в HQ — опыта больше. Сколько нужно до апа, точно не посчитать: игра не хранит формулу.");
                });
        });

        // ── Список покупок с учётом инвентаря
        var need = new Dictionary<uint, (string Name, int Amount)>();
        void Add(Rec r, int crafts, int depth)
        {
            foreach (var ing in r.Ings)
            {
                var amt = ing.Amount * crafts;
                // Заготовки этой же профессии своего уровня — раскладываем до сырья
                if (depth < 4 && byItem.TryGetValue(ing.Item, out var sub) && sub.Level <= r.Level && sub.Item != r.Item)
                {
                    Add(sub, (int)Math.Ceiling(amt / (double)sub.Yield), depth + 1);
                    continue;
                }
                need[ing.Item] = need.TryGetValue(ing.Item, out var cur) ? (cur.Name, cur.Amount + amt) : (ing.Name, amt);
            }
        }
        if (includeFirst) foreach (var r in firsts) Add(r, 1, 0);
        if (grind is not null && grindCount > 0) Add(grind, grindCount, 0);

        var region = MarketService.CurrentRegion(false);
        if (region is not null && need.Count > 0) market.Request(region, need.Keys);

        Theme.Panel("lpshop", () =>
        {
            Theme.SubHeader("Что купить");
            ImGui.Checkbox("Первые крафты##lpinc", ref includeFirst);
            ImGui.SameLine();
            ImGui.Checkbox("Учитывать ретейнеров##lpret", ref includeRetainers);
            ImGui.SameLine();
            ImGui.TextColored(Theme.Gray, "Заготовки своей профессии разложены до сырья (их скрафтишь сам).");
            if (need.Count == 0) { ImGui.TextColored(Theme.TextDim, "Ничего не нужно."); return; }

            long total = 0;
            var missing = 0;
            if (!ImGui.BeginTable("##lpshopt", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp)) return;
            ImGui.TableSetupColumn("Материал", ImGuiTableColumnFlags.None, 2.2f);
            ImGui.TableSetupColumn("Нужно", ImGuiTableColumnFlags.None, 0.6f);
            ImGui.TableSetupColumn("В сумках", ImGuiTableColumnFlags.None, 0.7f);
            ImGui.TableSetupColumn("У ретейнеров", ImGuiTableColumnFlags.None, 0.8f);
            ImGui.TableSetupColumn("Купить", ImGuiTableColumnFlags.None, 0.6f);
            ImGui.TableSetupColumn("Цена (за шт.)", ImGuiTableColumnFlags.None, 1.1f);
            ImGui.TableHeadersRow();
            foreach (var (id, (name, amount)) in need.OrderBy(kv => kv.Value.Name.Contains("Shard") || kv.Value.Name.Contains("Crystal") ? 1 : 0).ThenByDescending(kv => kv.Value.Amount))
            {
                var bag = PlayerInfo.ItemCount(id);
                var ret = includeRetainers ? RetainerTracker.CountOf(id).Total : 0;
                var buy = Math.Max(0, amount - bag - ret);
                var price = region is null ? null : market.Get(region, id);
                if (buy > 0) missing++;
                if (price is not null) total += price.Price * buy;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.Selectable($"{name}##lps{id}")) WikiNav.Open(WikiKind.Item, id, name);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(amount.ToString());
                ImGui.TableNextColumn();
                ImGui.TextColored(bag > 0 ? Theme.Green : Theme.Gray, bag.ToString());
                ImGui.TableNextColumn();
                ImGui.TextColored(ret > 0 ? Theme.Green : Theme.Gray, includeRetainers ? ret.ToString() : "—");
                ImGui.TableNextColumn();
                ImGui.TextColored(buy > 0 ? Theme.Yellow : Theme.Green, buy > 0 ? buy.ToString() : "✓");
                ImGui.TableNextColumn();
                if (price is not null) ImGui.TextColored(Theme.TextDim, $"{price.Price:N0} · {price.World}");
                else ImGui.TextColored(Theme.Gray, region is not null && market.IsLoading(region, id) ? "…" : "—");
            }
            ImGui.EndTable();
            ImGui.TextColored(missing == 0 ? Theme.Green : Theme.Text,
                missing == 0 ? "Всё уже есть — можно крафтить ✓" : $"Докупить позиций: {missing}. Примерно: {total:N0} гил (по самым дешёвым лотам дата-центра).");
            if (includeRetainers && !RetainerTracker.All.Any())
                ImGui.TextColored(Theme.Gray, "Чтобы учитывать ретейнеров, открой каждого у колокольчика — плагин запомнит их сумки.");
        });
    }

    private void DrawGatherer(int level)
    {
        if (job == 18)
        {
            Theme.Panel("lpfish", () =>
            {
                Theme.SubHeader("Рыбалка");
                ImGui.TextColored(Theme.TextDim, "Быстрее всего рыбака качает рыбалка в океане (вкладка «Океан»), а также доставки и левы. Места ловли — в вики → Рыбалка.");
            });
            return;
        }
        var types = job == 16 ? new[] { "Mining", "Quarrying" } : new[] { "Logging", "Harvesting" };
        var lo = Math.Max(1, level - 4);
        var spots = GatherIndex.All
            .SelectMany(kv => kv.Value.Where(sp => !sp.Timed && sp.Level >= lo && sp.Level <= level && types.Contains(sp.Type)).Select(sp => (Item: kv.Key, Spot: sp)))
            .GroupBy(x => (x.Spot.Zone, x.Spot.Place, x.Spot.Level))
            .OrderByDescending(g => g.Key.Level).ThenBy(g => g.Key.Zone).Take(20).ToList();
        var items = Plugin.Data.GetExcelSheet<Item>();
        Theme.Panel("lpgath", () =>
        {
            Theme.SubHeader($"Точки сбора уровней {lo}–{level}");
            ImGui.TextColored(Theme.TextDim, "Собирай на точках своего уровня — за первый сбор нового предмета тоже дают бонус опыта. Нажми «Флаг» — метка на карте.");
            if (spots.Count == 0) { ImGui.TextColored(Theme.TextDim, "Точек этого уровня не нашлось."); return; }
            var n = 0;
            foreach (var g in spots)
            {
                var sp = g.First().Spot;
                ImGui.TextColored(Theme.Gold, $"ур. {g.Key.Level}");
                ImGui.SameLine();
                ImGui.TextUnformatted($"{g.Key.Zone} — {g.Key.Place} ({sp.X:0.0}, {sp.Y:0.0})");
                ImGui.SameLine();
                if (ImGui.SmallButton($"Флаг##lpgf{n++}")) GatherIndex.Flag(sp);
                ImGui.TextColored(Theme.TextDim, "   " + string.Join(", ", g.Select(x => items.GetRowOrDefault(x.Item)?.Name.ExtractText() ?? "").Where(t => t.Length > 0).Distinct().Take(6)));
            }
        });
    }
}
