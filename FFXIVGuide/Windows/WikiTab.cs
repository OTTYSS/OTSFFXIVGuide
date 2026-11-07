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
/// «Вики»: избранное и недавнее, зоны, NPC, рыбалка, синий маг.
/// </summary>
internal sealed class WikiTab
{
    private static readonly string[] Sections = { "Избранное", "Зоны", "NPC", "Рыбалка", "Синий маг" };

    private readonly GuideData data;
    private readonly QuestsTab quests;
    private int section;
    private readonly HashSet<string> expanded = new();

    public WikiTab(GuideData data, QuestsTab quests)
    {
        this.data = data;
        this.quests = quests;
    }

    public void ShowZone(uint placeName)
    {
        section = 1;
        zoneSelected = placeName;
        zoneSearch = "";
    }

    public void ShowNpc(uint id)
    {
        section = 2;
        npcSelected = id;
    }

    public void ShowFishing() => section = 3;
    public void ShowBlue() => section = 4;

    public void Draw()
    {
        Theme.Header("Вики", "Зоны, NPC, рыбалка, синяя магия, избранное. Любое имя кликабельно.");
        Theme.Chips("wsec", Sections, ref section);
        switch (section)
        {
            case 0: DrawFavorites(); break;
            case 1: DrawZones(); break;
            case 2: DrawNpcs(); break;
            case 3: DrawFishing(); break;
            case 4: DrawBlue(); break;
        }
    }

    private void More(string key, int total, int shown)
    {
        if (total <= shown) return;
        var open = expanded.Contains(key);
        if (ImGui.SmallButton(open ? $"Свернуть##{key}" : $"Показать все ({total})##{key}"))
        {
            if (open) expanded.Remove(key); else expanded.Add(key);
        }
    }

    private int Limit(string key, int n) => expanded.Contains(key) ? 10000 : n;

    // ───────────── Избранное и недавнее ─────────────

    private void DrawFavorites()
    {
        if (!ImGui.BeginChild("##wfav", Vector2.Zero)) { ImGui.EndChild(); return; }
        var favs = WikiNav.Favorites.ToList();
        Theme.Grid("wfavgrid", 2, 360f, i =>
        {
            if (i == 0)
                Theme.Panel("wfavs", () =>
                {
                    Theme.SubHeader($"Избранное · {favs.Count}");
                    if (favs.Count == 0)
                        ImGui.TextColored(Theme.TextDim, "Пусто. Нажми ★ в шапке окна на любой карточке (предмет, моб, данж, зона, NPC, квест).");
                    WikiLink? remove = null;
                    foreach (var l in favs)
                    {
                        if (LinkLine(l, "fav")) { }
                        if (ImGui.BeginPopupContextItem($"##favctx{l.Key}"))
                        {
                            if (ImGui.Selectable("Убрать из избранного")) remove = l;
                            ImGui.EndPopup();
                        }
                    }
                    if (remove is not null) WikiNav.ToggleFavorite(remove);
                    if (favs.Count > 0) ImGui.TextColored(Theme.Gray, "Правый клик — убрать.");
                });
            else
                Theme.Panel("wrecent", () =>
                {
                    Theme.SubHeader("Недавно смотрел");
                    if (WikiNav.Recent.Count == 0)
                        ImGui.TextColored(Theme.TextDim, "Здесь появятся карточки, которые ты открывал. Назад/вперёд — стрелки в шапке окна.");
                    foreach (var l in WikiNav.Recent.ToList()) LinkLine(l, "rec");
                });
        });
        ImGui.EndChild();
    }

    private static bool LinkLine(WikiLink l, string uid)
    {
        var h = 26 * Theme.S;
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##ll{uid}{l.Key}", new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), h));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        if (hovered) dl.AddRectFilled(pos, pos + new Vector2(ImGui.GetItemRectSize().X, h), Theme.U32(Theme.CardHover), 5f * Theme.S);
        var ty = (h - ImGui.GetTextLineHeight()) / 2;
        if (l.Kind == WikiKind.Item)
            Theme.GameIcon(WikiView.ItemInfo((uint)l.Id).Icon, pos + new Vector2(2 * Theme.S), h - 4 * Theme.S);
        else
        {
            ImGui.PushFont(UiBuilder.IconFont);
            dl.AddText(pos + new Vector2(6 * Theme.S, ty), Theme.U32(Theme.GoldDim), l.Icon.ToIconString());
            ImGui.PopFont();
        }
        var lname = NameTranslations.Show(l.Name);
        dl.AddText(pos + new Vector2(h + 6 * Theme.S, ty), Theme.U32(hovered ? Theme.Gold : Theme.Text), lname);
        dl.AddText(pos + new Vector2(h + 16 * Theme.S + ImGui.CalcTextSize(lname).X, ty), Theme.U32(Theme.TextDim), l.KindName);
        if (clicked) WikiNav.Open(l);
        return clicked;
    }

