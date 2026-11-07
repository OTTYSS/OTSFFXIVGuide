using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

internal sealed class DutiesTab
{
    private readonly GuideData data;
    private readonly QuestsTab quests;
    private readonly Action<MainTab> go;
    private string search = "";
    private int typeIdx;
    private int statusFilter; // 0 все, 1 закрытые, 2 открытые, 3 не пройденные
    private bool highEndOnly;
    private DutyEntry? selected;
    private bool scrollToSelected;
    private string[]? types;
    private readonly MechanicsView mechanicsView = new();

    private static readonly string[] StatusNames = { "Все", "Закрыты", "Открыты", "Не пройдены" };

    public DutiesTab(GuideData data, QuestsTab quests, Action<MainTab> go)
    {
        this.data = data;
        this.quests = quests;
        this.go = go;
    }

    // Пришли из «Обзора»: показываем только те же данжи, что считаются там (с квестом открытия)
    private bool onlyTracked;

    /// <summary>Открыть список оставшегося: 1 — ещё не открытые, 3 — открытые, но не пройденные.</summary>
    public void ShowRemaining(int status)
    {
        search = "";
        typeIdx = 0;
        statusFilter = status;
        highEndOnly = false;
        onlyTracked = true;
        selected = null;
        go(MainTab.Duties);
    }

    public void Show(DutyEntry d)
    {
        search = "";
        typeIdx = 0;
        statusFilter = 0;
        highEndOnly = false;
        selected = d;
        scrollToSelected = true;
        go(MainTab.Duties);
    }

