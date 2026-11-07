using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FFXIVGuide.Data;

/// <summary>
/// Офлайн-база вики: файлы Teamcraft (GitHub) и индексы Garland Tools, скачанные на диск.
/// «Скачать» — всё сразу, «Обновить» — только изменившиеся файлы (по SHA из GitHub).
/// После загрузки вики работает без сети; Garland остаётся запасным источником.
/// </summary>
public sealed class OfflineDb : IDisposable
{
    public static OfflineDb Instance { get; private set; } = null!;

    private const string Repo = "ffxiv-teamcraft/ffxiv-teamcraft";
    private const string Branch = "staging";
    private const string JsonPath = "libs/data/src/lib/json";

    /// <summary>Нужные файлы Teamcraft (без дублей с файлами игры и служебных файлов сайта).</summary>
    public static readonly string[] TeamcraftFiles =
    {
        "shops.json", "npcs.json", "monsters.json", "quests.json",
        "drop-sources.json", "instance-sources.json", "reverse-instance-sources.json", "loot-sources.json",
        "quest-sources.json", "reduction.json", "desynth.json", "venture-sources.json", "fate-sources.json",
        "fishing-sources.json", "fishing-spots.json", "spearfishing-sources.json", "gardening-sources.json",
        "voyage-sources.json", "mogstation-sources.json",
        "collectables.json", "submarine-parts.json", "island-animals.json",
    };

    /// <summary>Индексы Garland Tools (по одному файлу).</summary>
    public static readonly (string File, string Url)[] GarlandFiles =
    {
        ("garland-mob.json", "https://www.garlandtools.org/db/doc/browse/en/2/mob.json"),
        ("garland-npc.json", "https://www.garlandtools.org/db/doc/browse/en/2/npc.json"),
        ("garland-fate.json", "https://www.garlandtools.org/db/doc/browse/en/2/fate.json"),
    };

    private readonly HttpClient http;
    public string Dir { get; }
    private string ManifestFile => Path.Combine(Dir, "manifest.json");

    public sealed class Manifest
    {
        public Dictionary<string, string> Sha { get; set; } = new();
        public DateTime Updated { get; set; }
    }

    public Manifest Info { get; private set; } = new();

    // Состояние загрузки
    public bool Busy { get; private set; }
    public string Status { get; private set; } = "";
    public float ProgressValue { get; private set; }
    public bool Installed => File.Exists(ManifestFile) && File.Exists(Path.Combine(Dir, "shops.json"));

    /// <summary>Есть ли файлы, которые появились в новой версии плагина и ещё не скачаны.</summary>
    public bool MissingFiles => Installed && TeamcraftFiles.Any(f => !File.Exists(Path.Combine(Dir, f)));
    public long SizeOnDisk { get; private set; }
    private CancellationTokenSource? cts;

    // Индексы в памяти
    public OfflineIndex? Index { get; private set; }

    /// <summary>Меняется после загрузки, обновления или удаления базы — разделы перечитывают свои файлы.</summary>
    public static int Version { get; private set; }
    public bool Loading { get; private set; }

