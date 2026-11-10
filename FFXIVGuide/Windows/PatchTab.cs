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

/// <summary>
/// Что нового в патче: (1) сравнение файлов игры с прошлой версией — работает сразу после обновления;
/// (2) разметка по патчам из базы Teamcraft — предметы, квесты, достижения и FATE каждого патча.
/// </summary>
internal sealed class PatchTab
{
    private sealed record PatchInfo(int Id, string Version, string Name, DateTime Released, Dictionary<string, List<uint>> Content);

    private List<PatchInfo>? patches;
    private int loadedFor = -1;
    private string? loadError;
    private int pick;
    private int mode;
    private static readonly string[] Modes = { "С прошлого обновления игры", "По патчам (Teamcraft)" };

    private void LoadPatches()
    {
        loadedFor = OfflineDb.Version;
        patches = null;
        loadError = null;
        var dir = OfflineDb.Instance.Dir;
        var namesFile = Path.Combine(dir, "patch-names.json");
        var contentFile = Path.Combine(dir, "patch-content.json");
        if (!File.Exists(namesFile) || !File.Exists(contentFile)) return;
        try
        {
            using var names = JsonDocument.Parse(File.ReadAllText(namesFile));
            using var content = JsonDocument.Parse(File.ReadAllText(contentFile));
            var list = new List<PatchInfo>();
            foreach (var p in names.RootElement.EnumerateObject())
            {
                if (!int.TryParse(p.Name, out var id)) continue;
                var v = p.Value;
                var ver = v.TryGetProperty("version", out var vv) && vv.ValueKind == JsonValueKind.String ? vv.GetString() ?? "" : "";
                var en = v.TryGetProperty("en", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
                var rel = v.TryGetProperty("release", out var r) && r.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(r.GetInt64()).LocalDateTime : DateTime.MinValue;
                var map = new Dictionary<string, List<uint>>();
                if (content.RootElement.TryGetProperty(p.Name, out var c) && c.ValueKind == JsonValueKind.Object)
                    foreach (var kind in c.EnumerateObject())
                        if (kind.Value.ValueKind == JsonValueKind.Array)
                            map[kind.Name] = kind.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetUInt32()).ToList();
                list.Add(new PatchInfo(id, ver, en, rel, map));
            }
            patches = list.OrderByDescending(x => x.Released).ThenByDescending(x => x.Id).ToList();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Патчи: не удалось прочитать данные Teamcraft");
            loadError = ex.Message;
        }
    }

    public void Draw()
    {
        Theme.Header("Что нового в патче", "Новые предметы, маунты, квесты, контент и достижения");
        Theme.Chips("patchmode", Modes, ref mode);
        if (!ImGui.BeginChild("##patch", Vector2.Zero)) { ImGui.EndChild(); return; }
        if (mode == 0) DrawSnapshot();
        else DrawTeamcraft();
        ImGui.EndChild();
    }

    // ---------- Сравнение файлов игры ----------

    private void DrawSnapshot()
    {
        if (!PatchSnapshot.Ready) { Theme.Panel("psload", () => ImGui.TextColored(Theme.TextDim, "Сравниваю файлы игры…")); return; }
        if (PatchSnapshot.Error is not null) { Theme.Panel("pserr", () => ImGui.TextColored(Theme.Red, "Не удалось сравнить: " + PatchSnapshot.Error)); return; }
        var added = PatchSnapshot.Added;
        if (added is null)
        {
            Theme.EmptyState(FontAwesomeIcon.History, "Пока не с чем сравнивать",
                $"Плагин запомнил файлы игры версии {PatchSnapshot.GameVersion}. После следующего обновления игры здесь появится всё, что оно добавило. А пока — вкладка «По патчам (Teamcraft)».");
            return;
        }
        Theme.Panel("pshead", () =>
        {
            ImGui.TextColored(Theme.Gold, $"Версия игры {PatchSnapshot.PreviousVersion} → {PatchSnapshot.GameVersion}");
            var total = added.Values.Sum(l => l.Count);
            ImGui.TextColored(Theme.TextDim, total == 0
                ? "Новых строк в основных таблицах нет — обновление, видимо, техническое (исправления, без нового контента)."
                : "Что появилось в файлах игры. Названия могут быть пустыми или временными, пока контент не открыт официально.");
        });

        foreach (var (key, title, _) in PatchSnapshot.Sheets)
        {
            if (!added.TryGetValue(key, out var ids) || ids.Count == 0) continue;
            Theme.Panel($"ps{key}", () =>
            {
                Theme.SubHeader($"{title}: {ids.Count}");
                DrawIds(key, ids, "ps");
            });
        }
    }

    // ---------- Данные Teamcraft ----------

