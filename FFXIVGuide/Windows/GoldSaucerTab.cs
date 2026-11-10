using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Gold Saucer: подсказчик Mini Cactpot и справочник Triple Triad. Только подсказывает.</summary>
internal sealed unsafe class GoldSaucerTab
{
    private static readonly string[] Sections = { "Mini Cactpot", "Fashion Report", "Карты Triple Triad", "Противники Triple Triad" };
    private int section;

    public void Draw()
    {
        Theme.Header("Gold Saucer", "Mini Cactpot: какую клетку открыть и какую линию выбрать. Triple Triad: карты и у кого их выиграть.");
        Theme.Chips("gssec", Sections, ref section);
        switch (section)
        {
            case 0: DrawCactpot(); break;
            case 1: DrawFashion(); break;
            case 2: DrawCards(); break;
            case 3: DrawNpcs(); break;
        }
    }

    // ═════════════ Mini Cactpot ═════════════

    private static readonly Dictionary<int, int> Payout = new()
    {
        [6] = 10000, [7] = 36, [8] = 720, [9] = 360, [10] = 80, [11] = 252, [12] = 108, [13] = 72, [14] = 54,
        [15] = 180, [16] = 72, [17] = 180, [18] = 119, [19] = 36, [20] = 306, [21] = 1080, [22] = 144, [23] = 1800, [24] = 3600,
    };