    public OfflineDb()
    {
        Instance = this;
        http = Net.Create(TimeSpan.FromMinutes(5));
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FFXIVGuide/2.1 (Dalamud plugin)");
        Dir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "db");
        try
        {
            Directory.CreateDirectory(Dir);
            if (File.Exists(ManifestFile))
                Info = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(ManifestFile)) ?? new();
        }
        catch (Exception e) { Plugin.Log.Warning(e, "База: не удалось прочитать манифест"); }
        RecalcSize();
        if (Installed) LoadIndexAsync();
    }

    private void RecalcSize()
    {
        try { SizeOnDisk = Directory.EnumerateFiles(Dir).Sum(f => new FileInfo(f).Length); }
        catch { SizeOnDisk = 0; }
    }

    public void Cancel() => cts?.Cancel();

    /// <summary>Скачать или обновить базу. force — перекачать всё.</summary>
    public void Update(bool force)
    {
        if (Busy) return;
        Busy = true;
        cts = new CancellationTokenSource();
        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                Status = "Проверяю версию на GitHub…";
                ProgressValue = 0;
                var remote = await RemoteShas(token).ConfigureAwait(false);
                var need = TeamcraftFiles
                    .Where(f => force || !File.Exists(Path.Combine(Dir, f)) || !Info.Sha.TryGetValue(f, out var s) || !remote.TryGetValue(f, out var r) || s != r)
                    .ToList();
                var total = need.Count + GarlandFiles.Length;
                var done = 0;
                foreach (var f in need)
                {
                    token.ThrowIfCancellationRequested();
                    Status = $"Скачиваю {f} ({done + 1}/{total})";
                    var url = $"https://raw.githubusercontent.com/{Repo}/{Branch}/{JsonPath}/{f}";
                    await Download(url, f, token).ConfigureAwait(false);
                    if (remote.TryGetValue(f, out var sha)) Info.Sha[f] = sha;
                    done++;
                    ProgressValue = (float)done / total;
                }
                foreach (var (file, url) in GarlandFiles)
                {
                    token.ThrowIfCancellationRequested();
                    Status = $"Скачиваю {file} ({done + 1}/{total})";
                    try { await Download(url, file, token).ConfigureAwait(false); }
                    catch (Exception e) when (e is not OperationCanceledException) { Plugin.Log.Warning(e, $"База: {file} не скачан"); }
                    done++;
                    ProgressValue = (float)done / total;
                }
                Info.Updated = DateTime.Now;
                await File.WriteAllTextAsync(ManifestFile, JsonSerializer.Serialize(Info), token).ConfigureAwait(false);
                Status = need.Count == 0 ? "Файлы Teamcraft уже актуальны, индексы Garland обновлены." : $"Готово: обновлено файлов {need.Count + GarlandFiles.Length}.";
                RecalcSize();
                WikiService.Instance.ResetMemory();
                LoadIndexAsync();
                NameTranslations.Instance.Start();   // переводим новые названия
            }
            catch (OperationCanceledException)
            {
                Status = "Загрузка отменена.";
            }
            catch (Exception e)
            {
                Plugin.Log.Warning(e, "База: ошибка загрузки");
                Status = "Ошибка: " + e.Message;
            }
            finally
            {
                Busy = false;
                ProgressValue = 0;
            }
        });
    }

    private async Task<Dictionary<string, string>> RemoteShas(CancellationToken token)
    {
        var map = new Dictionary<string, string>();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/contents/{JsonPath}?ref={Branch}");
            using var resp = await http.SendAsync(req, token).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false));
            foreach (var f in doc.RootElement.EnumerateArray())
                if (f.TryGetProperty("name", out var n) && f.TryGetProperty("sha", out var s))
                    map[n.GetString() ?? ""] = s.GetString() ?? "";
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Plugin.Log.Warning(e, "База: GitHub не ответил, скачиваю всё заново");
        }
        return map;
    }

    private async Task Download(string url, string file, CancellationToken token)
    {
        var tmp = Path.Combine(Dir, file + ".part");
        using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using var dst = File.Create(tmp);
            await src.CopyToAsync(dst, token).ConfigureAwait(false);
        }
        File.Move(tmp, Path.Combine(Dir, file), true);
    }

    public void Delete()
    {
        if (Busy) return;
        Index = null;
        try { foreach (var f in Directory.EnumerateFiles(Dir)) File.Delete(f); }
        catch (Exception e) { Plugin.Log.Warning(e, "База: не удалось удалить"); }
        Info = new Manifest();
        Version++;
        RecalcSize();
        WikiService.Instance.ResetMemory();
        Status = "База удалена.";
    }

    public void LoadIndexAsync()
    {
        if (Loading) return;
        Loading = true;
        _ = Task.Run(() =>
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Index = OfflineIndex.Build(Dir);
                Version++;
                Plugin.Log.Info($"Офлайн-база загружена за {sw.ElapsedMilliseconds} мс");
                WikiService.Instance.ResetMemory();
            }
            catch (Exception e)
            {
                Plugin.Log.Warning(e, "База: не удалось прочитать файлы — скачай заново");
                Status = "Файлы базы повреждены — нажми «Скачать заново».";
            }
            finally { Loading = false; }
        });
    }

    public void Dispose()
    {
        cts?.Cancel();
        http.Dispose();
    }
}
