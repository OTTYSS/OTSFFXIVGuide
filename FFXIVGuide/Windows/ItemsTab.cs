using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Utility;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

internal sealed class ItemsTab
{
    private sealed record ItemRow(uint Id, string Name, uint Icon, uint ItemLevel, string Category, uint CategoryId, uint EquipLevel, uint JobCategory, bool Marketable);

    /// <summary>Категория из игры (ItemUICategory) с группой для списка.</summary>
    private sealed record CategoryInfo(uint Id, string Name, int Major, int Minor);

    private static readonly string[] MajorNames = { "Прочее", "Оружие", "Инструменты", "Броня", "Аксессуары", "Еда и зелья", "Материалы", "Прочее" };
    private static readonly string[] SortNames = { "Сначала точные совпадения", "По названию", "iLvl: сначала высокий", "iLvl: сначала низкий", "Уровень: сначала высокий", "Уровень: сначала низкий" };

    private List<CategoryInfo>? categories;
    private uint categoryId;
    private string categorySearch = "";
    private int sortMode;
    private bool onlyMyJob, onlyOwned, onlyMarketable;
    private DateTime lastFilter;

    private const int MaxRows = 500;
    private const int MaxCategoryRows = 5000;

    private List<ItemRow>? all;
    private List<ItemRow> results = new();
    private string search = "";
    private string lastSearch = "\0";
    private ItemRow? selected;
    private readonly GuideData data;

    private uint pendingSelect;

    private readonly MarketService market;

    public ItemsTab(GuideData data, MarketService market)
    {
        this.data = data;
        this.market = market;
    }

    /// <summary>Открыть предмет из другого раздела.</summary>
    public void Show(uint id, string name)
    {
        search = name;
        pendingSelect = id;
    }

    public void Draw()
    {
        all ??= BuildIndex();

        Theme.Header("Предметы", "Поиск по всей базе предметов: статы, кто носит, сколько у тебя есть");
        Theme.Search("##isearch", categoryId != 0 ? "Название (можно не вводить — покажу всю категорию)…" : "Название предмета (минимум 2 буквы)…", ref search);
        DrawFilters();

        var term = search.Trim();
        // «Для моей профессии» и «есть у меня» зависят от игры — пересчитываем раз в пару секунд
        var jobId = PlayerInfo.Ready ? Plugin.PlayerState.ClassJob.RowId : 0;
        var key = $"{search}|{categoryId}|{sortMode}|{onlyMyJob}|{onlyOwned}|{onlyMarketable}|{jobId}";
        if (key != lastSearch || ((onlyOwned || onlyMyJob) && DateTime.Now - lastFilter > TimeSpan.FromSeconds(2)))
        {
            lastSearch = key;
            lastFilter = DateTime.Now;
            results = Filter(term, jobId);
        }

        if (pendingSelect != 0)
        {
            selected = results.FirstOrDefault(r => r.Id == pendingSelect) ?? all.FirstOrDefault(r => r.Id == pendingSelect);
            if (selected is not null && !results.Contains(selected)) results.Insert(0, selected);
            pendingSelect = 0;
        }

        var limit = categoryId != 0 ? MaxCategoryRows : MaxRows;
        ImGui.TextColored(Theme.TextDim,
            term.Length < 2 && categoryId == 0 ? "Начни вводить название или выбери категорию" :
            results.Count >= limit ? $"Показаны первые {limit}, уточни запрос" : $"Найдено: {results.Count}");

        var avail = ImGui.GetContentRegionAvail();
        var rowH = 42 * Theme.S;
        if (ImGui.BeginChild("##ilist", new Vector2(Theme.SplitWidth("items", avail.X, 0.5f), 0)))
        {
            Theme.VirtualList(results.Count, rowH, i =>
            {
                var it = results[i];
                var owned = PlayerInfo.ItemCount(it.Id);
                var sub = (it.ItemLevel > 1 ? $"iLvl {it.ItemLevel} · " : "") + it.Category;
                if (Theme.Card($"##i{it.Id}", it.Icon, it.Name, sub, owned > 0 ? $"×{owned}" : null, Theme.Green, selected == it, rowH))
                    WikiNav.Open(WikiKind.Item, it.Id, it.Name);
            });
        }
        ImGui.EndChild();

        Theme.Splitter("items", avail.X, 0.5f);
        if (ImGui.BeginChild("##idetails", Vector2.Zero))
        {
            if (selected is null)
            {
                Theme.Panel("iph", () =>
                {
                    Theme.FaIcon(FontAwesomeIcon.Gem, Theme.GoldDim);
                    ImGui.TextColored(Theme.TextDim, "Найди предмет и выбери его слева. Где достать и цены — кнопками на Garland Tools, Teamcraft и Universalis.");
                });
            }
            else
            {
                InventoryHighlight.Request(selected.Id);
                WikiNav.Visit(new WikiLink(WikiKind.Item, selected.Id, selected.Name));
                DrawDetails(selected);
            }
        }
        ImGui.EndChild();
    }