    public void Draw()
    {
        types ??= new[] { "Все типы" }.Concat(data.Duties.Select(d => d.Type).Distinct().OrderBy(t => t)).ToArray();

        Theme.Header("Данжи и бои", "Как открыть каждый данж, триал и рейд");
        Theme.Search("##dsearch", "Название данжа или боя…", ref search);

        Theme.Chips("dstatus", StatusNames, ref statusFilter);
        ImGui.SameLine(0, 16 * Theme.S);
        ImGui.Checkbox("Сложный контент", ref highEndOnly);
        ImGui.SameLine(0, 16 * Theme.S);
        ImGui.SetNextItemWidth(Math.Max(160 * Theme.S, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("##dtype", types[typeIdx]))
        {
            for (var i = 0; i < types.Length; i++)
                if (ImGui.Selectable(types[i], typeIdx == i)) typeIdx = i;
            ImGui.EndCombo();
        }

        if (onlyTracked)
        {
            Theme.InlineBadge("Из «Обзора»: только данжи и бои с квестом открытия", Theme.Blue);
            ImGui.SameLine();
            if (ImGui.SmallButton("Показать все")) onlyTracked = false;
        }

        var rows = Filter().ToList();
        ImGui.TextColored(Theme.TextDim, $"Найдено: {rows.Count}");

        var avail = ImGui.GetContentRegionAvail();
        var rowH = 46 * Theme.S;
        if (ImGui.BeginChild("##dlist", new Vector2(Theme.SplitWidth("duties", avail.X, 0.5f), 0)))
        {
            if (scrollToSelected && selected is not null)
            {
                var idx = rows.IndexOf(selected);
                if (idx >= 0) ImGui.SetScrollY(idx * (rowH + ImGui.GetStyle().ItemSpacing.Y));
                scrollToSelected = false;
            }

            Theme.VirtualList(rows.Count, rowH, i =>
            {
                var d = rows[i];
                var (badge, color) = StatusBadge(d);
                var sub = $"Ур. {d.Level}" + (d.ItemLevel > 0 ? $" · iLvl {d.ItemLevel}" : "") + $" · {d.Type}";
                if (Theme.Card($"##d{d.CfcId}", d.Icon, d.Name, sub, badge, color, selected == d, rowH))
                    WikiNav.Open(WikiKind.Duty, d.CfcId, d.Name);
            });
        }
        ImGui.EndChild();

        Theme.Splitter("duties", avail.X, 0.5f);
        if (ImGui.BeginChild("##ddetails", Vector2.Zero))
        {
            if (selected is null)
            {
                Theme.Panel("dph", () =>
                {
                    Theme.FaIcon(FontAwesomeIcon.Dungeon, Theme.GoldDim);
                    ImGui.TextColored(Theme.TextDim, "Выбери данж слева: покажу, каким квестом он открывается и что сделать прямо сейчас.");
                });
            }
            else
            {
                WikiNav.Visit(new WikiLink(WikiKind.Duty, selected.CfcId, selected.Name));
                DrawDetails(selected);
            }
        }
        ImGui.EndChild();
    }

    private IEnumerable<DutyEntry> Filter()
    {
        var type = typeIdx == 0 ? "" : types![typeIdx];
        foreach (var d in data.Duties)
        {
            if (search.Length > 0 && !NameTranslations.Matches(d.Name, search)) continue;
            if (type != "" && d.Type != type) continue;
            if (highEndOnly && !d.HighEnd) continue;
            if (onlyTracked && (d.UnlockQuestId == 0 || d.InstanceContentId == 0)) continue;
            if (statusFilter != 0 && PlayerInfo.Ready)
            {
                if (d.InstanceContentId == 0) continue;
                var unlocked = PlayerInfo.IsDutyUnlocked(d);
                if (statusFilter == 1 && unlocked) continue;
                if (statusFilter == 2 && !unlocked) continue;
                if (statusFilter == 3 && (!unlocked || PlayerInfo.IsDutyCompleted(d))) continue;
            }
            yield return d;
        }
    }

    public static (string?, Vector4) StatusBadge(DutyEntry d)
    {
        if (!PlayerInfo.Ready || d.InstanceContentId == 0) return (null, Theme.Gray);
        if (PlayerInfo.IsDutyCompleted(d)) return ("пройден", Theme.Green);
        if (PlayerInfo.IsDutyUnlocked(d)) return ("открыт", Theme.Yellow);
        return ("закрыт", Theme.Gray);
    }

    public void ShowByCfc(uint cfc)
    {
        var d = data.Duties.FirstOrDefault(x => x.CfcId == cfc);
        if (d is not null) Show(d);
    }

    /// <summary>Открыть данж по InstanceContent id (ссылки из вики).</summary>
    public void ShowByInstance(uint instanceId)
    {
        var d = data.Duties.FirstOrDefault(x => x.InstanceContentId == instanceId);
        if (d is not null) Show(d);
    }

    private static string FightName(string type, int bossNo, int midNo) => type switch
    {
        "Boss" => "Финальный босс",
        "MidBoss" => $"Босс {midNo}",
        _ => type,
    };

    private void DrawLoot(DutyEntry d)
    {
        if (d.InstanceContentId == 0) return;
        Theme.Panel("dloot", () =>
        {
            Theme.LargeText(Theme.Gold, "Лут");
            var inst = WikiService.Instance.Duty(d.InstanceContentId);
            if (!WikiView.Status(inst, "instance", d.InstanceContentId)) return;
            var w = inst!;

            var bosses = MechanicsDb.Find(d.Name)?.Bosses.Select(b => b.Name).ToList() ?? new List<string>();
            var withLoot = w.Fights.Where(f => f.Items.Count > 0).ToList();
            if (withLoot.Count > 0)
            {
                var mid = 0;
                for (var i = 0; i < withLoot.Count; i++)
                {
                    var f = withLoot[i];
                    if (f.Type == "MidBoss") mid++;
                    var title = i < bosses.Count && bosses.Count == withLoot.Count ? bosses[i] : FightName(f.Type, i + 1, mid);
                    Theme.FaIcon(FontAwesomeIcon.Skull, f.Type == "Boss" ? Theme.Red : Theme.GoldDim);
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.Text, title);
                    WikiView.ItemGrid(f.Items, $"f{i}");
                }
            }

            var extra = w.Coffers.Where(c => !withLoot.Any(f => f.Items.SequenceEqual(c.Items))).ToList();
            if (extra.Count > 0)
            {
                ImGui.Dummy(new Vector2(0, 2 * Theme.S));
                Theme.SubHeader("Сундуки по пути");
                for (var i = 0; i < extra.Count; i++)
                {
                    Theme.FaIcon(FontAwesomeIcon.Box, Theme.GoldDim);
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.TextDim, extra[i].Coords.Length > 0 ? $"Сундук ({extra[i].Coords})" : "Сундук");
                    WikiView.ItemGrid(extra[i].Items, $"c{i}");
                }
            }

            if (withLoot.Count == 0 && extra.Count == 0)
            {
                if (w.Rewards.Count == 0)
                {
                    ImGui.TextColored(Theme.TextDim, "Лут для этого контента не описан.");
                    return;
                }
                ImGui.TextColored(Theme.TextDim, "Всё, что можно получить (по боссам не разделено):");
                WikiView.ItemGrid(w.Rewards, "rw");
            }
            else
            {
                var rest = w.Rewards.Except(withLoot.SelectMany(f => f.Items)).Except(w.Coffers.SelectMany(c => c.Items)).ToList();
                if (rest.Count > 0)
                {
                    Theme.SubHeader("Прочие награды (токены, валюта)");
                    WikiView.ItemGrid(rest, "rest");
                }
            }
            ImGui.TextColored(Theme.Gray, "Зелёная рамка — улучшение для текущей профессии, синяя точка — уже есть у тебя.");
        });
    }

    private void DrawDetails(DutyEntry d)
    {
        Theme.Panel("dmain", () =>
        {
            Theme.Banner(d.Image, ImGui.GetContentRegionAvail().X);
            ImGui.Dummy(new Vector2(0, 2 * Theme.S));
            Theme.LargeText(Theme.Gold, d.Name);

            var (badge, color) = StatusBadge(d);
            if (badge is not null) { Theme.InlineBadge(badge, color); ImGui.SameLine(); }
            Theme.InlineBadge(d.Type, Theme.Blue);
            ImGui.SameLine();
            Theme.InlineBadge($"Ур. {d.Level}", Theme.TextDim);
            if (d.ItemLevel > 0) { ImGui.SameLine(); Theme.InlineBadge($"iLvl {d.ItemLevel}", Theme.TextDim); }
            if (d.Expansion.Length > 0) { ImGui.SameLine(); Theme.InlineBadge(d.Expansion, Theme.TextDim); }
            WikiNav.FavoriteButton(new WikiLink(WikiKind.Duty, d.CfcId, d.Name));
        });

        DrawLoot(d);

        var mech = MechanicsDb.Find(d.Name);
        if (mech is not null)
            Theme.Panel("dmech", () =>
            {
                Theme.LargeText(Theme.Gold, "Механики боссов");
                mechanicsView.Draw(mech, $"d{d.CfcId}");
            });

        if (d.UnlockQuestId == 0 || !data.Quests.TryGetValue(d.UnlockQuestId, out var unlock))
        {
            Theme.Panel("dnounlock", () =>
                ImGui.TextColored(Theme.TextDim, "Квест открытия не найден в данных игры: этот контент открывается иначе (по уровню, другим контентом или событием)."));
            return;
        }

        Theme.Panel("dunlock", () =>
        {
            Theme.SubHeader("Открывается квестом");
            var (qb, qc) = QuestsTab.StateBadge(unlock);
            if (Theme.Card("##unlock", unlock.Icon, unlock.Name, $"Ур. {unlock.Level} · {unlock.Place}", qb, qc, false, 44 * Theme.S))
                quests.Show(unlock);
            if (Theme.IconButton("uflag", FontAwesomeIcon.Flag, "Флаг на карте"))
                PlayerInfo.FlagQuestGiver(unlock);
        });

        if (!PlayerInfo.Ready)
            return;

        if (PlayerInfo.IsQuestDone(unlock.Id))
        {
            Theme.Panel("ddone", () =>
            {
                Theme.FaIcon(FontAwesomeIcon.CheckCircle, Theme.Green);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Green, "Квест выполнен: контент доступен в Duty Finder.");
            });
            return;
        }

        var next = PlayerInfo.NextStepTowards(data, unlock.Id);
        if (next is null || next.Id == unlock.Id)
            return;

        Theme.Panel("dnext", () =>
        {
            Theme.SubHeader("Сделать сейчас");
            if (Theme.Card("##dnextq", next.Icon, next.Name, $"Ур. {next.Level} · {next.Place}", null, Theme.Gray, false, 44 * Theme.S))
                quests.Show(next);
            if (Theme.IconButton("nflag", FontAwesomeIcon.Flag, "Флаг на карте"))
                PlayerInfo.FlagQuestGiver(next);
        });
    }
}