    private static readonly int[][] Lines =
    {
        new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8 },
        new[] { 0, 3, 6 }, new[] { 1, 4, 7 }, new[] { 2, 5, 8 },
        new[] { 0, 4, 8 }, new[] { 2, 4, 6 },
    };

    private static readonly string[] LineNames =
    {
        "верхняя строка", "средняя строка", "нижняя строка",
        "левый столбец", "средний столбец", "правый столбец",
        "диагональ ↘", "диагональ ↙",
    };

    private readonly int[] board = new int[9];
    private int editCell = -1;
    private string solvedFor = "";
    private Task<(double[] LineEv, double[] CellEv)>? solving;
    private (double[] LineEv, double[] CellEv)? result;

    private static double LineEv(int[] b, int[] line, List<int> remaining)
    {
        var known = 0;
        var unknown = 0;
        foreach (var c in line)
            if (b[c] == 0) unknown++; else known += b[c];
        if (unknown == 0) return Payout[known];
        double sum = 0;
        var count = 0;
        // Перебор упорядоченных выборок недостающих чисел
        void Rec(int depth, int acc, int used)
        {
            if (depth == unknown)
            {
                sum += Payout[acc];
                count++;
                return;
            }
            for (var i = 0; i < remaining.Count; i++)
            {
                if ((used & (1 << i)) != 0) continue;
                Rec(depth + 1, acc + remaining[i], used | (1 << i));
            }
        }
        Rec(0, known, 0);
        return count == 0 ? 0 : sum / count;
    }

    private static List<int> Remaining(int[] b) => Enumerable.Range(1, 9).Where(n => !b.Contains(n)).ToList();

    private static double BestLine(int[] b)
    {
        var rem = Remaining(b);
        return Lines.Max(l => LineEv(b, l, rem));
    }

    /// <summary>Ожидаемый выигрыш при оптимальной игре, если осталось открыть reveals клеток.</summary>
    private static double Value(int[] b, int reveals, ConcurrentDictionary<long, double> memo)
    {
        if (reveals == 0) return BestLine(b);
        long key = reveals;
        foreach (var v in b) key = key * 10 + v;
        if (memo.TryGetValue(key, out var cached)) return cached;
        var rem = Remaining(b);
        double best = 0;
        for (var c = 0; c < 9; c++)
        {
            if (b[c] != 0) continue;
            double s = 0;
            foreach (var v in rem)
            {
                b[c] = v;
                s += Value(b, reveals - 1, memo);
                b[c] = 0;
            }
            best = Math.Max(best, s / rem.Count);
        }
        memo[key] = best;
        return best;
    }

    private static (double[] LineEv, double[] CellEv) Solve(int[] input)
    {
        var b = (int[])input.Clone();
        var revealed = b.Count(v => v != 0);
        var rem = Remaining(b);
        var lineEv = Lines.Select(l => LineEv(b, l, rem)).ToArray();
        var cellEv = new double[9];
        if (revealed is >= 1 and < 4)
        {
            var memo = new ConcurrentDictionary<long, double>();
            var left = 4 - revealed;
            for (var c = 0; c < 9; c++)
            {
                if (b[c] != 0) { cellEv[c] = -1; continue; }
                double s = 0;
                foreach (var v in rem)
                {
                    b[c] = v;
                    s += Value(b, left - 1, memo);
                    b[c] = 0;
                }
                cellEv[c] = s / rem.Count;
            }
        }
        return (lineEv, cellEv);
    }

    // ───────────── Fashion Report ─────────────

    private static unsafe void DrawFashion()
    {
        // Неделя: вторник 08:00 UTC — новая тема; пятница 08:00 UTC — начало оценки
        var reset = RaidTier.LastReset;
        var judging = reset.AddDays(3);
        var now = DateTime.UtcNow;
        Theme.Panel("frtime", () =>
        {
            Theme.SubHeader("Расписание");
            if (now < judging)
                ImGui.TextColored(Theme.Text, $"Сейчас предпросмотр темы. Оценка откроется {judging.ToLocalTime():dd.MM HH:mm} (пятница) и продлится до {RaidTier.NextReset.ToLocalTime():dd.MM HH:mm}.");
            else
                ImGui.TextColored(Theme.Green, $"Идёт оценка — до {RaidTier.NextReset.ToLocalTime():dd.MM HH:mm}. 80+ баллов — 60 000 MGP, просто участие — 10 000 MGP.");
            ImGui.TextColored(Theme.Gray, "Masked Rose стоит в Gold Saucer у Event Square. Ответы недели сообщество (Kaiyoko) публикует на Reddit в пятницу.");
        });

        var fm = FFXIVClientStructs.FFXIV.Client.Game.FashionCheckManager.Instance();
        if (fm == null || !PlayerInfo.Ready) return;
        var themeId = fm->Theme.WeeklyTheme;
        var themes = Plugin.Data.GetExcelSheet<FashionCheckWeeklyTheme>();
        var cats = Plugin.Data.GetExcelSheet<FashionCheckThemeCategory>();
        Theme.Panel("frtheme", () =>
        {
            Theme.SubHeader("Тема этой недели");
            var name = themeId == 0 ? "" : themes.GetRowOrDefault(themeId)?.Name.ExtractText() ?? "";
            if (name.Length == 0)
            {
                ImGui.TextColored(Theme.TextDim, "Тема неизвестна — поговори с Masked Rose в Gold Saucer, и плагин покажет тему и подсказки по слотам.");
                return;
            }
            Theme.LargeText(Theme.Gold, name);
            var items = fm->HighScore.ItemIds.ToArray();
            var hints = fm->Theme.ItemThemes.ToArray();
            var shown = 0;
            for (var i = 0; i < hints.Length; i++)
            {
                if (hints[i] == 0) continue;
                var hint = cats.GetRowOrDefault(hints[i])?.Name.ExtractText() ?? "";
                if (hint.Length == 0) continue;
                shown++;
                var item = i < items.Length ? items[i] : 0u;
                var slot = item != 0 ? SlotName(item) : $"Слот {i + 1}";
                ImGui.TextColored(Theme.Text, $"{slot}: ");
                ImGui.SameLine();
                ImGui.TextColored(Theme.Gold, hint);
            }
            if (shown == 0) ImGui.TextColored(Theme.TextDim, "Подсказки по слотам игра показывает после первой оценки на этой неделе.");
        });

        Theme.Panel("frbest", () =>
        {
            Theme.SubHeader($"Твой лучший результат этой недели: {fm->HighScore.Score} баллов");
            var items = fm->HighScore.ItemIds.ToArray();
            var any = false;
            for (var i = 0; i < items.Length; i++)
            {
                if (items[i] == 0) continue;
                any = true;
                WikiView.ItemLine(items[i] % 1000000, 0, SlotName(items[i]), $"fr{i}");
            }
            if (!any) ImGui.TextColored(Theme.TextDim, "Пока нет оценки на этой неделе.");
        });
    }

    private static string SlotName(uint item)
    {
        var it = Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(item % 1000000);
        if (it?.EquipSlotCategory.ValueNullable is not { } c) return "Слот";
        return GuideData.SlotOf(c) switch
        {
            GearSlot.MainHand => "Оружие", GearSlot.OffHand => "Вторая рука", GearSlot.Head => "Голова", GearSlot.Body => "Тело",
            GearSlot.Hands => "Руки", GearSlot.Legs => "Ноги", GearSlot.Feet => "Обувь", GearSlot.Ears => "Серьги",
            GearSlot.Neck => "Ожерелье", GearSlot.Wrists => "Браслеты", GearSlot.Ring => "Кольцо", _ => "Слот",
        };
    }

    private void DrawCactpot()
    {
        var revealed = board.Count(v => v != 0);
        var key = string.Join(",", board);
        if (key != solvedFor && revealed >= 1 && board.Distinct().Count(v => v != 0) == revealed)
        {
            solvedFor = key;
            var copy = (int[])board.Clone();
            result = null;
            solving = Task.Run(() => Solve(copy));
        }
        if (solving is { IsCompleted: true }) { result = solving.Result; solving = null; }

        Theme.Panel("mcp", () =>
        {
            ImGui.TextColored(Theme.TextDim, "Нажми на клетку и выбери цифру — как в игре. Одна цифра открыта сразу, потом открываешь ещё 3.");
            if (revealed != board.Distinct().Count(v => v != 0))
                ImGui.TextColored(Theme.Red, "Цифры не должны повторяться.");

            var size = 64 * Theme.S;
            var gap = 8 * Theme.S;
            var start = ImGui.GetCursorScreenPos() + new Vector2(30 * Theme.S, 30 * Theme.S);
            var dl = ImGui.GetWindowDrawList();
            var bestCell = -1;
            var bestLine = -1;
            if (result is { } r)
            {
                if (revealed < 4 && r.CellEv.Any(x => x > 0)) bestCell = Array.IndexOf(r.CellEv, r.CellEv.Max());
                if (revealed >= 4) bestLine = Array.IndexOf(r.LineEv, r.LineEv.Max());
            }

            for (var c = 0; c < 9; c++)
            {
                var pos = start + new Vector2(c % 3 * (size + gap), c / 3 * (size + gap));
                ImGui.SetCursorScreenPos(pos);
                if (ImGui.InvisibleButton($"##cell{c}", new Vector2(size)))
                {
                    editCell = c;
                    ImGui.OpenPopup("##cpnum");
                }
                var hovered = ImGui.IsItemHovered();
                var inLine = bestLine >= 0 && Lines[bestLine].Contains(c);
                var bg = c == bestCell ? Theme.Alpha(Theme.Green, 0.35f) : inLine ? Theme.Alpha(Theme.Gold, 0.35f) : hovered ? Theme.CardHover : Theme.CardBg;
                dl.AddRectFilled(pos, pos + new Vector2(size), Theme.U32(bg), 10f * Theme.S);
                dl.AddRect(pos, pos + new Vector2(size), Theme.U32(c == bestCell ? Theme.Green : inLine ? Theme.Gold : Theme.Border), 10f * Theme.S, ImDrawFlags.None, 2f * Theme.S);
                var text = board[c] == 0 ? (result is { } rr && rr.CellEv[c] > 0 && revealed < 4 ? $"{rr.CellEv[c]:0}" : "") : board[c].ToString();
                if (text.Length > 0)
                {
                    if (board[c] != 0) Theme.LargeTextAt(pos + new Vector2(size / 2, size / 2), text, Theme.Text);
                    else
                    {
                        var ts = ImGui.CalcTextSize(text);
                        dl.AddText(pos + (new Vector2(size) - ts) / 2, Theme.U32(Theme.TextDim), text);
                    }
                }
            }

            // Стрелки линий с ожидаемым выигрышем
            if (result is { } res)
            {
                void Arrow(int line, Vector2 p, string glyph)
                {
                    var ev = res.LineEv[line];
                    var best = line == bestLine;
                    var t = $"{glyph}{ev:0}";
                    var ts = ImGui.CalcTextSize(t);
                    dl.AddText(p - ts / 2, Theme.U32(best ? Theme.Gold : Theme.TextDim), t);
                }
                var grid = 3 * size + 2 * gap;
                for (var i = 0; i < 3; i++)
                {
                    Arrow(i, start + new Vector2(-18 * Theme.S, i * (size + gap) + size / 2), "→");
                    Arrow(3 + i, start + new Vector2(i * (size + gap) + size / 2, -16 * Theme.S), "↓");
                }
                Arrow(6, start + new Vector2(-18 * Theme.S, -16 * Theme.S), "↘");
                Arrow(7, start + new Vector2(grid + 18 * Theme.S, -16 * Theme.S), "↙");
            }

            if (ImGui.BeginPopup("##cpnum"))
            {
                for (var n = 1; n <= 9; n++)
                {
                    if (n > 1 && (n - 1) % 3 != 0) ImGui.SameLine();
                    var used = board.Contains(n) && board[editCell] != n;
                    ImGui.BeginDisabled(used);
                    if (ImGui.Button($"{n}##n{n}", new Vector2(36 * Theme.S))) { board[editCell] = n; ImGui.CloseCurrentPopup(); }
                    ImGui.EndDisabled();
                }
                if (ImGui.Button("Очистить", new Vector2(116 * Theme.S, 0))) { board[editCell] = 0; ImGui.CloseCurrentPopup(); }
                ImGui.EndPopup();
            }

            ImGui.SetCursorScreenPos(start + new Vector2(0, 3 * size + 2 * gap + 12 * Theme.S));
            if (revealed == 0) ImGui.TextColored(Theme.TextDim, "Введи цифру, которая открыта на билете.");
            else if (solving is not null) ImGui.TextColored(Theme.TextDim, "Считаю…");
            else if (result is { } r2)
            {
                if (revealed < 4)
                {
                    var c = Array.IndexOf(r2.CellEv, r2.CellEv.Max());
                    ImGui.TextColored(Theme.Green, $"Открой клетку, подсвеченную зелёным (ожидаемый выигрыш ~{r2.CellEv[c]:0} MGP). Осталось открыть: {4 - revealed}.");
                    ImGui.TextColored(Theme.Gray, "Цифры в пустых клетках — ожидаемый выигрыш, если открыть эту клетку.");
                }
                else
                {
                    var l = Array.IndexOf(r2.LineEv, r2.LineEv.Max());
                    ImGui.TextColored(Theme.Gold, $"Выбери {LineNames[l]}: в среднем ~{r2.LineEv[l]:0} MGP.");
                }
            }
            if (Theme.IconButton("cpreset", FontAwesomeIcon.Redo, "Новый билет"))
            {
                Array.Clear(board);
                result = null;
                solvedFor = "";
            }
        });
    }

    // ═════════════ Triple Triad ═════════════

    private sealed record Card(uint Id, int Number, string Name, int Stars, string Sides, string Type, uint Icon, uint ItemId, int SaleMgp);

    private sealed record TtNpc(uint Id, uint NpcId, string Name, string Place, uint LevelId, List<string> Rules, List<uint> RewardCards, int Fee);

    private List<Card>? cards;
    private List<TtNpc>? npcs;
    private Task? building;
    private string cardSearch = "";
    private int cardFilter;
    private static readonly string[] CardFilters = { "Все", "Нет у меня", "Есть" };
    private string npcSearch = "";
    private bool npcOnlyMissing;

    private static bool Owned(uint cardId) => PlayerInfo.Ready && UIState.Instance()->IsTripleTriadCardUnlocked((ushort)cardId);

    private static string Side(byte v) => v == 10 ? "A" : v.ToString();

    private void Build()
    {
        if (building is not null) return;
        building = Task.Run(() =>
        {
            try
            {
                // Предмет-карта → id карты
                var cardItem = new Dictionary<uint, uint>();
                foreach (var it in Plugin.Data.GetExcelSheet<Item>())
                    if (it.ItemAction.ValueNullable is { } ia && ia.Action.RowId == 3357 && ia.Data[0] != 0)
                        cardItem.TryAdd(ia.Data[0], it.RowId);

                var res = Plugin.Data.GetExcelSheet<TripleTriadCardResident>();
                var list = new List<Card>();
                foreach (var c in Plugin.Data.GetExcelSheet<TripleTriadCard>())
                {
                    var name = c.Name.ExtractText();
                    if (name.Length == 0) continue;
                    var r = res.GetRowOrDefault(c.RowId);
                    if (r is null) continue;
                    var rr = r.Value;
                    var item = cardItem.GetValueOrDefault(c.RowId);
                    list.Add(new Card(c.RowId, rr.Order, name, rr.TripleTriadCardRarity.ValueNullable?.Stars ?? 0,
                        $"{Side(rr.Top)}/{Side(rr.Right)}/{Side(rr.Bottom)}/{Side(rr.Left)}",
                        rr.TripleTriadCardType.ValueNullable?.Name.ExtractText() ?? "",
                        item != 0 ? WikiView.ItemInfo(item).Icon : 0, item, rr.SaleValue));
                }

                // Противники: NPC, у которых есть событие Triple Triad (0x23xxxx)
                var tt = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.TripleTriad>();
                var names = Plugin.Data.GetExcelSheet<ENpcResident>();
                var npcLevel = new Dictionary<uint, Level>();
                foreach (var lv in Plugin.Data.GetExcelSheet<Level>())
                    if (lv.Object.RowId is >= 1000000 and < 2000000) npcLevel.TryAdd(lv.Object.RowId, lv);
                var found = new List<TtNpc>();
                foreach (var b in Plugin.Data.GetExcelSheet<ENpcBase>())
                {
                    foreach (var d in b.ENpcData)
                    {
                        var ev = d.RowId;
                        if (ev >> 16 != 0x23) continue;
                        var t = tt.GetRowOrDefault(ev);
                        if (t is null) continue;
                        var name = names.GetRowOrDefault(b.RowId)?.Singular.ExtractText() ?? "";
                        if (name.Length == 0) continue;
                        var place = "";
                        uint levelId = 0;
                        if (npcLevel.TryGetValue(b.RowId, out var lv2))
                        {
                            place = lv2.Territory.ValueNullable?.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
                            levelId = lv2.RowId;
                        }
                        var rules = t.Value.TripleTriadRule.Where(x => x.RowId != 0).Select(x => x.ValueNullable?.Name.ExtractText() ?? "").Where(x => x.Length > 0).ToList();
                        var rewards = t.Value.ItemPossibleReward.Where(x => x.RowId != 0)
                            .Select(x => x.ValueNullable?.ItemAction.ValueNullable is { } ia && ia.Action.RowId == 3357 ? (uint)ia.Data[0] : 0u)
                            .Where(x => x != 0).Distinct().ToList();
                        found.Add(new TtNpc(ev, b.RowId, GuideData.Capitalize(name), place, levelId, rules, rewards, t.Value.Fee));
                        break;
                    }
                }
                cards = list.OrderBy(c => c.Number).ToList();
                npcs = found.GroupBy(n => n.Id).Select(g => g.First()).OrderBy(n => n.Place).ThenBy(n => n.Name).ToList();
            }
            catch (Exception e)
            {
                Plugin.Log.Warning(e, "Triple Triad: не удалось собрать данные");
                cards ??= new List<Card>();
                npcs ??= new List<TtNpc>();
            }
        });
    }

    private void DrawCards()
    {
        Build();
        if (cards is null || npcs is null) { Theme.Panel("ttload", () => ImGui.TextColored(Theme.TextDim, "Собираю карты и противников из файлов игры…")); return; }

        var owned = cards.Count(c => Owned(c.Id));
        if (PlayerInfo.Ready) Theme.Progress("Собрано карт", owned, cards.Count, Theme.Gold);
        Theme.Search("##ttsearch", "Карта…", ref cardSearch);
        Theme.Chips("ttfilter", CardFilters, ref cardFilter);
        var term = cardSearch.Trim();
        var rows = cards.Where(c => (term.Length == 0 || NameTranslations.Matches(c.Name, term))
                                    && (cardFilter == 0 || Owned(c.Id) == (cardFilter == 2))).ToList();
        var byCard = npcs.SelectMany(n => n.RewardCards.Select(c => (c, n))).ToLookup(x => x.c, x => x.n);

        var rowH = 46 * Theme.S;
        if (!ImGui.BeginChild("##ttcards", Vector2.Zero)) { ImGui.EndChild(); return; }
        Theme.VirtualList(rows.Count, rowH, i =>
        {
            var c = rows[i];
            var has = Owned(c.Id);
            var from = byCard[c.Id].ToList();
            var sub = $"{new string('★', c.Stars)} · {c.Sides}" + (c.Type.Length > 0 ? $" · {c.Type}" : "") +
                      (from.Count > 0 ? $" · выиграть: {string.Join(", ", from.Take(2).Select(n => n.Name))}{(from.Count > 2 ? $" и ещё {from.Count - 2}" : "")}" : "");
            if (Theme.Card($"##ttc{c.Id}", c.Icon, $"№{c.Number} {NameTranslations.Show(c.Name)}", sub, has ? "есть" : "нет", has ? Theme.Green : Theme.Gray, false, rowH) && c.ItemId != 0)
                WikiNav.Open(WikiKind.Item, c.ItemId, c.Name);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip((from.Count > 0 ? "Выигрывается у:\n" + string.Join("\n", from.Select(n => $"• {n.Name} ({n.Place})")) + "\n\n" : "") +
                                 (c.SaleMgp > 0 ? $"Продать: {c.SaleMgp} MGP\n" : "") + "Нажми — карточка предмета (все источники)");
        });
        ImGui.EndChild();
    }

    private void DrawNpcs()
    {
        Build();
        if (cards is null || npcs is null) { Theme.Panel("ttload2", () => ImGui.TextColored(Theme.TextDim, "Собираю противников из файлов игры…")); return; }
        Theme.Search("##ttnsearch", "Имя или зона…", ref npcSearch);
        ImGui.Checkbox("Только те, у кого есть недостающие карты", ref npcOnlyMissing);
        var term = npcSearch.Trim();
        var cardById = cards.ToDictionary(c => c.Id);
        var rows = npcs.Where(n => (term.Length == 0 || NameTranslations.Matches(n.Name, term) || NameTranslations.Matches(n.Place, term))
                                   && (!npcOnlyMissing || n.RewardCards.Any(c => !Owned(c)))).ToList();
        if (!ImGui.BeginChild("##ttnpcs", Vector2.Zero)) { ImGui.EndChild(); return; }
        Theme.Grid("ttgrid", rows.Count, 380f, i =>
        {
            var n = rows[i];
            Theme.Panel($"ttn{n.Id}", () =>
            {
                ImGui.TextColored(Theme.Gold, NameTranslations.Show(n.Name));
                ImGui.SameLine();
                ImGui.TextColored(Theme.TextDim, n.Place);
                if (n.LevelId != 0)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Флаг##tf{n.Id}")) ItemSources.FlagLevel(n.LevelId);
                }
                if (n.Rules.Count > 0) ImGui.TextColored(Theme.TextDim, "Правила: " + string.Join(", ", n.Rules));
                if (n.Fee > 0) ImGui.TextColored(Theme.TextDim, $"Взнос: {n.Fee} MGP");
                var missing = n.RewardCards.Count(c => !Owned(c));
                ImGui.TextColored(missing > 0 ? Theme.Text : Theme.Green, n.RewardCards.Count == 0 ? "Карт в награду нет" : missing > 0 ? $"Карты в награду (нет {missing}):" : "Все карты собраны ✓");
                foreach (var cid in n.RewardCards)
                {
                    if (!cardById.TryGetValue(cid, out var c)) continue;
                    var has = Owned(cid);
                    Theme.FaIcon(has ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.Circle, has ? Theme.Green : Theme.Gray);
                    ImGui.SameLine();
                    ImGui.TextColored(has ? Theme.TextDim : Theme.Text, $"№{c.Number} {NameTranslations.Show(c.Name)} {new string('★', c.Stars)}");
                }
            });
        });
        ImGui.EndChild();
    }
}
