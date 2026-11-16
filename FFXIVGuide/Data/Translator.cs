using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FFXIVGuide.Data;

/// <summary>
/// Перевод английских текстов игры на русский прямо в плагине (Google Translate).
/// Отправляется только текст квеста. Результаты кешируются на время сессии.
/// </summary>
public sealed class Translator : IDisposable
{
    public static Translator Instance { get; } = new();

    private readonly HttpClient http = Net.Create(TimeSpan.FromSeconds(15));
    private readonly ConcurrentDictionary<string, string> done = new();
    private readonly ConcurrentDictionary<string, byte> failed = new();
    private readonly ConcurrentDictionary<string, byte> inFlight = new();
    // Очередь: переводим по одному с паузой, иначе Google отвечает «429 Too Many Requests»
    private readonly ConcurrentQueue<string> queue = new();
    private readonly ConcurrentDictionary<string, int> attempts = new();
    private int workerRunning;
    private DateTime pauseUntil = DateTime.MinValue;
    private int backoff;

    private string? file;
    private DateTime nextSave = DateTime.MaxValue;

    private Translator()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
    }

    /// <summary>Загрузить сохранённые переводы (вызывается при старте плагина).</summary>
    public void LoadFromDisk()
    {
        try
        {
            file = System.IO.Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "translations.json");
            if (System.IO.File.Exists(file))
                foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(file)) ?? new())
                    done[k] = v;
        }
        catch (Exception e) { Plugin.Log.Warning(e, "Перевод: не удалось прочитать сохранённые переводы"); }
    }

    /// <summary>Сохранить, если были новые переводы (раз в 20 секунд).</summary>
    public void Tick()
    {
        if (DateTime.Now < nextSave || file is null) return;
        nextSave = DateTime.MaxValue;
        Save();
    }

    public void Save()
    {
        if (file is null) return;
        try { System.IO.File.WriteAllText(file, JsonSerializer.Serialize(done.ToDictionary(k => k.Key, v => v.Value))); }
        catch (Exception e) { Plugin.Log.Warning(e, "Перевод: не удалось сохранить"); }
    }

    public int Count => done.Count;

    /// <summary>Перевод, если уже готов.</summary>
    public string? Get(string text)
    {
        if (Plugin.Config.UseXivRus && XivRusNames.Text(text) is { } x) return x;
        return done.TryGetValue(text, out var t) ? t : null;
    }

    public bool IsLoading(string text) => inFlight.ContainsKey(text);
    public bool IsFailed(string text) => failed.ContainsKey(text);

    /// <summary>Запросить перевод (повторно не запрашивает).</summary>
    public void Request(IEnumerable<string> texts)
    {
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text) || done.ContainsKey(text) || failed.ContainsKey(text))
                continue;
            // Есть ручной перевод сообщества XIV Rus — в Google не отправляем
            if (Plugin.Config.UseXivRus && XivRusNames.Text(text) is not null)
                continue;
            if (!inFlight.TryAdd(text, 0))
                continue;
            queue.Enqueue(text);
        }
        if (!queue.IsEmpty && Interlocked.CompareExchange(ref workerRunning, 1, 0) == 0)
            _ = Task.Run(Worker);
    }

    private async Task Worker()
    {
        try
        {
            while (queue.TryDequeue(out var text))
            {
                var wait = pauseUntil - DateTime.Now;
                if (wait > TimeSpan.Zero) await Task.Delay(wait).ConfigureAwait(false);
                await Translate(text).ConfigureAwait(false);
                await Task.Delay(350).ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref workerRunning, 0);
            // Если пока заканчивали, добавили новое — запускаемся снова
            if (!queue.IsEmpty && Interlocked.CompareExchange(ref workerRunning, 1, 0) == 0)
                _ = Task.Run(Worker);
        }
    }

    public void RetryFailed() => failed.Clear();

    private const string RuPrefix = "ru:";

    /// <summary>Перевести русский текст на английский (для поиска по-русски).</summary>
    public void RequestEnglish(string russian) => Request([RuPrefix + russian]);

    public string? GetEnglish(string russian) => Get(RuPrefix + russian);

    /// <summary>Сбросить переводы (например, после изменения словаря).</summary>
    public void ClearCache()
    {
        done.Clear();
        failed.Clear();
        try { if (file is not null && System.IO.File.Exists(file)) System.IO.File.Delete(file); } catch { /* ignore */ }
    }

    private async Task Translate(string text)
    {
        var requeued = false;
        try
        {
            // Ключи с префиксом RuPrefix — обратный перевод (русский запрос → английский, для поиска)
            var toEnglish = text.StartsWith(RuPrefix, StringComparison.Ordinal);
            var source = toEnglish ? text[RuPrefix.Length..] : TermDictionary.Apply(text);
            var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={(toEnglish ? "ru" : "en")}&tl={(toEnglish ? "en" : "ru")}&dt=t&q=" + Uri.EscapeDataString(source);
            var json = await http.GetStringAsync(url).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder();
            foreach (var part in doc.RootElement[0].EnumerateArray())
                if (part.ValueKind == JsonValueKind.Array && part[0].ValueKind == JsonValueKind.String)
                    sb.Append(part[0].GetString());

            var result = sb.ToString().Trim();
            if (result.Length > 0)
            {
                done[text] = result;
                if (nextSave == DateTime.MaxValue) nextSave = DateTime.Now.AddSeconds(20);
            }
            else failed[text] = 0;
            backoff = 0;
        }
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            // Google просит подождать: пауза растёт (15 с, 30 с, … до 2 мин), текст возвращаем в очередь
            backoff = Math.Min(backoff + 1, 8);
            pauseUntil = DateTime.Now.AddSeconds(15 * backoff);
            if (attempts.AddOrUpdate(text, 1, (_, n) => n + 1) <= 5) { queue.Enqueue(text); requeued = true; }
            else failed[text] = 0;
            if (backoff == 1) Plugin.Log.Info("Перевод: Google просит подождать — пауза и повтор");
        }
        catch (Exception e)
        {
            if (attempts.AddOrUpdate(text, 1, (_, n) => n + 1) <= 2) { queue.Enqueue(text); requeued = true; }
            else
            {
                failed[text] = 0;
                Plugin.Log.Warning(e, "Перевод: не удалось перевести текст");
            }
        }
        finally
        {
            if (!requeued) inFlight.TryRemove(text, out _);
        }
    }

    public void Dispose()
    {
        Save();
        http.Dispose();
    }
}