    private List<ItemRow> Filter(string term, uint jobId)
    {
        if (term.Length < 2 && categoryId == 0) return new List<ItemRow>();
        var job = onlyMyJob && jobId != 0 ? GuideData.JobAbbrEnglish(jobId) : "";
        IEnumerable<ItemRow> q = all!;
        if (categoryId != 0) q = q.Where(i => i.CategoryId == categoryId);
        if (term.Length >= 2) q = q.Where(i => NameTranslations.Matches(i.Name, term));
        if (onlyMarketable) q = q.Where(i => i.Marketable);
        if (job.Length > 0) q = q.Where(i => i.JobCategory != 0 && data.JobInCategory(i.JobCategory, job));
        if (onlyOwned) q = q.Where(i => PlayerInfo.ItemCount(i.Id) > 0);
        q = sortMode switch
        {
            1 => q.OrderBy(i => NameTranslations.Show(i.Name)),
            2 => q.OrderByDescending(i => i.ItemLevel).ThenBy(i => i.Name),
            3 => q.OrderBy(i => i.ItemLevel).ThenBy(i => i.Name),
            4 => q.OrderByDescending(i => i.EquipLevel).ThenByDescending(i => i.ItemLevel),
            5 => q.OrderBy(i => i.EquipLevel).ThenBy(i => i.ItemLevel),
            _ => term.Length >= 2
                ? q.OrderByDescending(i => i.Name.Equals(term, StringComparison.OrdinalIgnoreCase)).ThenBy(i => i.Name.Length)
                : q.OrderByDescending(i => i.ItemLevel).ThenBy(i => i.Name),
        };
        return q.Take(categoryId != 0 ? MaxCategoryRows : MaxRows).ToList();
    }

