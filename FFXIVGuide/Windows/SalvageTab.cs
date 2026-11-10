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
/// Desynthesis, Aetherial Reduction и сундуки: что получится из предмета и из чего достать нужный.
/// Данные — офлайн-база Teamcraft (Настройки → База данных).
/// </summary>
internal sealed class SalvageTab
{
    private static readonly string[] Sections = { "Desynthesis", "Aetherial Reduction", "Сундуки и коробки" };
    private int section;
    private string search = "";
    private int dbVersion = -1;
    private uint selected;

    // результат → источники (как в базе) и обратные: источник → результаты
    private Dictionary<uint, List<uint>> desynthFrom = new(), desynthTo = new();
    private Dictionary<uint, List<uint>> reduceFrom = new(), reduceTo = new();
    private Dictionary<uint, List<uint>> coffers = new(); // сундук → содержимое
    private List<uint> keys = new();
    private string keysFor = "";

    private void Build(OfflineIndex ix)
    {
        static Dictionary<uint, List<uint>> Invert(Dictionary<uint, List<uint>> m)
        {
            var r = new Dictionary<uint, List<uint>>();
            foreach (var (result, sources) in m)
                foreach (var s in sources)
                {
                    if (!r.TryGetValue(s, out var l)) r[s] = l = new List<uint>();
                    if (!l.Contains(result)) l.Add(result);
                }
            return r;
        }
        desynthFrom = ix.Desynth;
        desynthTo = Invert(ix.Desynth);
        reduceFrom = ix.Reduction;
        reduceTo = Invert(ix.Reduction);
        coffers = Invert(ix.Loot);
        keysFor = "";
    }

    private static bool Owned(uint item)
    {
        if (PlayerInfo.ItemCount(item) + RetainerTracker.CountOf(item).Total > 0) return true;
        var row = Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(item);
        return row is { } r && r.ItemAction.RowId != 0 && PlayerInfo.Ready && Plugin.Unlocks.IsItemUnlocked(r);
    }

    public void Draw()
    {
        Theme.Header("Разборка и сундуки", "Что выйдет из предмета при Desynthesis и Aetherial Reduction, что лежит в сундуках — и обратно");
        var ix = OfflineDb.Instance.Index;
        if (ix is null)
        {
            Theme.EmptyState(FontAwesomeIcon.Database, "Нужна офлайн-база", "Скачай базу в Настройки → База данных — там данные о разборке и сундуках.");
            return;
        }
        if (dbVersion != OfflineDb.Version) { dbVersion = OfflineDb.Version; Build(ix); }

        var prev = section;
        Theme.Chips("svsec", Sections, ref section);
        if (prev != section) { selected = 0; keysFor = ""; }
        ImGui.SetNextItemWidth(Math.Min(420 * Theme.S, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##svsearch", section == 2 ? "Название сундука или предмета внутри…" : "Название предмета…", ref search, 64);

        var (from, to) = section switch
        {
            0 => (desynthFrom, desynthTo),
            1 => (reduceFrom, reduceTo),
            _ => (new Dictionary<uint, List<uint>>(), coffers),
        };

        // Список: все предметы, у которых есть хоть одна связь
        var k = section + "|" + search;
        if (k != keysFor)
        {
            keysFor = k;
            var all = section == 2 ? coffers.Keys : to.Keys.Concat(from.Keys).Distinct();
            bool Match(uint id) => search.Length == 0 || WikiView.ItemInfo(id).Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                                   || NameTranslations.Show(WikiView.ItemInfo(id).Name).Contains(search, StringComparison.OrdinalIgnoreCase);
            keys = all.Where(id => WikiView.ItemInfo(id).Name.Length > 0)
                .Where(id => Match(id) || (section == 2 && search.Length >= 3 && coffers[id].Any(Match)))
                .OrderBy(id => WikiView.ItemInfo(id).Name).Take(2000).ToList();
        }

        var avail = ImGui.GetContentRegionAvail();
        var leftW = Math.Min(380 * Theme.S, avail.X * 0.45f);
        if (ImGui.BeginChild("##svl", new Vector2(leftW, 0)))
        {
            ImGui.TextColored(Theme.TextDim, $"Найдено: {keys.Count}");
            var rowH = 40 * Theme.S;
            Theme.VirtualList(keys.Count, rowH, i =>
            {
                var id = keys[i];
                var info = WikiView.ItemInfo(id);
                var sub = section == 2 ? $"внутри: {coffers[id].Count}"
                    : $"{(to.TryGetValue(id, out var t) ? $"даёт {t.Count}" : "")}{(to.ContainsKey(id) && from.ContainsKey(id) ? " · " : "")}{(from.TryGetValue(id, out var f) ? $"из {f.Count} предм." : "")}";
                if (Theme.Card($"##sv{section}_{id}", info.Icon, NameTranslations.Show(info.Name), sub, Owned(id) ? "есть" : null, Theme.Green, selected == id, rowH))
                    selected = id;
            });
        }
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##svr", Vector2.Zero)) DrawDetail(from, to);
        ImGui.EndChild();
    }

    private void DrawDetail(Dictionary<uint, List<uint>> from, Dictionary<uint, List<uint>> to)
    {
        if (selected == 0)
        {
            Theme.EmptyState(FontAwesomeIcon.Recycle, "Выбери предмет", section == 2 ? "Слева сундуки и коробки; справа будет их содержимое." : "Слева предметы; справа — что из них выходит и из чего их получить.");
            return;
        }
        var info = WikiView.ItemInfo(selected);
        Theme.Panel("svhead", () =>
        {
            WikiView.ItemLine(selected, 0, Owned(selected) ? "есть у тебя" : "", "svsel");
        });

        void Block(string title, List<uint>? ids, string uid, bool markOwned)
        {
            if (ids is null || ids.Count == 0) return;
            Theme.Panel(uid, () =>
            {
                var owned = markOwned ? ids.Count(Owned) : 0;
                Theme.SubHeader(markOwned ? $"{title} ({owned} из {ids.Count} уже есть)" : $"{title} ({ids.Count})");
                foreach (var id in ids.OrderBy(Owned).ThenBy(x => WikiView.ItemInfo(x).Name).Take(300))
                    WikiView.ItemLine(id, 0, markOwned && Owned(id) ? "есть ✓" : "", uid + id);
            });
        }

        switch (section)
        {
            case 0:
                Block("При разборке может выпасть", to.GetValueOrDefault(selected), "svto", false);
                Block("Выпадает при разборке этих предметов", from.GetValueOrDefault(selected), "svfrom", false);
                ImGui.TextColored(Theme.Gray, "Шанс и количество зависят от уровня разборки твоего класса; редкие вещи падают нечасто.");
                break;
            case 1:
                Block("При превращении (Aetherial Reduction) даёт", to.GetValueOrDefault(selected), "svto", false);
                Block("Получается превращением этих коллекционных предметов", from.GetValueOrDefault(selected), "svfrom", false);
                ImGui.TextColored(Theme.Gray, "Превращать можно только коллекционные (★) предметы со сбора; больше Collectability — больше результата.");
                break;
            default:
                Block("Может выпасть", to.GetValueOrDefault(selected), "svto", true);
                if (OfflineDb.Instance.Index?.Loot.GetValueOrDefault(selected) is { Count: > 0 } inside)
                    Block("Сам лежит в сундуках", inside, "svin", false);
                break;
        }
    }
}
