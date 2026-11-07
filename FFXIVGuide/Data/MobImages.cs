using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;

namespace FFXIVGuide.Data;

/// <summary>
/// Фото мобов и боссов с ffxiv.consolegameswiki.com. Берём только картинку, чьё имя файла
/// совпадает с именем моба — чтобы не показать чужого зверя со страницы-семейства.
/// Картинки и «картинки нет» кешируются на диск.
/// </summary>
public sealed class MobImages : IDisposable
{
    public static MobImages Instance { get; private set; } = null!;

    private const string Api = "https://ffxiv.consolegameswiki.com/mediawiki/api.php";

    private readonly HttpClient http = Net.Create(TimeSpan.FromSeconds(30));
    private readonly string dir;
    private readonly string indexFile;
    private readonly ConcurrentDictionary<string, string> urls = new(StringComparer.OrdinalIgnoreCase);  // имя → url ("" — нет)
    private readonly ConcurrentDictionary<string, IDalamudTextureWrap?> textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> inFlight = new(StringComparer.OrdinalIgnoreCase);

    public MobImages()
    {
        Instance = this;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FFXIVGuide/1.8 (Dalamud plugin)");
        dir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "wiki", "img");
        indexFile = Path.Combine(dir, "index.json");
        try
        {
            Directory.CreateDirectory(dir);
            if (File.Exists(indexFile))
                foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(indexFile)) ?? new())
                    urls[k] = v;
        }
        catch (Exception e) { Plugin.Log.Warning(e, "Фото мобов: не удалось прочитать кеш"); }
    }

    public enum State { Loading, None, Ready }

    /// <summary>Картинка моба: Ready — есть текстура, None — фото нет, Loading — грузится.</summary>
    public (State State, IDalamudTextureWrap? Texture) Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (State.None, null);
        if (textures.TryGetValue(name, out var t)) return t is null ? (State.None, null) : (State.Ready, t);
        if (inFlight.TryAdd(name, 0)) _ = Task.Run(() => Load(name));
        return (State.Loading, null);
    }

    private static string FileKey(string name) =>
        string.Concat(name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_'));

    private static string Norm(string s) =>
        new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private async Task Load(string name)
    {
        try
        {
            var file = Path.Combine(dir, FileKey(name) + ".img");
            if (!File.Exists(file))
            {
                if (!urls.TryGetValue(name, out var url))
                {
                    url = await FindUrl(name).ConfigureAwait(false) ?? "";
                    urls[name] = url;
                    SaveIndex();
                }
                if (url.Length == 0)
                {
                    textures[name] = null;
                    return;
                }
                var bytes = await http.GetByteArrayAsync(url).ConfigureAwait(false);
                await File.WriteAllBytesAsync(file, bytes).ConfigureAwait(false);
            }

            var data = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
            textures[name] = await Plugin.Textures.CreateFromImageAsync(data).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, $"Фото мобов: не удалось загрузить {name}");
            textures[name] = null;
        }
        finally
        {
            inFlight.TryRemove(name, out _);
        }
    }

    /// <summary>Ищет файл File:Имя.jpg/png, затем картинку с именем моба на его странице.</summary>
    private async Task<string?> FindUrl(string name)
    {
        // 1) Файл, названный ровно как моб
        var titles = string.Join("|", new[] { "jpg", "png", "jpeg" }.Select(ext => $"File:{name}.{ext}"));
        var direct = await Query($"action=query&titles={Uri.EscapeDataString(titles)}&prop=imageinfo&iiprop=url|size&iiurlwidth=360&format=json").ConfigureAwait(false);
        var hit = Pick(direct, name, exact: true);
        if (hit is not null) return hit;

        // 2) Картинки со страницы моба, но только если в имени файла есть имя моба
        var page = await Query($"action=query&titles={Uri.EscapeDataString(name)}&redirects=1&generator=images&gimlimit=50&prop=imageinfo&iiprop=url|size&iiurlwidth=360&format=json").ConfigureAwait(false);
        return Pick(page, name, exact: false);
    }

    private async Task<JsonDocument?> Query(string q)
    {
        try
        {
            var json = await http.GetStringAsync($"{Api}?{q}").ConfigureAwait(false);
            return JsonDocument.Parse(json);
        }
        catch (Exception e)
        {
            Plugin.Log.Debug($"Фото мобов: запрос не удался: {e.Message}");
            return null;
        }
    }

    private static string? Pick(JsonDocument? doc, string name, bool exact)
    {
        if (doc is null) return null;
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("query", out var q) || !q.TryGetProperty("pages", out var pages)) return null;
            var mob = Norm(name);
            var best = (Score: -1, Url: (string?)null);
            foreach (var p in pages.EnumerateObject())
            {
                var title = p.Value.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                if (!p.Value.TryGetProperty("imageinfo", out var ii) || ii.GetArrayLength() == 0) continue;
                var info = ii[0];
                var w = info.TryGetProperty("width", out var wv) ? wv.GetInt32() : 0;
                var h = info.TryGetProperty("height", out var hv) ? hv.GetInt32() : 0;
                if (w < 120 || h < 90) continue;                       // значки и иконки пропускаем
                var file = title.StartsWith("File:") ? title[5..] : title;
                var dot = file.LastIndexOf('.');
                var stem = Norm(dot > 0 ? file[..dot] : file);
                var ext = dot > 0 ? file[(dot + 1)..].ToLowerInvariant() : "";
                if (ext is not ("jpg" or "jpeg" or "png")) continue;
                if (stem.Contains("icon") || stem.Contains("concept") || stem.Contains("map")) continue;

                int score;
                if (stem == mob) score = 100;
                else if (!exact && stem.StartsWith(mob) && stem.Length - mob.Length <= 2) score = 80;  // Titan1.jpg
                else continue;
                if (score > best.Score)
                {
                    var url = info.TryGetProperty("thumburl", out var tu) ? tu.GetString() : null;
                    url ??= info.TryGetProperty("url", out var u) ? u.GetString() : null;
                    if (url is not null) best = (score, url);
                }
            }
            return best.Url;
        }
    }

    private readonly object saveLock = new();

    private void SaveIndex()
    {
        try
        {
            lock (saveLock)
                File.WriteAllText(indexFile, JsonSerializer.Serialize(urls.ToDictionary(k => k.Key, v => v.Value)));
        }
        catch { /* кеш не обязателен */ }
    }

    public void ClearCache()
    {
        foreach (var t in textures.Values) t?.Dispose();
        textures.Clear();
        urls.Clear();
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir)) File.Delete(f);
        }
        catch (Exception e) { Plugin.Log.Warning(e, "Фото мобов: не удалось очистить кеш"); }
    }

    public void Dispose()
    {
        foreach (var t in textures.Values) t?.Dispose();
        textures.Clear();
        http.Dispose();
    }
}
