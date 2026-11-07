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

internal sealed class GearTab
{
    private static readonly (GearSlot Slot, string Name, int[] EquipIndexes)[] Slots =
    {
        (GearSlot.MainHand, "Оружие", new[] { 0 }),
        (GearSlot.OffHand, "Вторая рука", new[] { 1 }),
        (GearSlot.Head, "Голова", new[] { 2 }),
        (GearSlot.Body, "Тело", new[] { 3 }),
        (GearSlot.Hands, "Руки", new[] { 4 }),
        (GearSlot.Legs, "Ноги", new[] { 6 }),
        (GearSlot.Feet, "Обувь", new[] { 7 }),
        (GearSlot.Ears, "Серьги", new[] { 8 }),
        (GearSlot.Neck, "Ожерелье", new[] { 9 }),
        (GearSlot.Wrists, "Браслеты", new[] { 10 }),
        (GearSlot.Ring, "Кольца", new[] { 11, 12 }),
    };

    private static readonly string[] LevelModeNames = { "Мой уровень", "+3 уровня", "+5 уровней", "Максимум" };
    private static readonly int[] LevelOffsets = { 0, 3, 5, 999 };
    private static readonly string[] SortModeNames = { "Лучшие по iLvl", "Дешёвые апгрейды на рынке" };
    private static readonly string[] SourceModeNames = { "Купить, скрафтить, за томестоны", "Все источники (с рейдами)" };

    private const int CandidatesPerSlot = 12;
    private const int ShownPerSlot = 6;

    private readonly GuideData data;
    private readonly MarketService market;
    private int levelMode;
    private int sortMode;
    private int scopeMode; // 0 — только мой мир, 1 — весь датацентр
    private int sourceMode; // 0 — только легкодоступное, 1 — всё
    private bool hideOwned;
    private (uint Job, int Level, int Source) cacheKey;
    private Dictionary<GearSlot, List<GearEntry>> cache = new();
    private uint copiedId;
    private DateTime copiedAt;

    public GearTab(GuideData data, MarketService market)
    {
        this.data = data;
        this.market = market;
    }

    // ───────────── Кукла персонажа ─────────────

    private static readonly (GearSlot Slot, string Name, int Index)[] LeftSlots =
    {
        (GearSlot.MainHand, "Оружие", 0), (GearSlot.Head, "Голова", 2), (GearSlot.Body, "Тело", 3),
        (GearSlot.Hands, "Руки", 4), (GearSlot.Legs, "Ноги", 6), (GearSlot.Feet, "Обувь", 7),
    };

    private static readonly (GearSlot Slot, string Name, int Index)[] RightSlots =
    {
        (GearSlot.OffHand, "Вторая рука", 1), (GearSlot.Ears, "Серьги", 8), (GearSlot.Neck, "Ожерелье", 9),
        (GearSlot.Wrists, "Браслеты", 10), (GearSlot.Ring, "Кольцо 1", 11), (GearSlot.Ring, "Кольцо 2", 12),
    };

    private sealed record SlotState(int Index, GearSlot Slot, string Name, Item? Row, uint Ilvl, uint BestIlvl,
        int MainStat, int MateriaSlots, int Melded, bool WrongStat, bool Disabled);

    private int selectedIndex;

    public void Draw()
    {
        Theme.Header("Экипировка", "Нажми на слот — покажу, чем его улучшить. Сверху — оценка всего сета.");

        if (!PlayerInfo.Ready || Plugin.PlayerState.ClassJob.ValueNullable is not { } job)
        {
            Theme.Panel("gnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем, чтобы подобрать экипировку под текущую профессию."));
            return;
        }

        var level = (int)Plugin.PlayerState.Level;
        var abbr = GuideData.JobAbbrEnglish(job.RowId);
        var worldName = MarketService.CurrentRegion(true);
        var dcName = MarketService.CurrentRegion(false);
        var region = scopeMode == 0 ? worldName : dcName;