    // ───────────── Зоны ─────────────

    private sealed record ZoneRow(uint PlaceName, uint Territory, uint Map, string Name, string Region, string Expansion, uint ExVersion);

    private List<ZoneRow>? zones;
    private string zoneSearch = "";
    private uint zoneSelected;

    private static List<ZoneRow> BuildZones()
    {
        var list = new Dictionary<uint, ZoneRow>();
        foreach (var t in Plugin.Data.GetExcelSheet<TerritoryType>())
        {
            var use = t.TerritoryIntendedUse.RowId;
            if (use is not (0 or 1) || t.PlaceName.RowId == 0 || t.Map.RowId == 0) continue;
            var name = t.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
            if (name.Length == 0 || list.ContainsKey(t.PlaceName.RowId)) continue;
            list[t.PlaceName.RowId] = new ZoneRow(t.PlaceName.RowId, t.RowId, t.Map.RowId, name,
                t.PlaceNameRegion.ValueNullable?.Name.ExtractText() ?? "",
                t.ExVersion.ValueNullable?.Name.ExtractText() ?? "", t.ExVersion.RowId);
        }
        return list.Values.OrderBy(z => z.ExVersion).ThenBy(z => z.Region).ThenBy(z => z.Name).ToList();
    }

    public static string? ZoneNameIfKnown(uint placeName) => placeName == 0 ? null : WikiService.ZoneName(placeName);

    private void DrawZones()
    {
        zones ??= BuildZones();
        Theme.Search("##zsearch", "Зона или регион…", ref zoneSearch);
        var term = zoneSearch.Trim();
        var rows = zones.Where(z => term.Length == 0 || NameTranslations.Matches(z.Name, term) || NameTranslations.Matches(z.Region, term)).ToList();

        var avail = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##zlist", new Vector2(Theme.SplitWidth("zones", avail.X, 0.32f), 0)))
        {
            var here = Plugin.ClientState.TerritoryType;
            string? lastEx = null;
            foreach (var z in rows)
            {
                if (z.Expansion != lastEx)
                {
                    lastEx = z.Expansion;
                    Theme.SubHeader(z.Expansion);
                }
                var label = NameTranslations.Show(z.Name) + (z.Territory == here ? "  (ты здесь)" : "");
                if (ImGui.Selectable($"{label}##z{z.PlaceName}", zoneSelected == z.PlaceName))
                    WikiNav.Open(WikiKind.Zone, z.PlaceName, z.Name);
            }
        }
        ImGui.EndChild();
        Theme.Splitter("zones", avail.X, 0.32f);
        if (ImGui.BeginChild("##zdet", Vector2.Zero))
        {
            var z = zones.FirstOrDefault(r => r.PlaceName == zoneSelected);
            if (z is null && zoneSelected != 0)
            {
                // Зона не из открытого мира (например, данж): показываем по названию
                var name = WikiService.ZoneName(zoneSelected);
                z = new ZoneRow(zoneSelected, 0, 0, name, "", "", 0);
            }
            if (z is null)
                Theme.EmptyState(FontAwesomeIcon.Map, "Выбери зону", "Мобы, FATE, точки сбора, рыбалка, вистас, течения эфира и NPC зоны — в одной карточке.");
            else
                DrawZone(z);
        }
        ImGui.EndChild();
    }

    // Кеш тяжёлых выборок по зоне: пересчитываем только при смене зоны
    private readonly Dictionary<string, object> memo = new();
    private uint memoZone;

    private T Memo<T>(string key, Func<T> make)
    {
        if (memo.TryGetValue(key, out var v)) return (T)v;
        var r = make();
        memo[key] = r!;
        return r;
    }

