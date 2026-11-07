using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

/// <summary>Бестиарий: все мобы с уровнем, зоной, точкой на карте и дропом (данные Garland Tools).</summary>
internal sealed class BestiaryTab
{
    private const int MaxRows = 400;

    private readonly GuideData data;
    private string search = "";
    private string lastSearch = "\0";
    private int lastIndexCount = -1;
    private string? appliedEnglish;
    private List<WikiMobRow> results = new();
    private int total;
    private ulong selected;
    private bool scrollTo;

    public BestiaryTab(GuideData data) => this.data = data;

    /// <summary>Открыть карточку моба из другого раздела.</summary>
    public void Show(ulong id)
    {
        selected = id;
        scrollTo = true;
    }

    /// <summary>Id моба в формате Garland для текущей цели в игре (BNpcBase × 10¹⁰ + BNpcName).</summary>
    public static ulong? TargetMobId()
    {
        if (Plugin.Targets.Target is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc npc) return null;
        if (npc.BaseId == 0 || npc.NameId == 0) return null;
        return (ulong)npc.BaseId * 10_000_000_000UL + npc.NameId;
    }

    /// <summary>Поиск из других разделов (например, глобального поиска).</summary>
    public void SetSearch(string text) => search = text;

    public static List<WikiMobRow> Find(List<WikiMobRow> index, string term, string? english)
    {
        var variants = new[] { term, english }.Where(v => !string.IsNullOrWhiteSpace(v) && v!.Length >= 2).Select(v => v!).ToList();
        return index
            .Where(m => variants.Any(v => NameTranslations.Matches(m.Name, v) ||
                                          WikiService.ZoneName(m.Zone).Contains(v, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(m => variants.Any(v => m.Name.Equals(v, StringComparison.OrdinalIgnoreCase)) ? 0 :
                          variants.Any(v => m.Name.StartsWith(v, StringComparison.OrdinalIgnoreCase)) ? 1 :
                          variants.Any(v => m.Name.Contains(v, StringComparison.OrdinalIgnoreCase)) ? 2 : 3)
            .ThenBy(m => m.Name)
            .ToList();
    }

    public void Draw()
    {
        Theme.Header("Бестиарий", "Мобы и боссы: где живут, какого уровня и что с них падает. Данные Garland Tools.");
        if (TargetMobId() is { } tid && Theme.IconButton("mtarget", FontAwesomeIcon.Crosshairs, "Моя цель"))
            WikiNav.Open(WikiKind.Mob, tid, Plugin.Targets.Target?.Name.TextValue ?? "");
        Theme.Tooltip("Открыть карточку моба, которого ты выделил в игре");
        Theme.Search("##msearch", "Имя моба или зона (например: Coblyn, Central Shroud, Огненный)…", ref search);

        var index = WikiService.Instance.MobIndex();
        if (index is null)
        {
            Theme.Panel("mload", () => WikiView.Status(null, "browse", "mob"));
            return;
        }

        var term = search.Trim();
        var cyr = term.Any(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я');
        if (cyr && search == lastSearch && appliedEnglish is null && Translator.Instance.GetEnglish(term) is { } en)
        {
            appliedEnglish = en;
            lastSearch = "\0";
        }
        if (search != lastSearch || index.Count != lastIndexCount)
        {
            if (search != lastSearch && lastSearch != "\0") appliedEnglish = null;
            lastSearch = search;
            lastIndexCount = index.Count;
            if (cyr && appliedEnglish is null) Translator.Instance.RequestEnglish(term);
            var found = term.Length < 2 ? new List<WikiMobRow>() : Find(index, term, cyr ? appliedEnglish : null);
            total = found.Count;
            results = found.Take(MaxRows).ToList();
        }

        ImGui.TextColored(Theme.TextDim,
            term.Length < 2 ? $"В базе {index.Count:N0} мобов. Начни вводить имя или зону." :
            total > MaxRows ? $"Найдено {total}, показаны первые {MaxRows}" : $"Найдено: {total}");

        var avail = ImGui.GetContentRegionAvail();
        var rowH = 42 * Theme.S;
        if (ImGui.BeginChild("##mlist", new Vector2(Theme.SplitWidth("mobs", avail.X, 0.45f), 0)))
        {
            if (scrollTo)
            {
                var idx = results.FindIndex(r => r.Id == selected);
                if (idx >= 0) ImGui.SetScrollY(idx * (rowH + ImGui.GetStyle().ItemSpacing.Y));
                scrollTo = false;
            }
            Theme.VirtualList(results.Count, rowH, i =>
            {
                var m = results[i];
                var zone = WikiService.ZoneName(m.Zone);
                if (Theme.Card($"##m{m.Id}", 0, m.Name, zone, m.Level.Length > 0 ? $"ур. {m.Level}" : null, Theme.TextDim, selected == m.Id, rowH))
                    WikiNav.Open(WikiKind.Mob, m.Id, m.Name);
            });
        }
        ImGui.EndChild();

        Theme.Splitter("mobs", avail.X, 0.45f);
        if (ImGui.BeginChild("##mdetails", Vector2.Zero))
        {
            if (selected == 0)
                Theme.EmptyState(FontAwesomeIcon.Skull, "Выбери моба", "Покажу зону и точку на карте (кнопка «Флаг»), уровень и всё, что с него падает. Любой предмет кликабелен.");
            else
                DrawMob(selected, index);
        }
        ImGui.EndChild();
    }

    /// <summary>Фото моба с вики по ширине карточки (не выше 280 px).</summary>
    public static void DrawPhoto(string name, float maxHeight = 280f)
    {
        var (state, tex) = MobImages.Instance.Get(name);
        var w = ImGui.GetContentRegionAvail().X;
        if (state == MobImages.State.Loading)
        {
            var pos = ImGui.GetCursorScreenPos();
            var size = new Vector2(w, 120 * Theme.S);
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(pos, pos + size, Theme.U32(Theme.CardBg), 8f * Theme.S);
            var t = "Ищу фото" + new string('.', 1 + (int)(ImGui.GetTime() * 3) % 3);
            var ts = ImGui.CalcTextSize(t);
            dl.AddText(pos + (size - ts) / 2, Theme.U32(Theme.TextDim), t);
            ImGui.Dummy(size);
            return;
        }
        if (state != MobImages.State.Ready || tex is null) return;

        var maxH = maxHeight * Theme.S;
        var scale = Math.Min(w / tex.Width, maxH / tex.Height);
        var img = new Vector2(tex.Width, tex.Height) * scale;
        var start = ImGui.GetCursorScreenPos();
        var p0 = start + new Vector2((w - img.X) / 2, 0);
        var dl2 = ImGui.GetWindowDrawList();
        dl2.AddRectFilled(start, start + new Vector2(w, img.Y), Theme.U32(Theme.CardBg), 8f * Theme.S);
        dl2.AddImageRounded(tex.Handle, p0, p0 + img, Vector2.Zero, Vector2.One, 0xFFFFFFFF, 8f * Theme.S);
        ImGui.Dummy(new Vector2(w, img.Y));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Фото: ffxiv.consolegameswiki.com");
        ImGui.Dummy(new Vector2(0, 2 * Theme.S));
    }

    private void DrawMob(ulong id, List<WikiMobRow> index)
    {
        var mob = WikiService.Instance.Mob(id);
        var row = index.FirstOrDefault(r => r.Id == id);
        WikiNav.Visit(new WikiLink(WikiKind.Mob, id, mob?.Name ?? row?.Name ?? $"Моб #{id}"));

        Theme.Panel("mmain", () =>
        {
            DrawPhoto(mob?.Name ?? row?.Name ?? "");
            Theme.LargeText(Theme.Gold, mob?.Name ?? row?.Name ?? $"Моб #{id}");
            WikiNav.FavoriteButton(new WikiLink(WikiKind.Mob, id, mob?.Name ?? row?.Name ?? $"Моб #{id}"));
            if (!WikiView.Status(mob, "mob", id)) return;
            var m = mob!;
            var zone = WikiService.ZoneName(m.Zone);
            if (m.Level.Length > 0) { Theme.InlineBadge($"ур. {m.Level}", Theme.Gold); ImGui.SameLine(); }
            if (zone.Length > 0)
            {
                var zp = ImGui.GetCursorScreenPos();
                Theme.InlineBadge(zone, Theme.Blue);
                if (ImGui.IsItemClicked()) WikiNav.Open(WikiKind.Zone, m.Zone, zone);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Открыть карточку зоны");
                ImGui.SameLine();
            }
            if (m.Quest) { Theme.InlineBadge("квестовый", Theme.Yellow); ImGui.SameLine(); }
            ImGui.NewLine();

            if (m.HasCoords)
            {
                Theme.FaIcon(FontAwesomeIcon.MapMarkerAlt, Theme.GoldDim);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Text, $"Координаты: X {m.X:0.0}, Y {m.Y:0.0}");
                if (Theme.IconButton("mflag", FontAwesomeIcon.Flag, "Флаг на карте"))
                    WikiService.Flag(m.Zone, m.X, m.Y);
            }

            var duty = data.Duties.FirstOrDefault(d => d.InstanceContentId != 0 && d.Name.Equals(Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.PlaceName>().GetRowOrDefault(m.Zone)?.Name.ExtractText() ?? "", StringComparison.OrdinalIgnoreCase));
            if (duty is not null)
            {
                ImGui.Dummy(new Vector2(0, 2 * Theme.S));
                Theme.SubHeader("Встречается в");
                WikiView.DutyLine(duty.InstanceContentId, duty.Name, "", "mob");
            }
        });

        if (mob is null) return;

        Theme.Panel("mdrops", () =>
        {
            Theme.SubHeader($"Дроп · {mob.Drops.Count}");
            if (mob.Drops.Count == 0)
            {
                ImGui.TextColored(Theme.TextDim, "Ничего не известно о дропе (или с моба ничего не падает).");
                return;
            }
            foreach (var d in mob.Drops) WikiView.ItemLine(d, uid: "md");
        });

        // Тёзки: тот же моб в других зонах/уровнях
        var twins = index.Where(r => r.Id != id && r.Name.Equals(mob.Name, StringComparison.OrdinalIgnoreCase)).Take(20).ToList();
        if (twins.Count > 0)
            Theme.Panel("mtwins", () =>
            {
                Theme.SubHeader("Тот же моб в других местах");
                foreach (var t in twins) WikiView.MobLine(t, "tw");
            });
    }
}
