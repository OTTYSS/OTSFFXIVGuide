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

/// <summary>Коллекционные предметы: что сдавать за скрипы, пороги коллекционности и награда.</summary>
internal sealed class CollectablesTab
{
    private sealed record Tier(int Rating, int Scrip, int Exp);
    private sealed record Entry(uint Item, string Job, int Level, uint Reward, Tier Base, Tier Mid, Tier High);

    private List<Entry>? entries;
    private int loadedVersion = -1;
    private int job;
    private string[] jobs = { "Все" };
    private string search = "";
    private bool onlyMyLevel = true;

    private static string JobOf(uint item)
    {
        var look = Plugin.Data.GetExcelSheet<RecipeLookup>().GetRowOrDefault(item);
        if (look is { } l)
        {
            var names = new[] { "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL" };
            var refs = new[] { l.CRP, l.BSM, l.ARM, l.GSM, l.LTW, l.WVR, l.ALC, l.CUL };
            for (var i = 0; i < refs.Length; i++) if (refs[i].RowId != 0) return names[i];
        }
        var g = GatherIndex.For(item).FirstOrDefault();
        if (g is not null) return g.Type is "Mining" or "Quarrying" ? "MIN" : "BTN";
        return Plugin.Data.GetExcelSheet<FishParameter>().Any(f => f.Item.RowId == item) ? "FSH" : "?";
    }

    private void Build()
    {
        entries = new List<Entry>();
        try
        {
            var f = Path.Combine(OfflineDb.Instance.Dir, "collectables.json");
            if (!File.Exists(f)) return;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(f));
            Tier T(JsonElement o, string n) => o.TryGetProperty(n, out var t) && t.ValueKind == JsonValueKind.Object
                ? new Tier(t.TryGetProperty("rating", out var r) ? r.GetInt32() : 0, t.TryGetProperty("scrip", out var s) ? s.GetInt32() : 0, t.TryGetProperty("exp", out var e) ? e.GetInt32() : 0)
                : new Tier(0, 0, 0);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (!uint.TryParse(p.Name, out var item)) continue;
                var o = p.Value;
                var reward = o.TryGetProperty("reward", out var rw) && rw.ValueKind == JsonValueKind.Number ? rw.GetUInt32() : 0;
                var lvl = o.TryGetProperty("level", out var lv) && lv.ValueKind == JsonValueKind.Number ? lv.GetInt32() : 0;
                entries.Add(new Entry(item, JobOf(item), lvl, reward, T(o, "base"), T(o, "mid"), T(o, "high")));
            }
            entries = entries.OrderBy(e => e.Job).ThenByDescending(e => e.Level).ToList();
            jobs = new[] { "Все" }.Concat(entries.Select(e => e.Job).Distinct().OrderBy(x => x)).ToArray();
        }
        catch (Exception e) { Plugin.Log.Warning(e, "Коллекционные: не удалось прочитать базу"); }
    }

    private static readonly Dictionary<string, int> levelCache = new();
    private static DateTime levelAt;

    private static int MyLevel(string abbr)
    {
        if (DateTime.Now - levelAt > TimeSpan.FromSeconds(10)) { levelCache.Clear(); levelAt = DateTime.Now; }
        if (levelCache.TryGetValue(abbr, out var c)) return c;
        return levelCache[abbr] = MyLevelRaw(abbr);
    }

    private static int MyLevelRaw(string abbr)
    {
        var cj = Plugin.Data.GetExcelSheet<ClassJob>().FirstOrDefault(c => GuideData.JobAbbrEnglish(c.RowId) == abbr);
        return cj.RowId == 0 || !PlayerInfo.Ready ? 100 : Plugin.PlayerState.GetClassJobLevel(cj);
    }

    public void Draw()
    {
        Theme.Header("Коллекционные предметы", "Что сдавать за скрипы: пороги коллекционности, сколько скрипов и опыта дают");
        if (entries is null || loadedVersion != OfflineDb.Version) { loadedVersion = OfflineDb.Version; Build(); }
        if (entries!.Count == 0)
        {
            Theme.EmptyState(FontAwesomeIcon.Box, "Нет данных", "Коллекционные берутся из офлайн-базы — нажми «Обновить» в Настройках → «База данных».");
            return;
        }
        Theme.Chips("cjob", jobs, ref job);
        Theme.Search("##csearch", "Предмет…", ref search);
        ImGui.Checkbox("Только доступные по моему уровню", ref onlyMyLevel);

        var term = search.Trim();
        var rows = entries.Where(e => (job == 0 || e.Job == jobs[job])
                                      && (term.Length < 2 || NameTranslations.Matches(WikiView.ItemInfo(e.Item).Name, term))
                                      && (!onlyMyLevel || e.Level <= MyLevel(e.Job))).ToList();

        if (!ImGui.BeginChild("##coll", Vector2.Zero)) { ImGui.EndChild(); return; }
        if (ImGui.BeginTable("##ctable", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Предмет", ImGuiTableColumnFlags.WidthStretch, 3);
            ImGui.TableSetupColumn("Проф.");
            ImGui.TableSetupColumn("Ур.");
            ImGui.TableSetupColumn("Порог → скрипы", ImGuiTableColumnFlags.WidthStretch, 2);
            ImGui.TableSetupColumn("Опыт (макс.)");
            ImGui.TableSetupColumn("Скрип", ImGuiTableColumnFlags.WidthStretch, 2);
            ImGui.TableHeadersRow();
            foreach (var e in rows.Take(400))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); WikiView.ItemLine(e.Item, uid: "col");
                ImGui.TableNextColumn(); ImGui.TextColored(Theme.TextDim, e.Job);
                ImGui.TableNextColumn(); ImGui.TextColored(Theme.TextDim, e.Level.ToString());
                ImGui.TableNextColumn(); ImGui.TextColored(Theme.Text, $"{e.Base.Rating}→{e.Base.Scrip} · {e.Mid.Rating}→{e.Mid.Scrip} · {e.High.Rating}→{e.High.Scrip}");
                ImGui.TableNextColumn(); ImGui.TextColored(Theme.TextDim, $"{e.High.Exp:N0}");
                ImGui.TableNextColumn();
                if (e.Reward != 0) ImGui.TextColored(Theme.Gold, NameTranslations.Show(WikiView.ItemInfo(e.Reward).Name));
            }
            ImGui.EndTable();
        }
        if (rows.Count > 400) ImGui.TextColored(Theme.Gray, $"Показаны первые 400 из {rows.Count} — уточни фильтр.");
        ImGui.EndChild();
    }
}
