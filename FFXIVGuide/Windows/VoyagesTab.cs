using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Субмарины и дирижабли компании: секторы, требования, время, добыча; детали субмарин.</summary>
internal sealed class VoyagesTab
{
    private static readonly string[] Sections = { "Субмарины", "Дирижабли", "Детали субмарин" };
    private int section;
    private int rank = 1;
    private string search = "";
    private Dictionary<(int Type, uint Id), List<uint>>? loot;

    private sealed record Part(uint Id, int Slot, int Rank, int Components, int Surveillance, int Retrieval, int Speed, int Range, int Favor, int Class, uint Item);
    private List<Part>? parts;
    private int loadedVersion = -1;

    private void LoadLoot()
    {
        if (loot is not null && loadedVersion == OfflineDb.Version) return;
        loadedVersion = OfflineDb.Version;
        parts = null;
        loot = new Dictionary<(int, uint), List<uint>>();
        var dir = OfflineDb.Instance.Dir;
        try
        {
            var file = Path.Combine(dir, "voyage-sources.json");
            if (File.Exists(file))
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (!uint.TryParse(p.Name, out var item)) continue;
                    foreach (var v in p.Value.EnumerateArray())
                    {
                        if (!v.TryGetProperty("type", out var t) || !v.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number) continue;
                        var key = (t.GetInt32(), id.GetUInt32());
                        if (!loot.TryGetValue(key, out var l)) loot[key] = l = new List<uint>();
                        l.Add(item);
                    }
                }
            }
            var pf = Path.Combine(dir, "submarine-parts.json");
            if (File.Exists(pf))
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(pf));
                int I(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
                parts = doc.RootElement.EnumerateObject().Select(p => p.Value)
                    .Select(o => new Part((uint)I(o, "id"), I(o, "slot"), I(o, "rank"), I(o, "components"), I(o, "surveillance"), I(o, "retrieval"),
                        I(o, "speed"), I(o, "range"), I(o, "favor"), I(o, "class"), (uint)I(o, "itemId")))
                    .Where(p => p.Item != 0).OrderBy(p => p.Class).ThenBy(p => p.Slot).ToList();
            }
        }
        catch (Exception e) { Plugin.Log.Warning(e, "Вояжи: не удалось прочитать базу"); }
    }

    public void Draw()
    {
        Theme.Header("Субмарины и дирижабли", "Куда отправлять, сколько идти и что можно привезти");
        LoadLoot();
        if (!OfflineDb.Instance.Installed)
            ImGui.TextColored(Theme.Yellow, "Добыча и детали берутся из офлайн-базы — скачай её в Настройках.");
        Theme.Chips("vsec", Sections, ref section);
        if (section < 2)
        {
            ImGui.SetNextItemWidth(140 * Theme.S);
            ImGui.InputInt("ранг твоего судна##vrank", ref rank);
            rank = Math.Clamp(rank, 1, 150);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(260 * Theme.S);
            ImGui.InputTextWithHint("##vsearch", "Сектор или предмет добычи…", ref search, 64);
        }
        if (!ImGui.BeginChild("##voy", Vector2.Zero)) { ImGui.EndChild(); return; }
        switch (section)
        {
            case 0: DrawSubs(); break;
            case 1: DrawAirships(); break;
            default: DrawParts(); break;
        }
        ImGui.EndChild();
    }

    private bool LootMatches(List<uint>? items, string term) =>
        items is not null && items.Any(i => NameTranslations.Matches(WikiView.ItemInfo(i).Name, term));

    private void DrawSubs()
    {
        var term = search.Trim();
        var maps = Plugin.Data.GetExcelSheet<SubmarineMap>();
        var sectors = Plugin.Data.GetExcelSheet<SubmarineExploration>()
            .Where(s => !s.StartingPoint && s.Destination.ExtractText().Length > 0)
            .ToList();
        foreach (var g in sectors.GroupBy(s => s.Map.RowId))
        {
            var mapName = maps.GetRowOrDefault(g.Key)?.Name.ExtractText() ?? $"Карта {g.Key}";
            var rows = g.Where(s => term.Length < 2 || NameTranslations.Matches(s.Destination.ExtractText(), term) || LootMatches(loot?.GetValueOrDefault((1, s.RowId)), term)).ToList();
            if (rows.Count == 0) continue;
            Theme.Panel($"sm{g.Key}", () =>
            {
                Theme.SubHeader(GuideData.Capitalize(mapName));
                foreach (var s in rows)
                {
                    var ok = s.RankReq <= rank;
                    Theme.InlineBadge(s.Location.ExtractText(), ok ? Theme.Gold : Theme.Gray);
                    ImGui.SameLine();
                    ImGui.TextColored(ok ? Theme.Text : Theme.TextDim, GuideData.Capitalize(s.Destination.ExtractText()));
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.TextDim, $"ранг {s.RankReq} · {new string('★', s.Stars)} · разведка {s.SurveyDurationmin / 60}ч{s.SurveyDurationmin % 60:00} · опыт {s.ExpReward:N0} · топливо {s.CeruleumTankReq}");
                    if (loot?.GetValueOrDefault((1, s.RowId)) is { Count: > 0 } items)
                        WikiView.ItemGrid(items.Distinct(), $"sl{s.RowId}");
                }
            });
        }
    }

    private void DrawAirships()
    {
        var term = search.Trim();
        var points = Plugin.Data.GetExcelSheet<AirshipExplorationPoint>()
            .Where(p => !p.Passengers && p.Name.ExtractText().Length > 0 && p.RankReq > 0)
            .Where(p => term.Length < 2 || NameTranslations.Matches(p.Name.ExtractText(), term) || LootMatches(loot?.GetValueOrDefault((0, p.RowId)), term))
            .OrderBy(p => p.RankReq).ToList();
        Theme.Panel("air", () =>
        {
            foreach (var p in points)
            {
                var ok = p.RankReq <= rank;
                Theme.InlineBadge($"ранг {p.RankReq}", ok ? Theme.Gold : Theme.Gray);
                ImGui.SameLine();
                ImGui.TextColored(ok ? Theme.Text : Theme.TextDim, GuideData.Capitalize(p.Name.ExtractText()));
                ImGui.SameLine();
                ImGui.TextColored(Theme.TextDim, $"разведка {p.SurveyDurationmin / 60}ч{p.SurveyDurationmin % 60:00} · дистанция {p.SurveyDistance} · нужна разведка {p.SurveillanceReq} · опыт {p.ExpReward:N0} · топливо {p.CeruleumTankReq}");
                if (loot?.GetValueOrDefault((0, p.RowId)) is { Count: > 0 } items)
                    WikiView.ItemGrid(items.Distinct(), $"al{p.RowId}");
            }
        });
    }

    private static readonly string[] SlotNames = { "Корпус (hull)", "Корма (stern)", "Нос (bow)", "Мостик (bridge)" };

    private void DrawParts()
    {
        if (parts is null) { ImGui.TextColored(Theme.TextDim, "Нет данных — скачай или обнови офлайн-базу."); return; }
        ImGui.TextColored(Theme.TextDim, "Слоты: корпус, корма, нос, мостик. Наведи на деталь — подсветится в сумке, клик — карточка с крафтом.");
        foreach (var g in parts.GroupBy(p => p.Slot).OrderBy(g => g.Key))
            Theme.Panel($"pt{g.Key}", () =>
            {
                Theme.SubHeader(g.Key < SlotNames.Length ? SlotNames[g.Key] : $"Слот {g.Key}");
                if (!ImGui.BeginTable($"##parts{g.Key}", 8, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) return;
                foreach (var h in new[] { "Деталь", "Ранг", "Разведка", "Сбор", "Скорость", "Дальность", "Удача", "Компоненты" }) ImGui.TableSetupColumn(h);
                ImGui.TableHeadersRow();
                foreach (var p in g.OrderBy(p => p.Rank))
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); WikiView.ItemLine(p.Item, uid: "part");
                    ImGui.TableNextColumn(); ImGui.TextColored(Theme.TextDim, p.Rank.ToString());
                    foreach (var v in new[] { p.Surveillance, p.Retrieval, p.Speed, p.Range, p.Favor })
                    {
                        ImGui.TableNextColumn();
                        ImGui.TextColored(v > 0 ? Theme.Green : v < 0 ? Theme.Red : Theme.Gray, v.ToString());
                    }
                    ImGui.TableNextColumn(); ImGui.TextColored(Theme.TextDim, p.Components.ToString());
                }
                ImGui.EndTable();
            });
    }
}
