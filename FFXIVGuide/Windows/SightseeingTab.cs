using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Журнал Sightseeing: все виды с местом, временем и эмоцией, отметка открытых и флаг на карте.</summary>
internal sealed unsafe class SightseeingTab
{
    private sealed record Vista(uint Id, int Index, string Name, string Place, string Region, uint LevelId, string Emote, int MinTime, int MaxTime, string Desc, uint Icon);

    private List<Vista>? vistas;
    private string search = "";
    private bool onlyMissing = true;
    private uint firstId;

    private void Build()
    {
        var sheet = Plugin.Data.GetExcelSheet<Adventure>();
        firstId = sheet.First().RowId;
        vistas = sheet.Where(a => a.Name.ExtractText().Length > 0)
            .Select(a =>
            {
                var place = a.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
                var region = a.Level.ValueNullable?.Territory.ValueNullable?.PlaceNameRegion.ValueNullable?.Name.ExtractText() ?? "";
                return new Vista(a.RowId, (int)(a.RowId - firstId), a.Name.ExtractText(), place, region, a.Level.RowId,
                    a.Emote.ValueNullable?.Name.ExtractText() ?? "", a.MinTime, a.MaxTime, a.Impression.ExtractText(),
                    (uint)Math.Max(0, a.IconList));
            }).ToList();
    }

    private static string Time(int hhmm) => $"{hhmm / 100:00}:{hhmm % 100:00}";

    private static bool Done(Vista v)
    {
        var ps = PlayerState.Instance();
        return ps != null && ps->IsAdventureComplete((uint)v.Index);
    }

    public void Focus(string text) { search = text; onlyMissing = false; }

    public IEnumerable<SearchHit> Search(string term, Action<MainTab> go)
    {
        if (vistas is null) Build();
        return vistas!.Where(v => v.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || v.Place.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Select(v => new SearchHit("Виды (Sightseeing)", $"№{v.Index + 1} · {v.Name}", $"{v.Place} · {v.Emote}", 0, () => { Focus(v.Name); go(MainTab.Sightseeing); }));
    }

    public void Draw()
    {
        Theme.Header("Виды (Sightseeing)", "Журнал видов: где стоять, в какое время и с какой эмоцией; что ещё не открыто");
        if (vistas is null)
        {
            try { Build(); }
            catch (Exception e) { Plugin.Log.Warning(e, "Виды: ошибка разбора"); vistas = new(); }
        }
        if (!PlayerInfo.Ready) { Theme.Panel("ssnone", () => ImGui.TextColored(Theme.TextDim, "Зайди персонажем.")); return; }

        var done = vistas!.Count(Done);
        var (eh, em) = EorzeaWeather.EorzeaTime(EorzeaWeather.Now);
        var etTime = eh * 100 + em;
        Theme.Panel("sstop", () =>
        {
            Theme.Progress("Открыто видов", done, vistas.Count, done == vistas.Count ? Theme.Green : Theme.Gold);
            ImGui.Checkbox("Только неоткрытые", ref onlyMissing);
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextDim, $"Сейчас в Эорзее {Time(etTime)}");
            ImGui.PushTextWrapPos(0);
            ImGui.TextColored(Theme.Gray, "Встань в отмеченное место в нужное время Эорзеи и сделай эмоцию. Некоторым видам нужна и определённая погода — её игра в данных не хранит, подсказка видна на табличке на месте.");
            ImGui.PopTextWrapPos();
        });
        ImGui.SetNextItemWidth(Math.Min(420 * Theme.S, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##sssearch", "Название, зона или регион…", ref search, 64);

        var list = vistas.Where(v => !onlyMissing || !Done(v))
            .Where(v => search.Length == 0 || v.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || v.Place.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || v.Region.Contains(search, StringComparison.OrdinalIgnoreCase) || NameTranslations.Show(v.Place).Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        bool InWindow(Vista v)
        {
            if (v.MinTime == v.MaxTime) return true;
            return v.MinTime < v.MaxTime ? etTime >= v.MinTime && etTime <= v.MaxTime : etTime >= v.MinTime || etTime <= v.MaxTime;
        }

        if (!ImGui.BeginChild("##sights", Vector2.Zero)) { ImGui.EndChild(); return; }
        foreach (var g in list.GroupBy(v => v.Region.Length > 0 ? v.Region : "Другое"))
        {
            Theme.SubHeader(NameTranslations.Show(g.Key));
            foreach (var v in g)
                Theme.Panel($"ss{v.Id}", () =>
                {
                    var ok = Done(v);
                    Theme.FaIcon(ok ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.Eye, ok ? Theme.Green : Theme.Gold);
                    ImGui.SameLine();
                    ImGui.TextColored(ok ? Theme.Green : Theme.Gold, $"№{v.Index + 1} · {v.Name}");
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.TextDim, NameTranslations.Show(v.Place));
                    var time = v.MinTime == v.MaxTime ? "в любое время" : $"ET {Time(v.MinTime)}–{Time(v.MaxTime)}";
                    var now = InWindow(v);
                    ImGui.TextColored(now ? Theme.Text : Theme.Gray, $"{time}{(v.MinTime != v.MaxTime ? now ? " (сейчас подходит)" : " (сейчас не то время)" : "")} · эмоция: {v.Emote}");
                    if (!ok && v.LevelId != 0)
                    {
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"Флаг##ssf{v.Id}")) ItemSources.FlagLevel(v.LevelId);
                    }
                    if (ok && v.Desc.Length > 0)
                    {
                        ImGui.PushTextWrapPos(0);
                        ImGui.TextColored(Theme.TextDim, v.Desc.Replace("\n", " "));
                        ImGui.PopTextWrapPos();
                    }
                });
        }
        ImGui.EndChild();
    }
}