    private void DrawFilters()
    {
        categories ??= Plugin.Data.GetExcelSheet<ItemUICategory>()
            .Where(c => !c.Name.IsEmpty)
            .Select(c => new CategoryInfo(c.RowId, c.Name.ExtractText(), c.OrderMajor, c.OrderMinor))
            .OrderBy(c => c.Major is >= 1 and <= 7 ? c.Major : 99).ThenBy(c => c.Minor).ToList();
        var cur = categories.FirstOrDefault(c => c.Id == categoryId);
        ImGui.SetNextItemWidth(260 * Theme.S);
        if (ImGui.BeginCombo("##icat", cur is null ? "Все категории" : NameTranslations.Show(cur.Name), ImGuiComboFlags.HeightLarge))
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##icatsearch", "Найти категорию…", ref categorySearch, 40);
            if (ImGui.Selectable("Все категории", categoryId == 0)) categoryId = 0;
            var lastMajor = -1;
            foreach (var c in categories)
            {
                if (categorySearch.Length > 0 && !NameTranslations.Matches(c.Name, categorySearch)) continue;
                var major = c.Major is >= 1 and <= 7 ? c.Major : 0;
                if (major != lastMajor && categorySearch.Length == 0)
                {
                    lastMajor = major;
                    ImGui.TextColored(Theme.Gold, MajorNames[major]);
                }
                if (ImGui.Selectable($"   {NameTranslations.Show(c.Name)}##icat{c.Id}", categoryId == c.Id)) categoryId = c.Id;
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(230 * Theme.S);
        if (ImGui.BeginCombo("##isort", SortNames[sortMode]))
        {
            for (var i = 0; i < SortNames.Length; i++)
                if (ImGui.Selectable(SortNames[i], sortMode == i)) sortMode = i;
            ImGui.EndCombo();
        }
        ImGui.Checkbox("Для моей профессии", ref onlyMyJob);
        ImGui.SameLine();
        ImGui.Checkbox("Есть у меня", ref onlyOwned);
        ImGui.SameLine();
        ImGui.Checkbox("Продаётся на рынке", ref onlyMarketable);
    }

    private static List<ItemRow> BuildIndex()
    {
        var list = new List<ItemRow>();
        foreach (var item in Plugin.Data.GetExcelSheet<Item>())
        {
            var name = item.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;
            list.Add(new ItemRow(
                item.RowId,
                name,
                item.Icon,
                item.LevelItem.RowId,
                item.ItemUICategory.ValueNullable?.Name.ExtractText() ?? "",
                item.ItemUICategory.RowId,
                item.LevelEquip,
                item.ClassJobCategory.RowId,
                item.ItemSearchCategory.RowId != 0 && !item.IsUntradable));
        }
        return list;
    }

    private static readonly Vector4[] RarityColors =
    {
        Theme.Text, Theme.Text, Theme.Rgb(0x8E, 0xE0, 0x7E), Theme.Rgb(0x6F, 0xA8, 0xFF), Theme.Rgb(0xB4, 0x8C, 0xFF), Theme.Rgb(0xF0, 0x9C, 0xD0), Theme.Rgb(0xFF, 0xD0, 0x6A), Theme.Text,
    };

    private void DrawDetails(ItemRow row)
    {
        var item = Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(row.Id);
        if (item is null)
            return;
        var it = item.Value;

        Theme.Panel("imain", () =>
        {
            var pos = ImGui.GetCursorScreenPos();
            Theme.GameIcon(row.Icon, pos, 52 * Theme.S, 6f);
            ImGui.SetCursorScreenPos(pos + new Vector2(62 * Theme.S, 2 * Theme.S));
            ImGui.BeginGroup();
            Theme.LargeText(RarityColors[Math.Min(it.Rarity, (byte)7)], row.Name);
            ImGui.TextColored(Theme.TextDim, row.Category);
            ImGui.EndGroup();
            ImGui.SetCursorScreenPos(new Vector2(pos.X, Math.Max(ImGui.GetCursorScreenPos().Y, pos.Y + 58 * Theme.S)));

            if (it.LevelItem.RowId > 1) { Theme.InlineBadge($"iLvl {it.LevelItem.RowId}", Theme.Gold); ImGui.SameLine(); }
            if (it.LevelEquip > 1) { Theme.InlineBadge($"с {it.LevelEquip} ур.", Theme.TextDim); ImGui.SameLine(); }
            if (it.MateriaSlotCount > 0) { Theme.InlineBadge($"материя ×{it.MateriaSlotCount}", Theme.Blue); ImGui.SameLine(); }
            Theme.InlineBadge(it.IsUntradable ? "не продаётся" : "рынок", it.IsUntradable ? Theme.Gray : Theme.Green);
            WikiNav.FavoriteButton(new WikiLink(WikiKind.Item, row.Id, row.Name));

            if (PlayerInfo.Ready)
            {
                var count = PlayerInfo.ItemCount(row.Id);
                ImGui.Dummy(new Vector2(0, 2 * Theme.S));
                Theme.FaIcon(FontAwesomeIcon.CheckCircle, count > 0 ? Theme.Green : Theme.Gray);
                ImGui.SameLine();
                var (retCount, retWhere) = RetainerTracker.CountOf(row.Id);
            if (retCount > 0)
            {
                Theme.FaIcon(FontAwesomeIcon.Box, Theme.Blue);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Blue, $"У ретейнеров: {retWhere}");
            }
            ImGui.TextColored(count > 0 ? Theme.Green : Theme.TextDim,
                    count > 0 ? $"У тебя: {count} (сумки, броне-шкаф, надето)" : "У тебя нет");
            }
        });

        var jobs = it.ClassJobCategory.ValueNullable?.Name.ExtractText();
        var stats = new List<(string Name, int Value)>();
        for (var i = 0; i < it.BaseParam.Count; i++)
        {
            var p = it.BaseParam[i].RowId;
            if (p == 0 || it.BaseParamValue[i] == 0) continue;
            var name = GuideData.StatNames.TryGetValue(p, out var n) ? n : it.BaseParam[i].ValueNullable?.Name.ExtractText() ?? $"#{p}";
            stats.Add((name, it.BaseParamValue[i]));
        }

        if (stats.Count > 0 || !string.IsNullOrEmpty(jobs))
        {
            Theme.Panel("istats", () =>
            {
                if (!string.IsNullOrEmpty(jobs))
                {
                    Theme.SubHeader("Кто может носить");
                    ImGui.TextColored(Theme.Text, jobs);
                }

                DrawCompare(it, stats);
                if (stats.Count == 0) return;
                Theme.SubHeader("Характеристики");
                if (ImGui.BeginTable("##st", 2))
                {
                    foreach (var (name, value) in stats)
                    {
                        ImGui.TableNextColumn();
                        ImGui.TextColored(Theme.TextDim, name);
                        ImGui.SameLine();
                        ImGui.TextColored(Theme.Green, $"+{value}");
                    }
                    ImGui.EndTable();
                }
            });
        }

        var desc = it.Description.ExtractText();
        if (!string.IsNullOrWhiteSpace(desc))
            Theme.Panel("idesc", () => ImGui.TextColored(Theme.TextDim, desc));

        Theme.Panel("ilinks", () =>
        {
            Theme.SubHeader("Где достать");
            foreach (var src in data.Sources.Get(row.Id, it.ItemSearchCategory.RowId != 0 && !it.IsUntradable))
            {
                var icon = src.Kind switch
                {
                    SourceKind.GilShop => FontAwesomeIcon.Home,
                    SourceKind.Exchange => FontAwesomeIcon.Gem,
                    SourceKind.Craft => FontAwesomeIcon.Star,
                    SourceKind.Market => FontAwesomeIcon.Search,
                    SourceKind.Gather => FontAwesomeIcon.MapMarkerAlt,
                    _ => FontAwesomeIcon.Skull,
                };
                Theme.FaIcon(icon, src.Easy ? Theme.Gold : Theme.Gray);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Text, src.Text);
                if (src.LevelId != 0)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Флаг##src{src.LevelId}{src.Text.GetHashCode()}"))
                        ItemSources.FlagLevel(src.LevelId);
                }
            }
            ImGui.Dummy(new Vector2(0, 2 * Theme.S));
            if (it.PriceLow > 0)
                ImGui.TextColored(Theme.TextDim, $"Продать NPC: {it.PriceLow:N0} гил");
            LinkButtons(row.Id);
        });

        DrawWiki(row.Id);
        DrawCraftTree(row.Id);
        if (it.ItemSearchCategory.RowId != 0 && !it.IsUntradable) DrawPriceHistory(row.Id);
    }

    // ───────────── Сравнение с надетым ─────────────

    private static readonly Dictionary<GearSlot, int[]> SlotIdx = new()
    {
        [GearSlot.MainHand] = new[] { 0 }, [GearSlot.OffHand] = new[] { 1 }, [GearSlot.Head] = new[] { 2 },
        [GearSlot.Body] = new[] { 3 }, [GearSlot.Hands] = new[] { 4 }, [GearSlot.Legs] = new[] { 6 },
        [GearSlot.Feet] = new[] { 7 }, [GearSlot.Ears] = new[] { 8 }, [GearSlot.Neck] = new[] { 9 },
        [GearSlot.Wrists] = new[] { 10 }, [GearSlot.Ring] = new[] { 11, 12 },
    };

    private static void DrawCompare(Item it, List<(string Name, int Value)> stats)
    {
        if (!PlayerInfo.Ready || it.EquipSlotCategory.ValueNullable is not { } cat || GuideData.SlotOf(cat) is not { } slot) return;
        var items = Plugin.Data.GetExcelSheet<Item>();
        // Для колец сравниваем с более слабым
        var worn = SlotIdx[slot].Select(PlayerInfo.EquippedItem).Where(id => id != 0)
            .Select(id => items.GetRowOrDefault(id)).Where(r => r is not null).Select(r => r!.Value)
            .OrderBy(r => r.LevelItem.RowId).FirstOrDefault();
        if (worn.RowId == 0 || worn.RowId == it.RowId) return;

        var wornStats = new Dictionary<string, int>();
        for (var i = 0; i < worn.BaseParam.Count; i++)
        {
            var p = worn.BaseParam[i].RowId;
            if (p == 0 || worn.BaseParamValue[i] == 0) continue;
            var name = GuideData.StatNames.TryGetValue(p, out var n) ? n : worn.BaseParam[i].ValueNullable?.Name.ExtractText() ?? $"#{p}";
            wornStats[name] = worn.BaseParamValue[i];
        }
        var all = stats.Select(s => s.Name).Concat(wornStats.Keys).Distinct().ToList();

        Theme.SubHeader("Сравнение с надетым");
        ImGui.TextColored(Theme.TextDim, $"Надето: {worn.Name.ExtractText()} (iLvl {worn.LevelItem.RowId})");
        var dIlvl = (int)it.LevelItem.RowId - (int)worn.LevelItem.RowId;
        ImGui.SameLine();
        ImGui.TextColored(dIlvl > 0 ? Theme.Green : dIlvl < 0 ? Theme.Red : Theme.Gray, dIlvl == 0 ? "iLvl тот же" : $"iLvl {(dIlvl > 0 ? "+" : "")}{dIlvl}");
        if (ImGui.BeginTable("##cmp", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Стат");
            ImGui.TableSetupColumn("Эта вещь");
            ImGui.TableSetupColumn("Надето");
            ImGui.TableSetupColumn("Разница");
            ImGui.TableHeadersRow();
            foreach (var name in all)
            {
                var a = stats.FirstOrDefault(s => s.Name == name).Value;
                var b = wornStats.GetValueOrDefault(name);
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextColored(Theme.TextDim, name);
                ImGui.TableNextColumn(); ImGui.TextColored(Theme.Text, a == 0 ? "—" : a.ToString());
                ImGui.TableNextColumn(); ImGui.TextColored(Theme.Text, b == 0 ? "—" : b.ToString());
                ImGui.TableNextColumn();
                var d = a - b;
                ImGui.TextColored(d > 0 ? Theme.Green : d < 0 ? Theme.Red : Theme.Gray, d == 0 ? "0" : $"{(d > 0 ? "+" : "")}{d}");
            }
            ImGui.EndTable();
        }
        ImGui.Dummy(new Vector2(0, 4 * Theme.S));
    }

    // ───────────── Дерево крафта ─────────────

    private static Recipe? RecipeFor(uint itemId)
    {
        var look = Plugin.Data.GetExcelSheet<RecipeLookup>().GetRowOrDefault(itemId);
        if (look is null) return null;
        var l = look.Value;
        foreach (var r in new[] { l.CRP, l.BSM, l.ARM, l.GSM, l.LTW, l.WVR, l.ALC, l.CUL })
            if (r.RowId != 0 && r.ValueNullable is { } rec) return rec;
        return null;
    }

    private static readonly string[] CraftJobs = { "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL" };

    private void CollectBase(uint itemId, double amount, Dictionary<uint, double> acc, int depth)
    {
        var rec = depth < 8 ? RecipeFor(itemId) : null;
        if (rec is null || itemId < 20)   // кристаллы и осколки не раскрываем
        {
            acc[itemId] = acc.GetValueOrDefault(itemId) + amount;
            return;
        }
        var r = rec.Value;
        var crafts = amount / Math.Max(1, (int)r.AmountResult);
        for (var i = 0; i < r.Ingredient.Count; i++)
        {
            var ing = r.Ingredient[i].RowId;
            var n = r.AmountIngredient[i];
            if (ing == 0 || n == 0) continue;
            CollectBase(ing, Math.Ceiling(crafts) * n, acc, depth + 1);
        }
    }

    private void DrawTree(uint itemId, int amount, int depth, string path)
    {
        var rec = depth < 8 ? RecipeFor(itemId) : null;
        var (name, icon, _, _) = WikiView.ItemInfo(itemId);
        if (rec is null || itemId < 20)
        {
            ImGui.Indent(depth == 0 ? 0 : 4 * Theme.S);
            WikiView.ItemLine(itemId, amount, uid: path);
            ImGui.Unindent(depth == 0 ? 0 : 4 * Theme.S);
            return;
        }
        var r = rec.Value;
        var job = r.CraftType.RowId < CraftJobs.Length ? CraftJobs[r.CraftType.RowId] : "";
        var lvl = r.RecipeLevelTable.ValueNullable?.ClassJobLevel ?? 0;
        var open = ImGui.TreeNodeEx($"{(amount > 1 ? $"{amount}× " : "")}{name}  ·  {job} {lvl}##t{path}", depth == 0 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
        if (ImGui.IsItemHovered()) InventoryHighlight.Request(itemId);
        if (!open) return;
        var crafts = (int)Math.Ceiling(amount / (double)Math.Max(1, (int)r.AmountResult));
        for (var i = 0; i < r.Ingredient.Count; i++)
        {
            var ing = r.Ingredient[i].RowId;
            var n = r.AmountIngredient[i];
            if (ing == 0 || n == 0) continue;
            DrawTree(ing, crafts * n, depth + 1, $"{path}.{i}");
        }
        ImGui.TreePop();
    }

    private void DrawCraftTree(uint itemId)
    {
        if (RecipeFor(itemId) is null) return;
        Theme.Panel("ctree", () =>
        {
            Theme.LargeText(Theme.Gold, "Дерево крафта");
            ImGui.TextColored(Theme.TextDim, "Все промежуточные рецепты до сырья. Наведи на строку — предмет подсветится в сумке.");
            DrawTree(itemId, 1, 0, "r");

            var acc = new Dictionary<uint, double>();
            CollectBase(itemId, 1, acc, 0);
            var mats = acc.Where(kv => kv.Key >= 20).OrderByDescending(kv => kv.Value).ToList();
            var crystals = acc.Where(kv => kv.Key < 20).ToList();
            ImGui.Dummy(new Vector2(0, 4 * Theme.S));
            Theme.SubHeader("Итого сырья на 1 шт.");
            var region = MarketService.CurrentRegion(true);
            if (region is not null) market.Request(region, mats.Select(m => m.Key));
            long total = 0;
            var unknown = 0;
            foreach (var (id, n) in mats)
            {
                var have = PlayerInfo.ItemCount(id);
                var p = region is null ? null : market.Get(region, id);
                if (p is not null) total += (long)(p.Price * n); else unknown++;
                var extra = (have > 0 ? $"есть {have}" : "") + (p is not null ? $"{(have > 0 ? " · " : "")}~{p.Price * n:N0} гил" : "");
                WikiView.ItemLine(id, (int)n, extra.Length > 0 ? extra : null, "base");
            }
            if (crystals.Count > 0)
                ImGui.TextColored(Theme.TextDim, "Кристаллы: " + string.Join(", ", crystals.Select(c => $"{c.Value}× {WikiView.ItemInfo(c.Key).Name}")));
            if (region is not null && total > 0)
                ImGui.TextColored(Theme.Gold, $"Сырьё на рынке твоего мира: ~{total:N0} гил" + (unknown > 0 ? $" (без цены: {unknown})" : ""));

            ImGui.Dummy(new Vector2(0, 4 * Theme.S));
            ImGui.SetNextItemWidth(110 * Theme.S);
            ImGui.InputInt("шт.##shopqty", ref shopQty);
            shopQty = Math.Clamp(shopQty, 1, 999);
            ImGui.SameLine();
            if (Theme.IconButton("toshop", FontAwesomeIcon.CartPlus, "В список покупок"))
            {
                var need = new Dictionary<uint, double>();
                CollectBase(itemId, shopQty, need, 0);
                foreach (var (id, n) in need.Where(kv => kv.Key >= 20)) ShoppingTab.Add(id, (int)Math.Ceiling(n));
                shopAdded = DateTime.Now;
            }
            Theme.Tooltip("Добавит всё сырьё на это количество в «Список покупок» (раздел «Экономика»)");
            if (DateTime.Now - shopAdded < TimeSpan.FromSeconds(3)) { ImGui.SameLine(); ImGui.TextColored(Theme.Green, "Добавлено ✓"); }
        });
    }

    private int shopQty = 1;
    private DateTime shopAdded;

    // ───────────── История цен ─────────────

    private void DrawPriceHistory(uint itemId)
    {
        var region = MarketService.CurrentRegion(false);
        if (region is null) return;
        Theme.Panel("phist", () =>
        {
            Theme.SubHeader($"Цена за 4 недели · {region}");
            var h = market.History(region, itemId);
            if (h is null) { ImGui.TextColored(Theme.TextDim, "Загружаю историю продаж с Universalis…"); return; }
            if (h.Count < 2) { ImGui.TextColored(Theme.TextDim, "Мало продаж, чтобы нарисовать график."); return; }

            var S = Theme.S;
            var pos = ImGui.GetCursorScreenPos() + new Vector2(0, 4 * S);
            var size = new Vector2(Math.Min(ImGui.GetContentRegionAvail().X, Theme.MaxBarWidth), 130 * S);
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(pos, pos + size, Theme.U32(Theme.CardBg), 8f * S);
            var min = h.Min(p => p.Avg);
            var max = h.Max(p => p.Avg);
            if (max - min < 1) { max += 1; min = Math.Max(0, min - 1); }
            var pad = new Vector2(64 * S, 14 * S);
            var area = size - pad * 2;
            var t0 = h[0].Day;
            var span = Math.Max(1, (h[^1].Day - t0).TotalDays);
            Vector2 P(int i) => pos + pad + new Vector2((float)(area.X * (h[i].Day - t0).TotalDays / span), area.Y * (float)(1 - (h[i].Avg - min) / (max - min)));
            for (var g = 0; g <= 2; g++)
            {
                var y = pos.Y + pad.Y + area.Y * g / 2;
                dl.AddLine(new Vector2(pos.X + pad.X, y), new Vector2(pos.X + size.X - pad.X, y), Theme.U32(Theme.Alpha(Theme.Border, 0.6f)));
                var v = max - (max - min) * g / 2;
                dl.AddText(new Vector2(pos.X + 6 * S, y - ImGui.GetTextLineHeight() / 2), Theme.U32(Theme.Gray), v.ToString("N0"));
            }
            for (var i = 1; i < h.Count; i++) dl.AddLine(P(i - 1), P(i), Theme.U32(Theme.Gold), 2.5f * S);
            var hoverIdx = -1;
            for (var i = 0; i < h.Count; i++)
            {
                var pt = P(i);
                dl.AddCircleFilled(pt, 3f * S, Theme.U32(Theme.Gold));
                if (Vector2.Distance(ImGui.GetMousePos(), pt) < 8 * S) hoverIdx = i;
            }
            ImGui.Dummy(size + new Vector2(0, 8 * S));
            if (hoverIdx >= 0)
                ImGui.SetTooltip($"{h[hoverIdx].Day:dd.MM}: в среднем {h[hoverIdx].Avg:N0} гил, продано {h[hoverIdx].Sold}");
            var last = h[^1].Avg;
            var first = h[0].Avg;
            var change = (last - first) / Math.Max(1, first) * 100;
            ImGui.TextColored(Theme.TextDim, $"Сейчас ~{last:N0} гил · за период {(change >= 0 ? "+" : "")}{change:0}% · продано {h.Sum(p => p.Sold):N0} шт.");
        });
    }

    private static readonly string[] NodeTypes = { "Добыча (MIN)", "Карьер (MIN)", "Рубка (BTN)", "Сбор трав (BTN)", "Рыбалка" };

    private static string JobName(uint id) =>
        Plugin.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(id)?.Abbreviation.ExtractText() ?? $"#{id}";

    /// <summary>Вики-блок: все источники и где используется (Garland Tools).</summary>
    private void DrawWiki(uint id)
    {
        var w = WikiService.Instance.Item(id);
        if (w is null)
        {
            Theme.Panel("wload", () =>
            {
                Theme.SubHeader("Все источники");
                WikiView.Status(null, "item", id);
            });
            return;
        }

        if (w.FishSpots.Count > 0)
            Theme.Panel("wfish", () =>
            {
                Theme.LargeText(Theme.Gold, "Как поймать");
                if (w.FishGuide.Length > 0) ImGui.TextColored(Theme.TextDim, w.FishGuide);
                var i = 0;
                foreach (var s in w.FishSpots.Take(expanded.Contains("fish") ? 100 : 4))
                {
                    ImGui.Dummy(new Vector2(0, 2 * Theme.S));
                    Theme.FaIcon(FontAwesomeIcon.Fish, Theme.Blue);
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.Text, s.Spot);
                    var zone = WikiService.ZoneName(s.Zone);
                    if (zone.Length > 0) { ImGui.SameLine(); ImGui.TextColored(Theme.TextDim, zone); }
                    if (s.HasCoords)
                    {
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"Флаг##fs{i}")) WikiService.Flag(s.Zone, s.X, s.Y);
                    }
                    var info = new List<string>();
                    if (s.Hookset.Length > 0) info.Add(s.Hookset);
                    if (s.Tug.Length > 0) info.Add($"поклёвка: {s.Tug}");
                    if (s.Time.Length > 0) info.Add($"время: {s.Time}");
                    if (s.Weather.Length > 0) info.Add($"погода: {s.Weather}");
                    if (info.Count > 0) ImGui.TextColored(Theme.TextDim, string.Join(" · ", info));
                    FishWindow(s, i);
                    foreach (var chain in s.Baits.Take(2))
                    {
                        ImGui.TextColored(Theme.TextDim, chain.Count > 1 ? "Наживка (цепочка мучинга):" : "Наживка:");
                        foreach (var b in chain) WikiView.ItemLine(b, uid: $"bait{i}");
                    }
                    i++;
                }
                More("fish", w.FishSpots.Count, 4);
            });

        if (w.HasAnySource)
            Theme.Panel("wsrc", () =>
            {
                Theme.LargeText(Theme.Gold, "Все источники");

                if (w.Drops.Count > 0)
                {
                    Theme.SubHeader($"Дроп с мобов · {w.Drops.Count}");
                    foreach (var m in w.Drops.Take(expanded.Contains("drops") ? 200 : 8)) WikiView.MobLine(m, "wd");
                    More("drops", w.Drops.Count, 8);
                }

                if (w.Instances.Count > 0)
                {
                    Theme.SubHeader($"Данжи и бои · {w.Instances.Count}");
                    foreach (var r in w.Instances.Take(expanded.Contains("inst") ? 200 : 8))
                        WikiView.DutyLine(uint.TryParse(r.Id, out var iid) ? iid : 0, r.Name, r.Extra, "wi");
                    More("inst", w.Instances.Count, 8);
                }

                if (w.Vendors.Count > 0)
                {
                    Theme.SubHeader($"Торговцы за гил{(w.VendorPrice > 0 ? $" · {w.VendorPrice:N0} гил" : "")}");
                    foreach (var n in w.Vendors.Take(expanded.Contains("vend") ? 200 : 5)) WikiView.NpcLine(n, "wv");
                    More("vend", w.Vendors.Count, 5);
                }

                if (w.TradeShops.Count > 0)
                {
                    Theme.SubHeader("Обмен");
                    var i = 0;
                    foreach (var t in w.TradeShops.Take(expanded.Contains("trade") ? 100 : 4))
                    {
                        ImGui.TextColored(Theme.Text, t.Shop);
                        foreach (var p in t.Pay) WikiView.ItemLine(p.Item, p.Amount, "цена", $"tp{i}");
                        foreach (var n in t.Npcs.Take(2)) WikiView.NpcLine(n, $"tn{i}");
                        ImGui.Dummy(new Vector2(0, 3 * Theme.S));
                        i++;
                    }
                    More("trade", w.TradeShops.Count, 4);
                }

                if (w.Crafts.Count > 0)
                {
                    Theme.SubHeader("Крафт");
                    var i = 0;
                    foreach (var c in w.Crafts)
                    {
                        Theme.InlineBadge($"{JobName(c.Job)} {c.Level}{new string('★', c.Stars)}", Theme.Gold);
                        if (c.Yield > 1) { ImGui.SameLine(); ImGui.TextColored(Theme.TextDim, $"выход ×{c.Yield}"); }
                        foreach (var ing in c.Ingredients) WikiView.ItemLine(ing.Item, ing.Amount, uid: $"cr{i}");
                        i++;
                    }
                }

                if (w.Nodes.Count > 0)
                {
                    Theme.SubHeader("Сбор");
                    foreach (var n in w.Nodes)
                    {
                        Theme.FaIcon(FontAwesomeIcon.Leaf, Theme.Green);
                        ImGui.SameLine();
                        var type = n.Type >= 0 && n.Type < NodeTypes.Length ? NodeTypes[n.Type] : "Сбор";
                        ImGui.TextColored(Theme.Text, $"{n.Name} · ур. {n.Level} · {type}");
                        ImGui.SameLine();
                        ImGui.TextColored(Theme.TextDim, WikiService.ZoneName(n.Zone) + (n.Limited.Length > 0 ? $" · {n.Limited}" : ""));
                    }
                }

                if (w.FishingSpots.Count > 0)
                {
                    Theme.SubHeader("Рыбалка");
                    foreach (var f in w.FishingSpots) ImGui.TextColored(Theme.Text, "• " + f.Name);
                }

                if (w.Treasure.Count > 0)
                {
                    Theme.SubHeader("Из сундуков, карт и контейнеров");
                    foreach (var t in w.Treasure) WikiView.ItemLine(t, uid: "tr");
                }

                if (w.DesynthFrom.Count > 0)
                {
                    Theme.SubHeader("Разбором (desynth) из");
                    foreach (var t in w.DesynthFrom.Take(expanded.Contains("des") ? 200 : 6)) WikiView.ItemLine(t, uid: "ds");
                    More("des", w.DesynthFrom.Count, 6);
                }

                if (w.ReducedFrom.Count > 0)
                {
                    Theme.SubHeader("Аэтериальной редукцией из");
                    foreach (var t in w.ReducedFrom) WikiView.ItemLine(t, uid: "rd");
                }

                if (w.Quests.Count > 0)
                {
                    Theme.SubHeader($"Награда за квесты · {w.Quests.Count}");
                    foreach (var q in w.Quests.Take(expanded.Contains("q") ? 200 : 6))
                        ImGui.TextColored(Theme.Text, $"• {q.Name}" + (q.Extra.Length > 0 ? $"  ({q.Extra})" : ""));
                    More("q", w.Quests.Count, 6);
                }

                if (w.Ventures > 0)
                    ImGui.TextColored(Theme.TextDim, "• Можно добыть ретейнером (вылазки)");
            });

        var usedIn = w.IngredientOf.Count + w.TradeCurrency.Count + w.UsedInQuests.Count + w.Leves.Count;
        if (usedIn > 0 || w.Supply)
            Theme.Panel("wuse", () =>
            {
                Theme.LargeText(Theme.Gold, "Где используется");

                if (w.TradeCurrency.Count > 0)
                {
                    Theme.SubHeader($"Можно обменять на · {w.TradeCurrency.Count}");
                    var i = 0;
                    foreach (var t in w.TradeCurrency.Take(expanded.Contains("cur") ? 300 : 8))
                    {
                        var cost = t.Pay.FirstOrDefault(p => p.Item == id)?.Amount ?? 0;
                        foreach (var g in t.Get) WikiView.ItemLine(g.Item, g.Amount, cost > 0 ? $"за {cost} шт. · {t.Shop}" : t.Shop, $"tc{i}");
                        i++;
                    }
                    More("cur", w.TradeCurrency.Count, 8);
                }

                if (w.IngredientOf.Count > 0)
                {
                    Theme.SubHeader($"Ингредиент в рецептах · {w.IngredientOf.Count}");
                    foreach (var r in w.IngredientOf.Take(expanded.Contains("ing") ? 300 : 8)) WikiView.ItemLine(r.Item, 0, $"нужно {r.Amount}", "io");
                    More("ing", w.IngredientOf.Count, 8);
                }

                if (w.UsedInQuests.Count > 0)
                {
                    Theme.SubHeader("Нужен для квестов");
                    foreach (var q in w.UsedInQuests) ImGui.TextColored(Theme.Text, "• " + q.Name);
                }

                if (w.Leves.Count > 0)
                    ImGui.TextColored(Theme.TextDim, $"• Нужен в {w.Leves.Count} левах");
                if (w.Supply)
                    ImGui.TextColored(Theme.TextDim, "• Принимается в Grand Company (supply & provisioning)");
            });
    }

    private readonly Dictionary<string, (long Calc, (long Start, long End)? Win)> fishWin = new();

    /// <summary>Ближайшее окно для рыбы с условиями по времени/погоде (пересчёт раз в 30 с).</summary>
    private void FishWindow(WikiFishSpot s, int i)
    {
        var hasTime = s.StartHour != s.EndHour;
        var hasWeather = s.Weathers is { Count: > 0 };
        if (!hasTime && !hasWeather) return;
        var rate = EorzeaWeather.RateForPlace(s.Zone);
        if (rate == 0 && hasWeather) return;
        var key = $"{s.Spot}|{s.StartHour}|{s.EndHour}|{s.Weather}";
        if (!fishWin.TryGetValue(key, out var c) || EorzeaWeather.Now - c.Calc > 30)
        {
            var w = s.Weathers?.Select(EorzeaWeather.WeatherByName).Where(x => x != 0).ToList();
            var p = s.Previous?.Select(EorzeaWeather.WeatherByName).Where(x => x != 0).ToList();
            c = (EorzeaWeather.Now, EorzeaWeather.NextWindow(rate, w, p, hasTime ? s.StartHour : 0, hasTime ? s.EndHour : 24, maxDays: 30));
            fishWin[key] = c;
        }
        Theme.FaIcon(FontAwesomeIcon.Clock, Theme.Gold);
        ImGui.SameLine();
        if (c.Win is not { } win)
            ImGui.TextColored(Theme.Gray, "Окно не найдено в ближайшие 30 дней");
        else if (win.Start <= EorzeaWeather.Now)
            ImGui.TextColored(Theme.Green, $"Окно открыто сейчас! Закроется через {EorzeaWeather.Until(win.End)}");
        else
            ImGui.TextColored(Theme.Gold, $"Следующее окно через {EorzeaWeather.Until(win.Start)} (в {EorzeaWeather.Local(win.Start)}, длится {(win.End - win.Start) / 60} мин)");
    }

    private readonly HashSet<string> expanded = new();

    private void More(string key, int total, int shown)
    {
        if (total <= shown) return;
        var open = expanded.Contains(key);
        if (ImGui.SmallButton(open ? $"Свернуть##{key}" : $"Показать все ({total})##{key}"))
        {
            if (open) expanded.Remove(key); else expanded.Add(key);
        }
    }

    public static void LinkButtons(uint itemId)
    {
        if (Theme.IconButton($"g{itemId}", FontAwesomeIcon.ExternalLinkAlt, "Garland Tools"))
            Util.OpenLink($"https://www.garlandtools.org/db/#item/{itemId}");
        Theme.Tooltip("Дроп, торговцы, крафт, сбор");
        ImGui.SameLine();
        if (Theme.IconButton($"t{itemId}", FontAwesomeIcon.ExternalLinkAlt, "Teamcraft"))
            Util.OpenLink($"https://ffxivteamcraft.com/db/en/item/{itemId}");
        ImGui.SameLine();
        if (Theme.IconButton($"u{itemId}", FontAwesomeIcon.ExternalLinkAlt, "Цены"))
            Util.OpenLink($"https://universalis.app/market/{itemId}");
        Theme.Tooltip("Universalis: цены на рынке");
    }
}