    private void DrawZone(ZoneRow z)
    {
        if (memoZone != z.PlaceName) { memo.Clear(); memoZone = z.PlaceName; }
        WikiNav.Visit(new WikiLink(WikiKind.Zone, z.PlaceName, z.Name));
        Theme.Panel("zmain", () =>
        {
            Theme.LargeText(Theme.Gold, z.Name);
            if (z.Region.Length > 0) { Theme.InlineBadge(z.Region, Theme.Blue); ImGui.SameLine(); }
            if (z.Expansion.Length > 0) { Theme.InlineBadge(z.Expansion, Theme.TextDim); ImGui.SameLine(); }
            ImGui.NewLine();
            WikiNav.FavoriteButton(new WikiLink(WikiKind.Zone, z.PlaceName, z.Name));

            if (z.Territory == 0) return;
            // Течения эфира
            var set = Plugin.Data.GetExcelSheet<AetherCurrentCompFlgSet>().FirstOrDefault(s => s.Territory.RowId == z.Territory);
            if (set.RowId != 0)
            {
                var currents = set.AetherCurrents.Where(c => c.RowId != 0 && c.ValueNullable is not null).Select(c => c.Value).ToList();
                if (currents.Count > 0)
                {
                    var have = PlayerInfo.Ready ? currents.Count(c => Plugin.Unlocks.IsAetherCurrentUnlocked(c)) : 0;
                    Theme.Progress("Течения эфира", have, currents.Count, have == currents.Count ? Theme.Green : Theme.Blue);
                }
            }
        });

        if (!ImGui.BeginChild("##zbody", Vector2.Zero)) { ImGui.EndChild(); return; }

        var sections = new List<System.Action>();

        // Вистас (Sightseeing log)
        if (z.Territory != 0)
        {
            var vistas = Memo("vistas", () => Plugin.Data.GetExcelSheet<Adventure>()
                .Where(a => a.Level.ValueNullable is { } lv && lv.Territory.RowId == z.Territory && a.Name.ExtractText().Length > 0)
                .ToList());
            if (vistas.Count > 0)
                sections.Add(() => Theme.Panel("zvistas", () =>
                {
                    var done = PlayerInfo.Ready ? vistas.Count(v => Plugin.Unlocks.IsAdventureComplete(v)) : 0;
                    Theme.SubHeader($"Вистас (журнал достопримечательностей) · {done}/{vistas.Count}");
                    foreach (var v in vistas)
                    {
                        var ok = PlayerInfo.Ready && Plugin.Unlocks.IsAdventureComplete(v);
                        Theme.FaIcon(ok ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.Binoculars, ok ? Theme.Green : Theme.GoldDim);
                        ImGui.SameLine();
                        ImGui.TextColored(ok ? Theme.TextDim : Theme.Text, v.Name.ExtractText());
                        ImGui.SameLine();
                        var time = v.MinTime != 0 || v.MaxTime != 0 ? $"{v.MinTime / 100:00}:{v.MinTime % 100:00}–{v.MaxTime / 100:00}:{v.MaxTime % 100:00} ET" : "";
                        if (time.Length > 0) { ImGui.TextColored(Theme.TextDim, time); ImGui.SameLine(); }
                        if (ImGui.SmallButton($"Флаг##v{v.RowId}")) ItemSources.FlagLevel(v.Level.RowId);
                    }
                }));
        }

        // FATE
        var fates = WikiService.Instance.FateIndex();
        sections.Add(() => Theme.Panel("zfates", () =>
        {
            if (fates is null) { Theme.SubHeader("FATE"); WikiView.Status(null, "browse", "fate"); return; }
            var list = Memo("fates", () => fates.Where(f => f.Zone == z.PlaceName).OrderBy(f => f.Level).ToList());
            Theme.SubHeader($"FATE · {list.Count}");
            if (list.Count == 0) { ImGui.TextColored(Theme.TextDim, "Нет FATE в этой зоне."); return; }
            var active = Plugin.Fates.Where(a => a.State == Dalamud.Game.ClientState.Fates.FateState.Running).GroupBy(a => (uint)a.FateId).ToDictionary(g => g.Key, g => g.First());
            foreach (var f in list.Take(Limit("zf", 10)))
                FateLine(f, active.GetValueOrDefault(f.Id));
            ImGui.TextColored(Theme.Gray, "Нажми на FATE — описание, задача и награды.");
            More("zf", list.Count, 10);
        }));

        // Мобы
        var mobs = WikiService.Instance.MobIndex();
        sections.Add(() => Theme.Panel("zmobs", () =>
        {
            if (mobs is null) { Theme.SubHeader("Мобы"); WikiView.Status(null, "browse", "mob"); return; }
            var list = Memo("mobs", () => mobs.Where(m => m.Zone == z.PlaceName).GroupBy(m => m.Name).Select(g => g.First()).OrderBy(m => m.Level.Length).ThenBy(m => m.Level).ToList());
            Theme.SubHeader($"Мобы · {list.Count}");
            if (list.Count == 0) ImGui.TextColored(Theme.TextDim, "Нет данных о мобах.");
            foreach (var m in list.Take(Limit("zm", 12))) WikiView.MobLine(m, "z");
            More("zm", list.Count, 12);
        }));

        // Сбор
        if (z.Territory != 0)
        {
            var points = Memo("gather", () => GatherIndex.All
                .SelectMany(kv => kv.Value.Where(s => s.Territory == z.Territory).Select(s => (Item: kv.Key, Spot: s)))
                .GroupBy(x => (x.Spot.Type, x.Spot.Place, x.Spot.Level, x.Spot.Timed))
                .OrderBy(g => g.Key.Level)
                .ToList());
            if (points.Count > 0)
                sections.Add(() => Theme.Panel("zgather", () =>
                {
                    Theme.SubHeader($"Точки сбора · {points.Count}");
                    var i = 0;
                    foreach (var g in points.Take(Limit("zg", 8)))
                    {
                        var s = g.First().Spot;
                        Theme.FaIcon(FontAwesomeIcon.Leaf, g.Key.Timed ? Theme.Yellow : Theme.Green);
                        ImGui.SameLine();
                        ImGui.TextColored(Theme.Text, $"{g.Key.Type} · ур. {g.Key.Level} · {g.Key.Place}" + (g.Key.Timed ? " · по времени" : ""));
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"Флаг##g{i}")) GatherIndex.Flag(s);
                        WikiView.ItemGrid(g.Select(x => x.Item).Distinct(), $"zg{i}");
                        i++;
                    }
                    More("zg", points.Count, 8);
                }));

