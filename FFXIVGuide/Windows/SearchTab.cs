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
/// Общий поиск: данжи, квесты, предметы. Для каждого результата сразу видно,
/// как его открыть или где взять; клик ведёт в подробности.
/// </summary>
internal sealed class SearchTab
{
    private sealed record ItemHit(uint Id, string Name, uint Icon, uint ItemLevel, string Category, bool Marketable);

    private const int PerGroup = 6;

    private readonly GuideData data;
    private readonly DutiesTab duties;
    private readonly QuestsTab quests;
    private readonly ItemsTab items;
    private readonly Action<MainTab> go;

    private List<ItemHit>? itemIndex;
    private string query = "";
    private string lastQuery = "\0";
    private bool focusInput;

    private List<DutyEntry> dutyHits = new();
    private List<QuestEntry> questHits = new();
    private List<ItemHit> itemHits = new();
    private int dutyTotal, questTotal, itemTotal;
    private readonly HashSet<string> expanded = new();

    public SearchTab(GuideData data, DutiesTab duties, QuestsTab quests, ItemsTab items, Action<MainTab> go)
    {
        this.go = go;
        this.data = data;
        this.duties = duties;
        this.quests = quests;
        this.items = items;
    }

    public BestiaryTab? Bestiary;

    /// <summary>Поиск по остальным разделам плагина (статусы, навыки, левы, S-ранги, семена…).</summary>
    public readonly List<Func<string, IEnumerable<SearchHit>>> Providers = new();
    private List<(string Group, List<SearchHit> Hits)> extraHits = new();
    private string extraFor = "\0";
    private DateTime extraAt;
    private List<WikiMobRow> mobHits = new();
    private int mobTotal;
    private int mobIndexCount = -1;

    public void SetQuery(string text, bool focus = true)
    {
        query = text;
        focusInput = focus;
    }

