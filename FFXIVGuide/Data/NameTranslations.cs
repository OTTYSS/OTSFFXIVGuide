using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

/// <summary>
/// Русские названия предметов, мобов, NPC, квестов, данжей и зон.
/// Официального русского у игры нет, поэтому переводим машинно (Google) в фоне пачками с паузами
/// и сохраняем на диск. Обновление базы переводит только новые названия.
/// </summary>
public sealed class NameTranslations : IDisposable
{
    public static NameTranslations Instance { get; private set; } = null!;

    private readonly HttpClient http = Net.Create(TimeSpan.FromSeconds(30));
    private readonly ConcurrentDictionary<string, string> ru = new(StringComparer.Ordinal);
    private readonly string file;
    private CancellationTokenSource? cts;

    public bool Running { get; private set; }
    public bool Paused { get; set; }
    public int Total { get; private set; }
    public int Done { get; private set; }
    public string Status { get; private set; } = "";
    public int Count => ru.Count;

    public NameTranslations()
    {
        Instance = this;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        file = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "ru-names.json");
        try
        {
            if (File.Exists(file))
                foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? new())
                    ru[k] = v;
        }
        catch (Exception e) { Plugin.Log.Warning(e, "Русские названия: не удалось прочитать файл"); }
    }

    // ───────────── Использование ─────────────

    public string? Get(string en) => en.Length > 0 && ru.TryGetValue(en, out var r) ? r : null;

    /// <summary>Как показывать название по настройке: английское, русское или «русское (English)».</summary>
    public static string Show(string en)
    {
        if (Instance is null || Plugin.Config.NameMode == 0 || string.IsNullOrEmpty(en)) return en;
        var r = Instance.Get(en);
        if (r is null || r.Equals(en, StringComparison.OrdinalIgnoreCase)) return en;
        return Plugin.Config.NameMode == 1 ? r : $"{r} ({en})";
    }

    /// <summary>Совпадает ли английское название или его перевод с запросом.</summary>
    public static bool Matches(string en, string term) =>
        en.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        (Instance?.Get(en) is { } r && r.Contains(term, StringComparison.OrdinalIgnoreCase));

    // ───────────── Фоновый перевод ─────────────

    /// <summary>Все названия, которые стоит перевести.</summary>
    private static List<string> Collect()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        void Add(string s)
        {
            s = s.Trim();
            if (s.Length >= 2 && s.Length < 120 && s.Any(char.IsLetter)) set.Add(s);
        }
        foreach (var it in Plugin.Data.GetExcelSheet<Item>()) Add(it.Name.ExtractText());
        foreach (var q in Plugin.Data.GetExcelSheet<Quest>()) Add(q.Name.ExtractText());
        foreach (var c in Plugin.Data.GetExcelSheet<ContentFinderCondition>()) Add(GuideData.Capitalize(c.Name.ExtractText()));
        foreach (var t in Plugin.Data.GetExcelSheet<TerritoryType>()) Add(t.PlaceName.ValueNullable?.Name.ExtractText() ?? "");
        foreach (var f in Plugin.Data.GetExcelSheet<Fate>()) Add(f.Name.ExtractText());
        foreach (var a in Plugin.Data.GetExcelSheet<AozAction>()) Add(a.Action.ValueNullable?.Name.ExtractText() ?? "");
        foreach (var c in Plugin.Data.GetExcelSheet<TripleTriadCard>()) Add(c.Name.ExtractText());
        foreach (var b in Plugin.Data.GetExcelSheet<BeastTribe>()) Add(GuideData.Capitalize(b.Name.ExtractText()));
        if (WikiService.Instance.MobIndex() is { } mobs) foreach (var m in mobs) Add(m.Name);
        if (WikiService.Instance.NpcIndex() is { } npcs)
            foreach (var n in npcs) { Add(n.Name); Add(n.Title); }
        if (WikiService.Instance.FateIndex() is { } fates) foreach (var f in fates) Add(f.Name);
        return set.ToList();
    }

    public void Start()
    {
        if (Running) return;
        Running = true;
        Paused = false;
        cts = new CancellationTokenSource();
        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                Status = "Собираю названия…";
                // Индексы мобов/NPC могут ещё грузиться
                for (var i = 0; i < 30 && (WikiService.Instance.MobIndex() is null || WikiService.Instance.NpcIndex() is null); i++)
                    await Task.Delay(1000, token).ConfigureAwait(false);
                var todo = Collect().Where(n => !ru.ContainsKey(n)).OrderBy(n => n.Length).ToList();
                Total = todo.Count;
                Done = 0;
                if (todo.Count == 0) { Status = $"Все названия переведены ({ru.Count:N0})."; return; }

                var batch = new List<string>();
                var len = 0;
                var sinceSave = 0;
                var failures = 0;
                for (var i = 0; i <= todo.Count; i++)
                {
                    var flush = i == todo.Count || len + todo[i].Length > 3500 || batch.Count >= 90;
                    if (flush && batch.Count > 0)
                    {
                        while (Paused && !token.IsCancellationRequested) { Status = "Пауза"; await Task.Delay(500, token).ConfigureAwait(false); }
                        Status = $"Перевожу названия: {Done:N0} из {Total:N0}";
                        var ok = await TranslateBatch(batch, token).ConfigureAwait(false);
                        if (!ok)
                        {
                            failures++;
                            if (failures >= 6) { Status = "Google ограничил запросы — продолжу позже (кнопка «Продолжить»)."; break; }
                            Status = $"Google просит подождать — пауза {30 * failures} с…";
                            await Task.Delay(30_000 * failures, token).ConfigureAwait(false);
                            i -= batch.Count + 1;   // повторить пачку
                            if (i < -1) i = -1;
                            batch.Clear(); len = 0;
                            continue;
                        }
                        failures = 0;
                        Done += batch.Count;
                        batch.Clear(); len = 0;
                        if (++sinceSave >= 15) { Save(); sinceSave = 0; }
                        await Task.Delay(1300, token).ConfigureAwait(false);
                    }
                    if (i < todo.Count) { batch.Add(todo[i]); len += todo[i].Length + 1; }
                }
                Save();
                if (Done >= Total) Status = $"Готово: переведено {Done:N0}, всего {ru.Count:N0}.";
            }
            catch (OperationCanceledException) { Status = "Перевод остановлен."; Save(); }
            catch (Exception e)
            {
                Plugin.Log.Warning(e, "Русские названия: ошибка");
                Status = "Ошибка: " + e.Message;
                Save();
            }
            finally { Running = false; }
        });
    }

    public void Stop() => cts?.Cancel();

    private static DateTime resumeAt = DateTime.Now.AddSeconds(40);

    /// <summary>Если перевод уже начинали — продолжить через 40 с после запуска плагина.</summary>
    public static void AutoResume()
    {
        if (resumeAt == DateTime.MaxValue || DateTime.Now < resumeAt) return;
        resumeAt = DateTime.MaxValue;
        if (Instance is { Running: false, Count: > 0 } && OfflineDb.Instance?.Installed == true) Instance.Start();
    }

    private async Task<bool> TranslateBatch(List<string> lines, CancellationToken token)
    {
        try
        {
            var source = string.Join("\n", lines.Select(TermDictionary.Apply));
            using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("q", source) });
            using var resp = await http.PostAsync("https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=ru&dt=t", content, token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return false;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false));
            var sb = new StringBuilder();
            foreach (var part in doc.RootElement[0].EnumerateArray())
                if (part.ValueKind == JsonValueKind.Array && part[0].ValueKind == JsonValueKind.String)
                    sb.Append(part[0].GetString());
            var outLines = sb.ToString().Split('\n');
            if (outLines.Length != lines.Count)
            {
                // Разбилось иначе — переводим по половинкам
                if (lines.Count == 1) { ru[lines[0]] = sb.ToString().Trim(); return true; }
                var half = lines.Count / 2;
                return await TranslateBatch(lines.Take(half).ToList(), token).ConfigureAwait(false)
                       && await TranslateBatch(lines.Skip(half).ToList(), token).ConfigureAwait(false);
            }
            for (var i = 0; i < lines.Count; i++)
            {
                var t = outLines[i].Trim();
                if (t.Length > 0) ru[lines[i]] = t;
            }
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            Plugin.Log.Debug($"Русские названия: {e.Message}");
            return false;
        }
    }

    private readonly object saveLock = new();

    public void Save()
    {
        try
        {
            lock (saveLock)
                File.WriteAllText(file, JsonSerializer.Serialize(ru.ToDictionary(k => k.Key, v => v.Value)));
        }
        catch (Exception e) { Plugin.Log.Warning(e, "Русские названия: не удалось сохранить"); }
    }

    public void Clear()
    {
        Stop();
        ru.Clear();
        try { if (File.Exists(file)) File.Delete(file); } catch { /* ignore */ }
        Status = "Переводы удалены.";
    }

    public void Dispose()
    {
        cts?.Cancel();
        http.Dispose();
    }
}