    private void DrawTeamcraft()
    {
        if (patches is null || loadedFor != OfflineDb.Version) LoadPatches();
        if (patches is null || patches.Count == 0)
        {
            Theme.Panel("ptnone", () =>
            {
                ImGui.PushTextWrapPos(0);
                if (loadError is not null) ImGui.TextColored(Theme.Red, "Ошибка чтения: " + loadError);
                ImGui.TextColored(Theme.TextDim, "Разметка по патчам берётся из офлайн-базы Teamcraft. Её нужно скачать или обновить (добавились два файла).");
                ImGui.PopTextWrapPos();
                if (OfflineDb.Instance.Busy) ImGui.TextColored(Theme.Gold, OfflineDb.Instance.Status);
                else if (ImGui.Button("Скачать / обновить базу")) OfflineDb.Instance.Update(false);
            });
            return;
        }

        var shown = patches.Take(12).ToList();
        var labels = shown.Select(p => p.Version.Length > 0 ? p.Version : p.Name).ToArray();
        pick = Math.Clamp(pick, 0, labels.Length - 1);
        Theme.Chips("ptpick", labels, ref pick);
        var cur = shown[pick];

        Theme.Panel("pthead", () =>
        {
            ImGui.TextColored(Theme.Gold, cur.Name);
            if (cur.Released > DateTime.MinValue) { ImGui.SameLine(); ImGui.TextColored(Theme.TextDim, $"вышел {cur.Released:dd.MM.yyyy}"); }
            var n = cur.Content.Values.Sum(l => l.Count);
            if (n == 0)
                ImGui.TextColored(Theme.TextDim, "Teamcraft ещё не разметил содержимое этого патча. Обычно это происходит через несколько дней; смотри «С прошлого обновления игры».");
        });

        if (cur.Content.TryGetValue("item", out var items) && items.Count > 0) DrawItemGroups(items, $"pt{cur.Id}");
        foreach (var (key, title) in new[] { ("quest", "Квесты"), ("achievement", "Достижения"), ("fate", "FATE") })
            if (cur.Content.TryGetValue(key, out var ids) && ids.Count > 0)
                Theme.Panel($"pt{cur.Id}{key}", () =>
                {
                    Theme.SubHeader($"{title}: {ids.Count}");
                    DrawIds(key, ids, $"pt{cur.Id}");
                });
    }

    /// <summary>Предметы патча по категориям: маунты, минёны, снаряжение, мебель и т.д.</summary>
    private static void DrawItemGroups(List<uint> ids, string uid)
    {
        var sheet = Plugin.Data.GetExcelSheet<Item>();
        var groups = ids.Select(id => sheet.GetRowOrDefault(id)).Where(i => i is not null && !i.Value.Name.IsEmpty).Select(i => i!.Value)
            .GroupBy(i => i.ItemUICategory.ValueNullable?.Name.ExtractText() is { Length: > 0 } c ? c : "Прочее")
            .OrderByDescending(g => g.Count()).ToList();
        Theme.Panel(uid + "items", () =>
        {
            Theme.SubHeader($"Предметы: {groups.Sum(g => g.Count())}");
            ImGui.TextColored(Theme.Gray, "Синяя точка — уже есть у тебя. Нажми на предмет — откроется его карточка.");
            foreach (var g in groups)
                if (ImGui.CollapsingHeader($"{NameTranslations.Show(g.Key)} ({g.Count()})##{uid}{g.Key}"))
                    WikiView.ItemGrid(g.Select(i => i.RowId), $"{uid}{g.Key}");
        });
    }

    private static void DrawIds(string key, List<uint> ids, string uid)
    {
        switch (key)
        {
            case "item":
                WikiView.ItemGrid(ids, uid + key);
                return;
            case "recipe":
            {
                var rec = Plugin.Data.GetExcelSheet<Recipe>();
                WikiView.ItemGrid(ids.Select(id => rec.GetRowOrDefault(id)?.ItemResult.RowId ?? 0).Where(x => x != 0).Distinct(), uid + key);
                return;
            }
        }
        foreach (var id in ids.Take(300))
        {
            var (name, open) = Describe(key, id);
            if (name.Length == 0) continue;
            if (open is not null)
            {
                if (ImGui.Selectable($"{NameTranslations.Show(name)}##{uid}{key}{id}")) open();
            }
            else ImGui.TextColored(Theme.Text, NameTranslations.Show(name));
        }
        if (ids.Count > 300) ImGui.TextColored(Theme.TextDim, $"…и ещё {ids.Count - 300}");
    }

    private static (string Name, System.Action? Open) Describe(string key, uint id)
    {
        switch (key)
        {
            case "mount": return (GuideData.Capitalize(Plugin.Data.GetExcelSheet<Mount>().GetRowOrDefault(id)?.Singular.ExtractText() ?? ""), null);
            case "minion": return (GuideData.Capitalize(Plugin.Data.GetExcelSheet<Companion>().GetRowOrDefault(id)?.Singular.ExtractText() ?? ""), null);
            case "orchestrion": return (Plugin.Data.GetExcelSheet<Orchestrion>().GetRowOrDefault(id)?.Name.ExtractText() ?? "", null);
            case "card": return (Plugin.Data.GetExcelSheet<TripleTriadCard>().GetRowOrDefault(id)?.Name.ExtractText() ?? "", null);
            case "emote": return (Plugin.Data.GetExcelSheet<Emote>().GetRowOrDefault(id)?.Name.ExtractText() ?? "", null);
            case "achievement":
            {
                var a = Plugin.Data.GetExcelSheet<Achievement>().GetRowOrDefault(id);
                return (a is null ? "" : $"{a.Value.Name.ExtractText()} — {a.Value.Description.ExtractText()}", null);
            }
            case "fate": return (Plugin.Data.GetExcelSheet<Fate>().GetRowOrDefault(id)?.Name.ExtractText() ?? "", null);
            case "quest":
            {
                var name = Plugin.Data.GetExcelSheet<Quest>().GetRowOrDefault(id)?.Name.ExtractText() ?? "";
                return (name, () => WikiNav.Open(WikiKind.Quest, id, name));
            }
            case "duty":
            {
                var name = GuideData.Capitalize(Plugin.Data.GetExcelSheet<ContentFinderCondition>().GetRowOrDefault(id)?.Name.ExtractText() ?? "");
                return (name, () => WikiNav.Open(WikiKind.Duty, id, name));
            }
        }
        return ("", null);
    }
}