    public void Draw()
    {
        Theme.Header("Поиск", "Данж, квест или предмет — покажу, как открыть или где взять");

        if (focusInput)
        {
            ImGui.SetKeyboardFocusHere();
            focusInput = false;
        }
        Theme.Search("##gsearch", "Например: Sastasha, Ифрит, Augmented Scaevan, White Mage…", ref query);

        var term = query.Trim();
        // Перевод русского запроса пришёл — перезапустить поиск с английским вариантом
        if (query == lastQuery && HasCyrillic(query.Trim()) && appliedEnglish is null
            && Translator.Instance.GetEnglish(query.Trim()) is { } en)
        {
            appliedEnglish = en;
            Run(query.Trim());
        }

        // Индекс мобов догрузился — пересчитать
        if (term.Length >= 2 && WikiService.Instance.MobIndex() is { } mi && mi.Count != mobIndexCount)
        {
            mobIndexCount = mi.Count;
            lastQuery = "\0";
        }

        if (query != lastQuery)
        {
            lastQuery = query;
            expanded.Clear();
            appliedEnglish = null;
            if (HasCyrillic(term)) Translator.Instance.RequestEnglish(term);
            Run(term);
        }

        if (term.Length < 2)
        {
            Theme.Panel("shint", () =>
            {
                Theme.FaIcon(FontAwesomeIcon.Search, Theme.GoldDim);
                ImGui.TextColored(Theme.TextDim, "Введи хотя бы 2 буквы. Ищу по названиям данжей и боёв, квестов (и тому, что они открывают) и предметов.");
                ImGui.TextColored(Theme.TextDim, "Можно по-английски или по-русски (Састаша, Ифрит, Шива) — русский запрос переводится автоматически. Из чата: /fguide Sastasha");
            });
            return;
        }

        // Остальные разделы: пересчёт при смене запроса и раз в 3 с (данные могут догружаться)
        if (term != extraFor || DateTime.Now - extraAt > TimeSpan.FromSeconds(3))
        {
            extraFor = term;
            extraAt = DateTime.Now;
            var hits = new List<SearchHit>();
            foreach (var p in Providers)
            {
                try { hits.AddRange(p(term).Take(60)); }
                catch (Exception e) { Plugin.Log.Warning(e, "Поиск: ошибка в разделе"); }
            }
            extraHits = hits.GroupBy(h => h.Group).Select(g => (g.Key, g.OrderBy(h => h.Name.Length).ToList())).ToList();
        }
        var extraTotal = extraHits.Sum(g => g.Hits.Count);

        var pageHits = MainWindow.AllPages.Count(x => x.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Hub.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (dutyTotal + questTotal + itemTotal + mobTotal + pageHits + extraTotal == 0)
        {
            Theme.Panel("sempty", () => ImGui.TextColored(Theme.TextDim, "Ничего не нашлось. Проверь написание (названия на английском)."));
            return;
        }

        if (!ImGui.BeginChild("##sresults", Vector2.Zero))
        {
            ImGui.EndChild();
            return;
        }

        var rowH = 46 * Theme.S;

        // Разделы плагина: «гламур», «погода», «субмарины»…
        var pages = MainWindow.AllPages.Where(x => x.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Hub.Contains(term, StringComparison.OrdinalIgnoreCase)).Take(6).ToList();
        if (pages.Count > 0)
            Theme.Panel("rpages", () =>
            {
                Theme.SubHeader("Разделы");
                foreach (var (tab, icon, name, hub) in pages)
                {
                    Theme.FaIcon(icon, Theme.Gold);
                    ImGui.SameLine();
                    if (ImGui.Selectable($"{hub} → {name}##pg{tab}")) go(tab);
                }
            });

        if (dutyTotal > 0)
        {
            Theme.Panel("rduties", () =>
            {
                GroupHeader("Данжи и бои", dutyTotal, "d");
                foreach (var d in Take(dutyHits, "d"))
                {
                    var (badge, color) = DutiesTab.StatusBadge(d);
                    if (Theme.Card($"##rd{d.CfcId}", d.Icon, d.Name, DutyHow(d), badge, color, false, rowH))
                        duties.Show(d);
                }
            });
        }

        if (questTotal > 0)
        {
            Theme.Panel("rquests", () =>
            {
                GroupHeader("Квесты", questTotal, "q");
                foreach (var q in Take(questHits, "q"))
                {
                    var (badge, color) = QuestsTab.StateBadge(q);
                    var sub = $"Ур. {q.Level} · {q.Place}" + (q.Unlocks.Length > 0 ? $" · открывает {q.Unlocks}" : "");
                    if (Theme.Card($"##rq{q.Id}", q.Icon, q.Name, sub, badge, color, false, rowH))
                        quests.Show(q);
                }
            });
        }

        if (itemTotal > 0)
        {
            Theme.Panel("ritems", () =>
            {
                GroupHeader("Предметы", itemTotal, "i");
                foreach (var it in Take(itemHits, "i"))
                {
                    var owned = PlayerInfo.ItemCount(it.Id);
                    if (Theme.Card($"##ri{it.Id}", it.Icon, it.Name, ItemHow(it), owned > 0 ? $"есть ×{owned}" : null, Theme.Green, false, rowH))
                    {
                        items.Show(it.Id, it.Name);
                        go(MainTab.Items);
                    }
                }
            });
        }

        if (mobTotal > 0)
        {
            Theme.Panel("rmobs", () =>
            {
                GroupHeader("Мобы и боссы", mobTotal, "m");
                foreach (var m in Take(mobHits, "m")) WikiView.MobLine(m, "s");
            });
        }

        foreach (var (group, hits) in extraHits)
        {
            var key = "x" + group;
            Theme.Panel("rx" + group, () =>
            {
                GroupHeader(group, hits.Count, key);
                foreach (var h in Take(hits, key))
                    if (Theme.Card($"##rx{group}{h.Name}{h.Sub.GetHashCode()}", h.Icon, NameTranslations.Show(h.Name), h.Sub.Length > 160 ? h.Sub[..160] + "…" : h.Sub, null, Theme.Gold, false, rowH))
                        h.Open();
            });
        }

        ImGui.EndChild();
    }

    private void GroupHeader(string title, int total, string key)
    {
        Theme.SubHeader($"{title} · {total}");
        if (total <= PerGroup) return;
        var open = expanded.Contains(key);
        if (ImGui.SmallButton(open ? $"Свернуть##{key}" : $"Показать все ({Math.Min(total, 50)})##{key}"))
        {
            if (open) expanded.Remove(key);
            else expanded.Add(key);
        }
    }

    private IEnumerable<T> Take<T>(List<T> list, string key) =>
        list.Take(expanded.Contains(key) ? 50 : PerGroup);

    private string DutyHow(DutyEntry d)
    {
        var head = $"{d.Type} · ур. {d.Level}" + (d.ItemLevel > 0 ? $" · iLvl {d.ItemLevel}" : "");
        if (PlayerInfo.Ready && PlayerInfo.IsDutyUnlocked(d))
            return head + " · уже открыт";
        if (d.UnlockQuestId != 0 && data.Quests.TryGetValue(d.UnlockQuestId, out var q))
        {
            var next = PlayerInfo.Ready ? PlayerInfo.NextStepTowards(data, q.Id) : null;
            return next is not null && next.Id != q.Id
                ? $"{head} · открывает «{q.Name}», начать с «{next.Name}»"
                : $"{head} · открывает квест «{q.Name}» ({q.Place})";
        }
        return head + " · открывается по уровню или другим контентом";
    }

    private string ItemHow(ItemHit it)
    {
        var head = (it.ItemLevel > 1 ? $"iLvl {it.ItemLevel} · " : "") + it.Category;
        var src = data.Sources.Get(it.Id, it.Marketable).FirstOrDefault();
        return src is null ? head : $"{head} · {src.Text}";
    }

    private string? appliedEnglish;

    private static bool HasCyrillic(string s) => s.Any(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё');

    private static readonly Dictionary<char, string> Translit = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e", ['ж'] = "zh",
        ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o",
        ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "ts",
        ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sh", ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
    };

    /// <summary>Нормализация для сравнения: только буквы и цифры, нижний регистр.</summary>
    private static string Norm(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Варианты запроса: как есть, транслитом (Састаша → sastasha) и переводом Google.</summary>
    private List<string> Variants(string term)
    {
        var list = new List<string> { term };
        if (HasCyrillic(term))
        {
            list.Add(string.Concat(term.ToLowerInvariant().Select(c => Translit.TryGetValue(c, out var t) ? t : c.ToString())));
            if (appliedEnglish is not null) list.Add(appliedEnglish);
        }
        return list.Where(v => Norm(v).Length >= 2).Distinct().ToList();
    }

    private static bool Matches(string name, List<string> variants)
    {
        if (variants.Any(v => HasCyrillic(v) && NameTranslations.Matches(name, v))) return true;
        var n = Norm(name);
        return variants.Any(v => n.Contains(Norm(v)));
    }

    private void Run(string term)
    {
        dutyHits.Clear(); questHits.Clear(); itemHits.Clear();
        dutyTotal = questTotal = itemTotal = mobTotal = 0;
        mobHits.Clear();
        if (term.Length < 2) return;

        if (WikiService.Instance.MobIndex() is { } mobs)
        {
            var found = BestiaryTab.Find(mobs, term, appliedEnglish)
                .Where(m => NameTranslations.Matches(m.Name, term) || (appliedEnglish is not null && m.Name.Contains(appliedEnglish, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(m => m.Name + "|" + m.Zone).Select(g => g.First())
                .ToList();
            mobTotal = found.Count;
            mobHits = found.Take(50).ToList();
        }

        itemIndex ??= BuildItemIndex();
        var variants = Variants(term);

        static int Rank(string name, string t) =>
            name.Equals(t, StringComparison.OrdinalIgnoreCase) ? 0 :
            name.StartsWith(t, StringComparison.OrdinalIgnoreCase) ? 1 : 2;

        var d = data.Duties
            .Where(x => Matches(x.Name, variants))
            .OrderBy(x => Rank(x.Name, term)).ThenBy(x => x.Level)
            .ToList();
        dutyTotal = d.Count;
        dutyHits = d.Take(50).ToList();

        var q = data.Quests.Values
            .Where(x => Matches(x.Name, variants) || Matches(x.Unlocks, variants))
            .OrderBy(x => NameTranslations.Matches(x.Name, term) ? Rank(x.Name, term) : 3)
            .ThenByDescending(x => x.IsMsq)
            .ThenBy(x => x.Level)
            .ToList();
        questTotal = q.Count;
        questHits = q.Take(50).ToList();

        var i = itemIndex
            .Where(x => Matches(x.Name, variants))
            .OrderBy(x => Rank(x.Name, term)).ThenBy(x => x.Name.Length)
            .ToList();
        itemTotal = i.Count;
        itemHits = i.Take(50).ToList();
    }

    private static List<ItemHit> BuildItemIndex()
    {
        var list = new List<ItemHit>();
        foreach (var item in Plugin.Data.GetExcelSheet<Item>())
        {
            var name = item.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name)) continue;
            list.Add(new ItemHit(
                item.RowId, name, item.Icon, item.LevelItem.RowId,
                item.ItemUICategory.ValueNullable?.Name.ExtractText() ?? "",
                item.ItemSearchCategory.RowId != 0 && !item.IsUntradable));
        }
        return list;
    }
}
