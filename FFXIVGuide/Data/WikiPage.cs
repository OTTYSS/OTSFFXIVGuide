using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FFXIVGuide.Data;

/// <summary>
/// Сырой текст страниц ffxiv.consolegameswiki.com с кешем на диске (обновляется раз в неделю или по запросу).
/// </summary>
public static class WikiPage
{
    private static readonly HttpClient Http = Net.Create(TimeSpan.FromSeconds(30));
    private static readonly ConcurrentDictionary<string, (string? Text, DateTime When)> cache = new();
    private static readonly ConcurrentDictionary<string, string> errors = new();
    private static readonly ConcurrentDictionary<string, byte> loading = new();

    static WikiPage() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("OTSFFXIVGuide (Dalamud plugin)");

    private static string FileFor(string title) =>
        Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "wiki", "pages", Regex.Replace(title, @"[^\w\-]", "_") + ".txt");

    public static bool IsLoading(string title) => loading.ContainsKey(title);
    public static string? Error(string title) => errors.TryGetValue(title, out var e) ? e : null;
    public static DateTime? Fetched(string title) => cache.TryGetValue(title, out var c) && c.Text is not null ? c.When : null;

    /// <summary>Текст страницы или null, пока грузится. Начинает загрузку при необходимости.</summary>
    public static string? Get(string title, bool force = false)
    {
        if (!force && cache.TryGetValue(title, out var c) && c.Text is not null && DateTime.Now - c.When < TimeSpan.FromDays(7)) return c.Text;
        var file = FileFor(title);
        if (!force && !cache.ContainsKey(title) && File.Exists(file))
        {
            try
            {
                var t = File.ReadAllText(file);
                cache[title] = (t, File.GetLastWriteTime(file));
                if (DateTime.Now - File.GetLastWriteTime(file) < TimeSpan.FromDays(7)) return t;
            }
            catch { /* перекачаем */ }
        }
        if (loading.TryAdd(title, 0))
        {
            errors.TryRemove(title, out _);
            Task.Run(async () =>
            {
                try
                {
                    var url = $"https://ffxiv.consolegameswiki.com/mediawiki/index.php?title={Uri.EscapeDataString(title)}&action=raw";
                    var text = await Http.GetStringAsync(url).ConfigureAwait(false);
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    await File.WriteAllTextAsync(file, text).ConfigureAwait(false);
                    cache[title] = (text, DateTime.Now);
                }
                catch (Exception e)
                {
                    Plugin.Log.Warning(e, $"Вики: не удалось загрузить {title}");
                    errors[title] = e.Message;
                }
                finally { loading.TryRemove(title, out _); }
            });
        }
        return cache.TryGetValue(title, out var old) ? old.Text : null;
    }

    /// <summary>Вики-разметка → обычный текст.</summary>
    public static string Clean(string s)
    {
        s = s.Replace("&harr;", "↔").Replace("&larr;", "←").Replace("&rarr;", "→").Replace("&nbsp;", " ");
        s = Regex.Replace(s, @"\{\{(?:weather icon|map icon)\|[^}]*\}\}", "");
        s = Regex.Replace(s, @"\{\{coords\|([^}]*)\}\}", m => "(" + m.Groups[1].Value.Replace(",", ", ").Replace(",  ", ", ") + ")");
        s = Regex.Replace(s, @"\{\{[^|}]*\|([^|}]*)(?:\|[^}]*)?\}\}", "$1");
        s = Regex.Replace(s, @"\{\{[^}]*\}\}", "");
        s = Regex.Replace(s, @"\[\[[^\]|]*\|([^\]]*)\]\]", "$1");
        s = Regex.Replace(s, @"\[\[([^\]]*)\]\]", "$1");
        s = Regex.Replace(s, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<[^>]+>", "");
        s = s.Replace("'''", "").Replace("''", "");
        return Regex.Replace(s, @"[ \t]{2,}", " ").Trim();
    }
}