        var target = Math.Min(level + LevelOffsets[levelMode], 999);
        if (cacheKey != (job.RowId, target, sourceMode))
        {
            cache = Build(job, abbr, target);
            cacheKey = (job.RowId, target, sourceMode);
        }

        var primary = job.PrimaryStat;
        var states = ReadSlots(primary);

        if (region is not null)
            market.Request(region, cache.Values.SelectMany(l => l).Where(g => g.Marketable).Select(g => g.Id));

        if (!ImGui.BeginChild("##gear", Vector2.Zero))
        {
            ImGui.EndChild();
            return;
        }

        DrawScore(states);

        var dollW = 330 * Theme.S;
        if (ImGui.BeginChild("##doll", new Vector2(dollW, 0)))
            DrawDoll(states, job);
        ImGui.EndChild();

        ImGui.SameLine(0, 10 * Theme.S);
        if (ImGui.BeginChild("##slotdetails", Vector2.Zero))
        {
            var state = states.FirstOrDefault(s => s.Index == selectedIndex) ?? states[0];
            DrawSlotDetails(state, primary, region, worldName, dcName);
        }
        ImGui.EndChild();

        ImGui.EndChild();
    }

    private List<SlotState> ReadSlots(uint primary)
    {
        var itemSheet = Plugin.Data.GetExcelSheet<Item>();
        var twoHanded = IsTwoHanded(PlayerInfo.EquippedItem(0));
        var list = new List<SlotState>();
        foreach (var (slot, name, idx) in LeftSlots.Concat(RightSlots))
        {
            var id = PlayerInfo.EquippedItem(idx);
            var row = id == 0 ? null : itemSheet.GetRowOrDefault(id);
            var best = cache.TryGetValue(slot, out var c) && c.Count > 0 ? c[0].ItemLevel : 0;
            var ilvl = row?.LevelItem.RowId ?? 0;
            var mainStat = row is null ? 0 : MainStatOf(row.Value, primary);
            var wrong = row is not null && row.Value.BaseParam.Any(p => p.RowId is 1 or 2 or 4 or 5)
                        && !row.Value.BaseParam.Any(p => p.RowId == primary);
            var (slots, melded) = MateriaOf(idx, row);
            list.Add(new SlotState(idx, slot, name, row, ilvl, best, mainStat, slots, melded, wrong, slot == GearSlot.OffHand && twoHanded));
        }
        return list;
    }

    private static unsafe (int Slots, int Melded) MateriaOf(int equipIndex, Item? row)
    {
        if (row is null) return (0, 0);
        var im = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
        if (im == null) return (row.Value.MateriaSlotCount, 0);
        var s = im->GetInventorySlot(FFXIVClientStructs.FFXIV.Client.Game.InventoryType.EquippedItems, equipIndex);
        if (s == null) return (row.Value.MateriaSlotCount, 0);
        var melded = 0;
        for (byte m = 0; m < 5; m++)
            if (s->GetMateriaId(m) != 0) melded++;
        return (row.Value.MateriaSlotCount, Math.Min(melded, (int)row.Value.MateriaSlotCount));
    }

    private static Vector4 StatusColor(SlotState s)
    {
        if (s.Disabled) return Theme.Gray;
        if (s.Row is null) return Theme.Red;
        if (s.BestIlvl == 0 || s.Ilvl >= s.BestIlvl) return Theme.Green;
        return s.BestIlvl - s.Ilvl <= 10 ? Theme.Yellow : Theme.Red;
    }

    // ───────────── Оценка сета ─────────────

    private static (int Percent, string Grade, Vector4 Color, List<string> Tips) Score(List<SlotState> states)
    {
        var active = states.Where(s => !s.Disabled).ToList();
        var withBest = active.Where(s => s.BestIlvl > 0).ToList();
        var ilvlRatio = withBest.Count == 0 ? 1.0 : Math.Min(1.0, withBest.Sum(s => (double)s.Ilvl) / withBest.Sum(s => (double)s.BestIlvl));
        var bestRatio = withBest.Count == 0 ? 1.0 : withBest.Count(s => s.Ilvl >= s.BestIlvl) / (double)withBest.Count;
        var totalMateria = active.Sum(s => s.MateriaSlots);
        var meldRatio = totalMateria == 0 ? 1.0 : active.Sum(s => s.Melded) / (double)totalMateria;
        var statRatio = active.Count == 0 ? 1.0 : 1.0 - active.Count(s => s.WrongStat) / (double)active.Count;

        var pct = (int)Math.Round(100 * (0.55 * ilvlRatio + 0.2 * bestRatio + 0.15 * meldRatio + 0.1 * statRatio));
        var (grade, color) = pct switch
        {
            >= 95 => ("S", Theme.Gold),
            >= 85 => ("A", Theme.Green),
            >= 70 => ("B", Theme.Blue),
            >= 50 => ("C", Theme.Yellow),
            _ => ("D", Theme.Red),
        };

        var tips = new List<string>();
        var empty = active.Where(s => s.Row is null).Select(s => s.Name).ToList();
        if (empty.Count > 0) tips.Add($"Пустые слоты: {string.Join(", ", empty)}");
        foreach (var s in withBest.Where(s => s.Row is not null && s.BestIlvl > s.Ilvl).OrderByDescending(s => s.BestIlvl - s.Ilvl).Take(3))
            tips.Add($"{s.Name}: можно +{s.BestIlvl - s.Ilvl} iLvl");
        if (totalMateria > 0 && meldRatio < 1)
            tips.Add($"Материя: вставлено {active.Sum(s => s.Melded)} из {totalMateria} слотов");
        var wrong = active.Where(s => s.WrongStat).Select(s => s.Name).ToList();
        if (wrong.Count > 0) tips.Add($"Не тот основной стат: {string.Join(", ", wrong)}");
        if (tips.Count == 0) tips.Add("Сет собран отлично для твоего уровня.");
        return (pct, grade, color, tips);
    }

    private static void DrawScore(List<SlotState> states)
    {
        var (pct, grade, color, tips) = Score(states);
        Theme.Panel("gscore", () =>
        {
            var start = ImGui.GetCursorScreenPos();
            var dl = ImGui.GetWindowDrawList();
            var r = 30 * Theme.S;
            var c = start + new Vector2(r, r);
            dl.AddCircleFilled(c, r, Theme.U32(Theme.Alpha(color, 0.18f)));
            dl.AddCircle(c, r, Theme.U32(color), 0, 3f * Theme.S);
            // Дуга-прогресс
            var segs = 40;
            for (var i = 0; i < segs * pct / 100; i++)
            {
                var a1 = -MathF.PI / 2 + i * 2 * MathF.PI / segs;
                var a2 = a1 + 2 * MathF.PI / segs;
                dl.AddLine(c + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * (r + 5 * Theme.S),
                    c + new Vector2(MathF.Cos(a2), MathF.Sin(a2)) * (r + 5 * Theme.S), Theme.U32(color), 3f * Theme.S);
            }
            Theme.LargeTextAt(c, grade, color);

            ImGui.SetCursorScreenPos(start + new Vector2(r * 2 + 20 * Theme.S, 0));
            ImGui.BeginGroup();
            Theme.LargeText(Theme.Text, $"Оценка сета: {pct}%");
            foreach (var t in tips)
            {
                Theme.FaIcon(t.StartsWith("Сет собран") ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.ArrowRight,
                    t.StartsWith("Сет собран") ? Theme.Green : Theme.GoldDim);
                ImGui.SameLine();
                ImGui.TextColored(Theme.TextDim, t);
            }
            ImGui.EndGroup();
            ImGui.SetCursorScreenPos(new Vector2(start.X, Math.Max(ImGui.GetItemRectMax().Y, start.Y + r * 2 + 10 * Theme.S)));
            ImGui.Dummy(Vector2.Zero);
        });
    }

    // ───────────── Кукла ─────────────

    private void DrawDoll(List<SlotState> states, ClassJob job)
    {
        var S = Theme.S;
        var slot = 48 * S;
        var gap = 10 * S;
        var origin = ImGui.GetCursorScreenPos() + new Vector2(4 * S, 4 * S);
        var width = ImGui.GetContentRegionAvail().X - 8 * S;
        var height = 6 * slot + 5 * gap;
        var dl = ImGui.GetWindowDrawList();

        // Фон «окна персонажа»
        dl.AddRectFilled(origin - new Vector2(4 * S), origin + new Vector2(width + 4 * S, height + 4 * S), Theme.U32(Theme.PanelBg), 10f * S);
        dl.AddRect(origin - new Vector2(4 * S), origin + new Vector2(width + 4 * S, height + 4 * S), Theme.U32(Theme.Border), 10f * S);

        // Центр: эмблема профессии, средний iLvl
        var cx = origin.X + width / 2;
        var cy = origin.Y + height / 2;
        var emblem = 96 * S;
        dl.AddCircleFilled(new Vector2(cx, cy - 20 * S), emblem / 2 + 10 * S, Theme.U32(Theme.Alpha(Theme.Gold, 0.10f)));
        dl.AddCircle(new Vector2(cx, cy - 20 * S), emblem / 2 + 10 * S, Theme.U32(Theme.Alpha(Theme.Gold, 0.45f)), 0, 1.5f * S);
        Theme.GameIcon(62000 + job.RowId, new Vector2(cx - emblem / 2, cy - 20 * S - emblem / 2), emblem, 0f);

        var active = states.Where(s => !s.Disabled && s.Row is not null).ToList();
        var avg = active.Count == 0 ? 0 : (int)active.Average(s => (double)s.Ilvl);
        var label = $"iLvl {avg}";
        var ls = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(cx - ls.X / 2, cy + emblem / 2 - 4 * S), Theme.U32(Theme.Gold), label);
        var jn = GuideData.Capitalize(job.Name.ExtractText());
        var js = ImGui.CalcTextSize(jn);
        dl.AddText(new Vector2(cx - js.X / 2, cy + emblem / 2 + ls.Y - 2 * S), Theme.U32(Theme.TextDim), jn);

        for (var i = 0; i < 6; i++)
        {
            var y = origin.Y + i * (slot + gap);
            SlotBox(states.First(s => s.Index == LeftSlots[i].Index), new Vector2(origin.X, y), slot);
            SlotBox(states.First(s => s.Index == RightSlots[i].Index), new Vector2(origin.X + width - slot, y), slot);
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + height + 12 * S));
        ImGui.TextColored(Theme.Green, "■");
        ImGui.SameLine();
        ImGui.TextColored(Theme.TextDim, "лучшее");
        ImGui.SameLine();
        ImGui.TextColored(Theme.Yellow, "■");
        ImGui.SameLine();
        ImGui.TextColored(Theme.TextDim, "чуть хуже");
        ImGui.SameLine();
        ImGui.TextColored(Theme.Red, "■");
        ImGui.SameLine();
        ImGui.TextColored(Theme.TextDim, "стоит заменить");
    }

    private void SlotBox(SlotState s, Vector2 pos, float size)
    {
        var S = Theme.S;
        ImGui.SetCursorScreenPos(pos);
        if (ImGui.InvisibleButton($"##slot{s.Index}", new Vector2(size)))
            selectedIndex = s.Index;
        var hovered = ImGui.IsItemHovered();
        var selected = selectedIndex == s.Index;
        var dl = ImGui.GetWindowDrawList();
        var color = StatusColor(s);

        dl.AddRectFilled(pos, pos + new Vector2(size), Theme.U32(hovered ? Theme.CardHover : Theme.CardBg), 6f * S);
        if (s.Row is not null)
            Theme.GameIcon(s.Row.Value.Icon, pos + new Vector2(4 * S), size - 8 * S, 4f);
        else
        {
            var t = s.Disabled ? "—" : "+";
            var ts = ImGui.CalcTextSize(t);
            dl.AddText(pos + (new Vector2(size) - ts) / 2, Theme.U32(Theme.Gray), t);
        }

        dl.AddRect(pos, pos + new Vector2(size), Theme.U32(selected ? Theme.Gold : Theme.Alpha(color, 0.85f)), 6f * S, selected ? 3f * S : 2f * S);

        if (s.Row is not null)
        {
            var il = s.Ilvl.ToString();
            var ils = ImGui.CalcTextSize(il);
            var bp = pos + new Vector2(size - ils.X - 4 * S, size - ils.Y - 2 * S);
            dl.AddRectFilled(bp - new Vector2(2 * S, 0), bp + ils + new Vector2(2 * S, 0), Theme.U32(Theme.Alpha(Theme.Rgb(0, 0, 0), 0.7f)), 3f * S);
            dl.AddText(bp, Theme.U32(color), il);
        }

        if (hovered)
        {
            var tip = s.Disabled ? $"{s.Name}: не нужна (двуручное оружие)"
                : s.Row is null ? $"{s.Name}: пусто"
                : $"{s.Name}: {s.Row.Value.Name.ExtractText()}\niLvl {s.Ilvl}" + (s.BestIlvl > s.Ilvl ? $" · можно до {s.BestIlvl}" : " · лучшее доступное");
            ImGui.SetTooltip(tip);
        }
    }

    // ───────────── Детали слота ─────────────

    private void DrawSlotDetails(SlotState s, uint primary, string? region, string? worldName, string? dcName)
    {
        Theme.Panel($"gsel{s.Index}", () =>
        {
            Theme.LargeText(Theme.Gold, s.Name);
            if (s.Disabled)
            {
                ImGui.TextColored(Theme.TextDim, "Не нужна: в руках двуручное оружие.");
                return;
            }
            if (s.Row is null)
            {
                ImGui.TextColored(Theme.Red, "Слот пустой — надень что угодно подходящее.");
            }
            else
            {
                var row = s.Row.Value;
                ItemLine(row.Icon, row.Name.ExtractText(), $"iLvl {s.Ilvl}", Theme.TextDim);
                var stats = new List<string>();
                for (var i = 0; i < row.BaseParam.Count; i++)
                    if (row.BaseParam[i].RowId != 0 && row.BaseParamValue[i] != 0)
                        stats.Add($"{(GuideData.StatNames.TryGetValue(row.BaseParam[i].RowId, out var n) ? n : $"#{row.BaseParam[i].RowId}")} +{row.BaseParamValue[i]}");
                if (stats.Count > 0) ImGui.TextColored(Theme.TextDim, string.Join(", ", stats));
                if (s.MateriaSlots > 0)
                    ImGui.TextColored(s.Melded < s.MateriaSlots ? Theme.Yellow : Theme.Green, $"Материя: {s.Melded} из {s.MateriaSlots}");
                if (s.WrongStat)
                    ImGui.TextColored(Theme.Red, "Основной стат не подходит твоей профессии.");
            }
        });

        Theme.Panel("gfilters", () =>
        {
            Theme.SubHeader("Чем улучшить");
            Theme.Chips("glvl", LevelModeNames, ref levelMode);
            Theme.Chips("gsrc", SourceModeNames, ref sourceMode);
            Theme.Chips("gsort", SortModeNames, ref sortMode);
            var scopeNames = new[] { $"Мой мир: {worldName ?? "?"}", $"Датацентр: {dcName ?? "?"}" };
            Theme.Chips("gscope", scopeNames, ref scopeMode);
            ImGui.Checkbox("Скрыть то, что уже есть", ref hideOwned);

            if (s.Disabled) return;
            if (!cache.TryGetValue(s.Slot, out var candidates) || candidates.Count == 0)
            {
                ImGui.TextColored(Theme.Gray, sourceMode == 0 ? "Нет доступных вещей — попробуй «Все источники»" : "Нет подходящих вещей для этой профессии");
                return;
            }

            var shown = Pick(candidates, s.Ilvl, region, out var note);
            ImGui.Dummy(new Vector2(0, 2 * Theme.S));
            if (shown.Count == 0) ImGui.TextColored(Theme.TextDim, note);
            foreach (var g in shown)
                DrawCandidate(g, s.Ilvl, s.MainStat, primary, region);
        });
    }

    private List<GearEntry> Pick(List<GearEntry> candidates, uint equippedIlvl, string? region, out string note)
    {
        note = "";
        IEnumerable<GearEntry> list = candidates;
        if (hideOwned)
            list = list.Where(g => PlayerInfo.ItemCount(g.Id) == 0);

        if (sortMode == 0)
            return list.Take(ShownPerSlot).ToList();

        if (region is null)
        {
            note = "Не удалось определить датацентр.";
            return new List<GearEntry>();
        }

        var upgrades = list.Where(g => g.Marketable && g.ItemLevel > equippedIlvl).ToList();
        if (upgrades.Count == 0)
        {
            note = "На рынке нет вещей лучше надетого.";
            return upgrades;
        }

        var priced = upgrades
            .Select(g => (g, p: market.Get(region, g.Id)))
            .Where(x => x.p is not null)
            .OrderBy(x => x.p!.Price)
            .Select(x => x.g)
            .Take(ShownPerSlot)
            .ToList();

        if (priced.Count == 0)
            note = upgrades.Any(g => market.IsLoading(region, g.Id)) ? "Загружаю цены…" : "Сейчас никто не продаёт апгрейды для этого слота.";
        return priced;
    }

    private void DrawCandidate(GearEntry g, uint equippedIlvl, int equippedMain, uint primary, string? region)
    {
        ImGui.PushID((int)g.Id);
        var delta = (int)g.ItemLevel - (int)equippedIlvl;
        var owned = PlayerInfo.ItemCount(g.Id);

        Theme.FaIcon(FontAwesomeIcon.ArrowRight, delta > 0 ? Theme.Green : Theme.Gray);
        ImGui.SameLine();
        ItemLine(g.Icon, g.Name, $"iLvl {g.ItemLevel} · ур. {g.EquipLevel}", delta > 0 ? Theme.Green : Theme.TextDim);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(StatsText(g));
        if (delta > 0) { ImGui.SameLine(); Theme.InlineBadge($"+{delta}", Theme.Green); }
        var mainDelta = g.Stats.Where(s => s.Param == primary).Select(s => s.Value).FirstOrDefault() - equippedMain;
        if (mainDelta != 0 && GuideData.StatNames.TryGetValue(primary, out var statName))
        {
            ImGui.SameLine();
            Theme.InlineBadge($"{(mainDelta > 0 ? "+" : "")}{mainDelta} {statName}", mainDelta > 0 ? Theme.Green : Theme.Red);
        }
        if (owned > 0) { ImGui.SameLine(); Theme.InlineBadge("есть у тебя", Theme.Blue); }

        // источник
        var sources = data.Sources.Get(g.Id, g.Marketable).Where(x => x.Kind != SourceKind.Market).Take(2).ToList();
        if (sources.Count > 0)
        {
            ImGui.Dummy(new Vector2(22 * Theme.S, 0));
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextDim, string.Join("  ·  ", sources.Select(x => x.Text)));
            var vendor = sources.FirstOrDefault(x => x.LevelId != 0);
            if (vendor is not null)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Флаг на торговца"))
                    ItemSources.FlagLevel(vendor.LevelId);
            }
        }

        // вторая строка: рынок
        ImGui.Dummy(new Vector2(22 * Theme.S, 0));
        ImGui.SameLine();
        DrawPrice(g, region);

        ImGui.SameLine();
        var justCopied = copiedId == g.Id && DateTime.Now - copiedAt < TimeSpan.FromSeconds(2);
        if (ImGui.SmallButton(justCopied ? "Скопировано ✓" : "Копировать название"))
        {
            ImGui.SetClipboardText(g.Name);
            copiedId = g.Id;
            copiedAt = DateTime.Now;
        }
        Theme.Tooltip("Подойди к маркетборду и вставь в поиск (Ctrl+V)");
        ImGui.SameLine();
        if (ImGui.SmallButton("Где достать"))
            Util.OpenLink($"https://www.garlandtools.org/db/#item/{g.Id}");
        ImGui.SameLine();
        if (ImGui.SmallButton("Universalis"))
            Util.OpenLink($"https://universalis.app/market/{g.Id}");

        ImGui.Dummy(new Vector2(0, 2 * Theme.S));
        ImGui.PopID();
    }

    private void DrawPrice(GearEntry g, string? region)
    {
        if (!g.Marketable)
        {
            Theme.InlineBadge("не продаётся", Theme.Gray);
            return;
        }

        if (region is null)
        {
            Theme.InlineBadge("на рынке", Theme.Green);
            return;
        }

        var price = market.Get(region, g.Id);
        if (price is not null)
        {
            var where = scopeMode == 0 ? "" : $" · {price.World}";
            Theme.InlineBadge($"от {price.Price:N0} гил{(price.Hq ? " (HQ)" : "")}{where}", Theme.Gold);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Лотов ({(scopeMode == 0 ? "на твоём мире" : "в датацентре")}): {price.Listings}\nОбновлено: {price.FetchedAt.ToLocalTime():HH:mm}");
        }
        else if (market.IsLoading(region, g.Id) || !market.IsKnown(region, g.Id))
        {
            Theme.InlineBadge("цена…", Theme.TextDim);
        }
        else
        {
            Theme.InlineBadge("нет лотов", Theme.Gray);
        }
    }

    private static void ItemLine(uint icon, string name, string info, Vector4 infoColor)
    {
        var h = ImGui.GetTextLineHeight() * 1.3f;
        var pos = ImGui.GetCursorScreenPos();
        Theme.GameIcon(icon, pos, h, 3f);
        ImGui.Dummy(new Vector2(h, h));
        ImGui.SameLine();
        ImGui.TextColored(Theme.Text, name);
        ImGui.SameLine();
        ImGui.TextColored(infoColor, info);
    }

    private Dictionary<GearSlot, List<GearEntry>> Build(ClassJob job, string abbr, int targetLevel)
    {
        var primary = job.PrimaryStat;
        int MainStat(GearEntry g) => g.Stats.Where(s => s.Param == primary).Select(s => s.Value).FirstOrDefault();

        return data.Gear
            .Where(g => !g.Pvp && g.EquipLevel <= targetLevel && data.JobInCategory(g.ClassJobCategory, abbr))
            .Where(g => sourceMode == 1 || data.Sources.IsEasy(g.Id, g.Marketable))
            .GroupBy(g => g.Slot)
            .ToDictionary(
                grp => grp.Key,
                grp => grp.OrderByDescending(g => g.ItemLevel)
                          .ThenByDescending(MainStat)
                          .ThenByDescending(g => g.EquipLevel)
                          .DistinctBy(g => g.Name)
                          .Take(CandidatesPerSlot)
                          .ToList());
    }

    private static int MainStatOf(Item item, uint primary)
    {
        for (var i = 0; i < item.BaseParam.Count; i++)
            if (item.BaseParam[i].RowId == primary)
                return item.BaseParamValue[i];
        return 0;
    }

    private static bool IsTwoHanded(uint mainHandId)
    {
        if (mainHandId == 0) return false;
        var item = Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(mainHandId);
        return item?.EquipSlotCategory.ValueNullable?.OffHand == -1;
    }

    private static string StatsText(GearEntry g) =>
        g.Stats.Length == 0
            ? "Без статов"
            : string.Join("\n", g.Stats.Select(s =>
                $"{(GuideData.StatNames.TryGetValue(s.Param, out var n) ? n : $"#{s.Param}")}: +{s.Value}"));
}