            // Рыбалка
            var spots = Memo("fish", () => Plugin.Data.GetExcelSheet<FishingSpot>()
                .Where(f => f.TerritoryType.RowId == z.Territory && f.PlaceName.RowId != 0)
                .ToList());
            if (spots.Count > 0)
                sections.Add(() => Theme.Panel("zfish", () =>
                {
                    Theme.SubHeader($"Места рыбалки · {spots.Count}");
                    foreach (var f in spots)
                    {
                        Theme.FaIcon(FontAwesomeIcon.Fish, Theme.Blue);
                        ImGui.SameLine();
                        ImGui.TextColored(Theme.Text, $"{f.PlaceName.ValueNullable?.Name.ExtractText()} · ур. {f.GatheringLevel}");
                        WikiView.ItemGrid(f.Item.Where(it => it.RowId != 0).Select(it => it.RowId).Distinct(), $"zfs{f.RowId}");
                    }
                }));
        }

        // NPC
        var npcs = WikiService.Instance.NpcIndex();
        sections.Add(() => Theme.Panel("znpc", () =>
        {
            if (npcs is null) { Theme.SubHeader("NPC"); WikiView.Status(null, "browse", "npc"); return; }
            var list = Memo("npcs", () => npcs.Where(n => n.Zone == z.PlaceName && (n.Title.Length > 0 || n.Quests > 0))
                .GroupBy(n => n.Name + "|" + n.Title).Select(g => g.First())
                .OrderByDescending(n => n.Title.Length > 0).ThenByDescending(n => n.Quests).ThenBy(n => n.Name).ToList());
            Theme.SubHeader($"NPC · {list.Count}");
            if (list.Count == 0) ImGui.TextColored(Theme.TextDim, "Нет данных об NPC.");
            foreach (var n in list.Take(Limit("zn", 15))) NpcRowLine(n, "z");
            More("zn", list.Count, 15);
        }));

        Theme.Grid("zgrid", sections.Count, 420f, i => sections[i]());
        ImGui.EndChild();
    }

    // ───────────── FATE ─────────────

    private uint fateOpen;

    /// <summary>Строка FATE: по клику раскрывается карточка (описание, задача, награды, идёт ли сейчас).</summary>
    private void FateLine(WikiFate f, Dalamud.Game.ClientState.Fates.IFate? live)
    {
        var h = 26 * Theme.S;
        var pos = ImGui.GetCursorScreenPos();
        var w = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var open = fateOpen == f.Id;
        var clicked = ImGui.InvisibleButton($"##fl{f.Id}", new Vector2(w, h));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        if (hovered || open) dl.AddRectFilled(pos, pos + new Vector2(w, h), Theme.U32(open ? Theme.CardSelected : Theme.CardHover), 5f * Theme.S);
        var row = Plugin.Data.GetExcelSheet<Fate>().GetRowOrDefault(f.Id);
        var ty = (h - ImGui.GetTextLineHeight()) / 2;
        if (row is { } r && r.Icon != 0)
            Theme.GameIcon(r.Icon, pos + new Vector2(2 * Theme.S), h - 4 * Theme.S);
        else
        {
            ImGui.PushFont(UiBuilder.IconFont);
            dl.AddText(pos + new Vector2(6 * Theme.S, ty), Theme.U32(Theme.Red), FontAwesomeIcon.Fire.ToIconString());
            ImGui.PopFont();
        }
        var name = NameTranslations.Show(row?.Name.ExtractText() is { Length: > 0 } n ? n : f.Name);
        dl.AddText(pos + new Vector2(h + 6 * Theme.S, ty), Theme.U32(hovered || open ? Theme.Gold : Theme.Text), name);
        var sub = $"ур. {f.Level} · {f.Type}";
        dl.AddText(pos + new Vector2(h + 16 * Theme.S + ImGui.CalcTextSize(name).X, ty), Theme.U32(Theme.TextDim), sub);
        if (live is not null)
        {
            var bw = Theme.BadgeSize("идёт сейчас").X;
            Theme.Badge(new Vector2(pos.X + w - bw - 4 * Theme.S, pos.Y + 3 * Theme.S), "идёт сейчас", Theme.Green);
        }
        if (clicked) fateOpen = open ? 0 : f.Id;
        if (!open) return;

        ImGui.Indent(10 * Theme.S);
        var full = WikiService.Instance.Fate(f.Id);
        var objective = row?.Objective.ExtractText() ?? "";
        var desc = row?.Description.ExtractText() ?? "";
        if (desc.Length == 0) desc = full?.Description ?? "";
        if (desc.Length > 0) ImGui.TextColored(Theme.TextDim, desc);
        if (objective.Length > 0)
        {
            Theme.FaIcon(FontAwesomeIcon.Bullseye, Theme.Gold);
            ImGui.SameLine();
            ImGui.TextColored(Theme.Text, objective);
        }
        var maxLvl = row?.ClassJobLevelMax ?? 0;
        ImGui.TextColored(Theme.TextDim, maxLvl > 0 ? $"Уровень: {f.Level}, синхронизация до {maxLvl}" : $"Уровень: {f.Level}");
        if (row is { } rr && rr.TurnInEventItem.RowId != 0 && rr.TurnInEventItem.ValueNullable is { } ev)
            ImGui.TextColored(Theme.TextDim, $"Сдавать: {ev.Name.ExtractText()}");
        if (live is not null)
        {
            var left = TimeSpan.FromSeconds(Math.Max(0, (int)live.TimeRemaining));
            Theme.Progress($"Идёт сейчас · осталось {left.Minutes}:{left.Seconds:00}", live.Progress, 100, Theme.Green);
        }
        if (f.X > 0)
        {
            ImGui.TextColored(Theme.TextDim, $"Координаты: X {f.X:0.0}, Y {f.Y:0.0}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Флаг на карте##ff{f.Id}")) WikiService.Flag(f.Zone, f.X, f.Y);
        }
        if (full is { Items.Count: > 0 })
        {
            ImGui.TextColored(Theme.TextDim, "Награды:");
            WikiView.ItemGrid(full.Items, $"fr{f.Id}");
        }
        else if (full is null && WikiService.Instance.Error("fate", f.Id) is null)
            ImGui.TextColored(Theme.Gray, "Загружаю награды…");
        ImGui.Unindent(10 * Theme.S);
        ImGui.Dummy(new Vector2(0, 4 * Theme.S));
    }

    // ───────────── NPC ─────────────

    private string npcSearch = "";
    private string npcLast = "\0";
    private List<WikiNpcRow> npcHits = new();
    private int npcTotal;
    private uint npcSelected;
    private Dictionary<uint, List<uint>>? questsByNpc;

    private static void NpcRowLine(WikiNpcRow n, string uid)
    {
        var h = 26 * Theme.S;
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##nl{uid}{n.Id}", new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), h));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        if (hovered) dl.AddRectFilled(pos, pos + new Vector2(ImGui.GetItemRectSize().X, h), Theme.U32(Theme.CardHover), 5f * Theme.S);
        var ty = (h - ImGui.GetTextLineHeight()) / 2;
        ImGui.PushFont(UiBuilder.IconFont);
        dl.AddText(pos + new Vector2(6 * Theme.S, ty), Theme.U32(Theme.GoldDim), FontAwesomeIcon.User.ToIconString());
        ImGui.PopFont();
        var nname = NameTranslations.Show(n.Name);
        dl.AddText(pos + new Vector2(28 * Theme.S, ty), Theme.U32(hovered ? Theme.Gold : Theme.Text), nname);
        var sub = (n.Title.Length > 0 ? NameTranslations.Show(n.Title) : "") + (n.Quests > 0 ? $"{(n.Title.Length > 0 ? " · " : "")}квестов: {n.Quests}" : "");
        dl.AddText(pos + new Vector2(38 * Theme.S + ImGui.CalcTextSize(nname).X, ty), Theme.U32(Theme.TextDim), sub);
        if (clicked) WikiNav.Open(WikiKind.Npc, n.Id, n.Name);
    }

    private void DrawNpcs()
    {
        Theme.Search("##nsearch", "Имя NPC или должность (например: Material Supplier, Mender)…", ref npcSearch);
        var index = WikiService.Instance.NpcIndex();
        if (index is null)
        {
            Theme.Panel("nload", () => WikiView.Status(null, "browse", "npc"));
            return;
        }
        var term = npcSearch.Trim();
        if (npcSearch != npcLast)
        {
            npcLast = npcSearch;
            var found = term.Length < 2 ? new List<WikiNpcRow>() :
                index.Where(n => NameTranslations.Matches(n.Name, term) || NameTranslations.Matches(n.Title, term))
                     .OrderBy(n => n.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenByDescending(n => n.Quests).ThenBy(n => n.Name).ToList();
            npcTotal = found.Count;
            npcHits = found.Take(300).ToList();
        }
        ImGui.TextColored(Theme.TextDim, term.Length < 2 ? $"В базе {index.Count:N0} NPC. Начни вводить имя или должность." : $"Найдено: {npcTotal}" + (npcTotal > 300 ? " (первые 300)" : ""));

        var avail = ImGui.GetContentRegionAvail();
        var rowH = 42 * Theme.S;
        if (ImGui.BeginChild("##nlist", new Vector2(Theme.SplitWidth("npcs", avail.X, 0.42f), 0)))
            Theme.VirtualList(npcHits.Count, rowH, i =>
            {
                var n = npcHits[i];
                var sub = WikiService.ZoneName(n.Zone) + (n.Title.Length > 0 ? $" · {n.Title}" : "");
                if (Theme.Card($"##n{n.Id}", 0, n.Name, sub, n.Quests > 0 ? $"квестов {n.Quests}" : null, Theme.Yellow, npcSelected == n.Id, rowH))
                    WikiNav.Open(WikiKind.Npc, n.Id, n.Name);
            });
        ImGui.EndChild();
        Theme.Splitter("npcs", avail.X, 0.42f);
        if (ImGui.BeginChild("##ndet", Vector2.Zero))
        {
            if (npcSelected == 0)
                Theme.EmptyState(FontAwesomeIcon.User, "Выбери NPC", "Где стоит (флаг на карте), что продаёт и какие квесты даёт.");
            else
                DrawNpc(npcSelected, index);
        }
        ImGui.EndChild();
    }

    private void DrawNpc(uint id, List<WikiNpcRow> index)
    {
        var row = index.FirstOrDefault(n => n.Id == id);
        var npc = WikiService.Instance.Npc(id);
        var name = npc?.Name ?? row?.Name ?? $"NPC #{id}";
        WikiNav.Visit(new WikiLink(WikiKind.Npc, id, name));

        Theme.Panel("nmain", () =>
        {
            Theme.LargeText(Theme.Gold, name);
            var title = npc?.Title ?? row?.Title ?? "";
            if (title.Length > 0) ImGui.TextColored(Theme.TextDim, title);
            WikiNav.FavoriteButton(new WikiLink(WikiKind.Npc, id, name));
            if (!WikiView.Status(npc, "npc", id)) return;
            var zone = WikiService.ZoneName(npc!.Zone);
            if (zone.Length > 0)
            {
                Theme.FaIcon(FontAwesomeIcon.MapMarkerAlt, Theme.GoldDim);
                ImGui.SameLine();
                if (ImGui.Selectable($"{zone}##nz", false, ImGuiSelectableFlags.None, new Vector2(ImGui.CalcTextSize(zone).X, 0)))
                    WikiNav.Open(WikiKind.Zone, npc.Zone, zone);
                Theme.Tooltip("Открыть карточку зоны");
                if (npc.HasCoords)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.TextDim, $"X {npc.X:0.0}, Y {npc.Y:0.0}");
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Флаг на карте##nflag")) WikiService.Flag(npc.Zone, npc.X, npc.Y);
                }
            }
        });
        if (npc is null) return;

        // Квесты, которые NPC выдаёт (из файлов игры)
        questsByNpc ??= Plugin.Data.GetExcelSheet<Quest>()
            .Where(q => q.IssuerStart.RowId != 0 && q.Name.ExtractText().Length > 0)
            .GroupBy(q => q.IssuerStart.RowId)
            .ToDictionary(g => g.Key, g => g.Select(q => q.RowId).ToList());
        if (questsByNpc.TryGetValue(id, out var qids))
        {
            var list = qids.Where(data.Quests.ContainsKey).Select(q => data.Quests[q]).OrderBy(q => q.Level).ToList();
            if (list.Count > 0)
                Theme.Panel("nquests", () =>
                {
                    Theme.SubHeader($"Выдаёт квесты · {list.Count}");
                    foreach (var q in list.Take(Limit("nq", 10)))
                    {
                        var (badge, color) = QuestsTab.StateBadge(q);
                        if (Theme.Card($"##nq{q.Id}", q.Icon, q.Name, $"Ур. {q.Level} · {q.Genre}", badge, color, false, 40 * Theme.S))
                            WikiNav.Open(WikiKind.Quest, q.Id, q.Name);
                    }
                    More("nq", list.Count, 10);
                });
        }

        foreach (var (shop, i) in npc.Shops.Select((s, i) => (s, i)))
            Theme.Panel($"nshop{i}", () =>
            {
                Theme.SubHeader($"{(shop.Name.Length > 0 ? shop.Name : "Магазин")} · {shop.Items.Count}");
                WikiView.ItemGrid(shop.Items, $"ns{i}");
            });
    }

    // ───────────── Рыбалка ─────────────

    private sealed record FishRow(uint Item, string Name, uint Icon, int Level, string Spot, uint SpotZone);

    private List<FishRow>? fish;
    private string fishSearch = "";
    private string fishLast = "\0";
    private int fishFilter;
    private static readonly string[] FishFilters = { "Все", "Не пойманы", "Пойманы", "Окно открыто сейчас" };
    private readonly Dictionary<uint, (long At, bool? Open)> fishWindows = new();

    private static unsafe bool Caught(uint item)
    {
        if (!PlayerInfo.Ready) return false;
        var ps = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
        return ps != null && ps->IsFishCaught(item);
    }

    /// <summary>null — рыба ловится всегда; true/false — открыто ли сейчас окно по времени и погоде.</summary>
    private bool? WindowOpen(uint item)
    {
        var now = EorzeaWeather.Now;
        if (fishWindows.TryGetValue(item, out var c) && now - c.At < 60) return c.Open;
        bool? open = null;
        if (OfflineDb.Instance.Index?.Fishing.TryGetValue(item, out var src) == true)
            foreach (var s in src)
            {
                var timed = s.Spawn >= 0 && s.Duration is > 0 and < 24;
                if (!timed && s.Weathers.Length == 0) continue;
                OfflineDb.Instance.Index.FishingSpots.TryGetValue(s.Spot, out var spot);
                var (place, _) = spot is null ? (0u, 0u) : OfflineIndex.MapInfo(spot.Map);
                var rate = EorzeaWeather.RateForPlace(place);
                var win = EorzeaWeather.NextWindow(rate, s.Weathers.Length > 0 ? s.Weathers : null, s.WeathersFrom.Length > 0 ? s.WeathersFrom : null,
                    timed ? s.Spawn : 0, timed ? (s.Spawn + s.Duration) % 24 : 24, maxDays: 1);
                open = (open ?? false) || (win is { } w && w.Start <= now);
            }
        fishWindows[item] = (now, open);
        return open;
    }
    private List<FishRow>? fishRows;

    private static List<FishRow> BuildFish()
    {
        var items = Plugin.Data.GetExcelSheet<Item>();
        var list = new List<FishRow>();
        foreach (var f in Plugin.Data.GetExcelSheet<FishParameter>())
        {
            if (f.Item.RowId == 0 || !f.IsInLog) continue;
            var it = items.GetRowOrDefault(f.Item.RowId);
            if (it is null) continue;
            var name = it.Value.Name.ExtractText();
            if (name.Length == 0) continue;
            var spot = f.FishingSpot.ValueNullable;
            list.Add(new FishRow(f.Item.RowId, name, it.Value.Icon,
                (int)(f.GatheringItemLevel.ValueNullable?.GatheringItemLevel ?? 0),
                spot?.PlaceName.ValueNullable?.Name.ExtractText() ?? "",
                spot?.TerritoryType.ValueNullable?.PlaceName.RowId ?? 0));
        }
        return list.OrderBy(f => f.Level).ThenBy(f => f.Name).ToList();
    }

    private void DrawFishing()
    {
        fish ??= BuildFish();
        Theme.Search("##fsearch", "Рыба, место или зона…", ref fishSearch);
        var term = fishSearch.Trim();
        if (fishRows is null || fishLast != fishSearch)
        {
            fishLast = fishSearch;
            fishRows = fish.Where(f => term.Length == 0 || NameTranslations.Matches(f.Name, term)
                                       || NameTranslations.Matches(f.Spot, term)
                                       || NameTranslations.Matches(WikiService.ZoneName(f.SpotZone), term)).ToList();
        }
        // Журнал рыбака: поймана или нет, открыто ли окно сейчас
        var caughtTotal = PlayerInfo.Ready ? fish.Count(f => Caught(f.Item)) : 0;
        if (PlayerInfo.Ready) Theme.Progress("Поймано рыб (журнал рыбака)", caughtTotal, fish.Count, Theme.Blue);
        Theme.Chips("ffilter", FishFilters, ref fishFilter);
        var rows = fishRows.Where(f => fishFilter switch
        {
            1 => !Caught(f.Item),
            2 => Caught(f.Item),
            3 => WindowOpen(f.Item) == true,
            _ => true,
        }).ToList();
        ImGui.TextColored(Theme.TextDim, $"Найдено: {rows.Count}. Нажми на рыбу — места, наживка, подсечка, время и погода.");
        var rowH = 42 * Theme.S;
        if (ImGui.BeginChild("##flist", Vector2.Zero))
            Theme.VirtualList(rows.Count, rowH, i =>
            {
                var f = rows[i];
                var zone = WikiService.ZoneName(f.SpotZone);
                var caught = Caught(f.Item);
                var open = WindowOpen(f.Item);
                var badge = open == true ? "окно открыто" : caught ? "поймана" : "не поймана";
                if (Theme.Card($"##fish{f.Item}", f.Icon, f.Name, $"ур. {f.Level} · {f.Spot}" + (zone.Length > 0 ? $" · {zone}" : "") + (open is null ? "" : " · по времени/погоде"),
                        badge, open == true ? Theme.Gold : caught ? Theme.Green : Theme.Gray, false, rowH))
                    WikiNav.Open(WikiKind.Item, f.Item, f.Name);
            });
        ImGui.EndChild();
    }

    // ───────────── Синий маг ─────────────

    private sealed record BlueSpell(uint Id, int Number, string Name, uint Icon, string Where, string Stats, string Description, AozAction Row);

    private List<BlueSpell>? spells;
    private string blueSearch = "";
    private int blueFilter;
    private static readonly string[] BlueFilters = { "Все", "Не выучены", "Выучены" };

    private static List<BlueSpell> BuildSpells()
    {
        var trans = Plugin.Data.GetExcelSheet<AozActionTransient>();
        var list = new List<BlueSpell>();
        foreach (var a in Plugin.Data.GetExcelSheet<AozAction>())
        {
            if (a.Action.RowId == 0 || a.Action.ValueNullable is not { } act) continue;
            var t = trans.GetRowOrDefault(a.RowId);
            if (t is null || t.Value.Number == 0) continue;
            var where = "";
            var loc = t.Value.Location;
            if (loc.TryGetValue<ContentFinderCondition>(out var cfc)) where = cfc.Name.ExtractText();
            else if (loc.TryGetValue<PlaceName>(out var pn)) where = pn.Name.ExtractText();
            else if (loc.TryGetValue<Quest>(out var q)) where = "Квест: " + q.Name.ExtractText();
            list.Add(new BlueSpell(a.RowId, t.Value.Number, act.Name.ExtractText(), t.Value.Icon, GuideData.Capitalize(where),
                t.Value.Stats.ExtractText(), t.Value.Description.ExtractText(), a));
        }
        return list.OrderBy(s => s.Number).ToList();
    }

    private void DrawBlue()
    {
        spells ??= BuildSpells();
        Theme.Search("##bsearch", "Заклинание или где учить…", ref blueSearch);
        Theme.Chips("bfilter", BlueFilters, ref blueFilter);
        var learned = PlayerInfo.Ready ? spells.Count(s => Plugin.Unlocks.IsAozActionUnlocked(s.Row)) : 0;
        if (PlayerInfo.Ready)
            Theme.Progress("Выучено заклинаний", learned, spells.Count, Theme.Blue);
        var term = blueSearch.Trim();
        var rows = spells.Where(s => (term.Length == 0 || NameTranslations.Matches(s.Name, term) || NameTranslations.Matches(s.Where, term))
                                     && (blueFilter == 0 || !PlayerInfo.Ready || Plugin.Unlocks.IsAozActionUnlocked(s.Row) == (blueFilter == 2))).ToList();

        if (!ImGui.BeginChild("##blist", Vector2.Zero)) { ImGui.EndChild(); return; }
        Theme.Grid("bgrid", rows.Count, 380f, i =>
        {
            var s = rows[i];
            var ok = PlayerInfo.Ready && Plugin.Unlocks.IsAozActionUnlocked(s.Row);
            Theme.Panel($"bs{s.Id}", () =>
            {
                var pos = ImGui.GetCursorScreenPos();
                Theme.GameIcon(s.Icon, pos, 40 * Theme.S, 6f);
                ImGui.SetCursorScreenPos(pos + new Vector2(48 * Theme.S, 0));
                ImGui.BeginGroup();
                ImGui.TextColored(ok ? Theme.Green : Theme.Text, $"№{s.Number} {NameTranslations.Show(s.Name)}" + (ok ? "  ✓" : ""));
                ImGui.TextColored(Theme.TextDim, s.Where.Length > 0 ? s.Where : "место не указано");
                ImGui.EndGroup();
                ImGui.SetCursorScreenPos(new Vector2(pos.X, Math.Max(ImGui.GetCursorScreenPos().Y, pos.Y + 44 * Theme.S)));
                if (ImGui.IsMouseHoveringRect(pos, pos + new Vector2(ImGui.GetContentRegionAvail().X, 44 * Theme.S)))
                    ImGui.SetTooltip((s.Stats + "\n\n" + s.Description).Trim());
                var duty = data.Duties.FirstOrDefault(d => d.Name.Equals(s.Where, StringComparison.OrdinalIgnoreCase));
                if (duty is not null && ImGui.SmallButton($"Открыть данж##bd{s.Id}"))
                    WikiNav.Open(WikiKind.Duty, duty.CfcId, duty.Name);
            });
        });
        ImGui.EndChild();
    }
}
