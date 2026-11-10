using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

/// <summary>
/// Снимок файлов игры: какие строки есть в основных таблицах. При каждом запуске сравнивается со снимком
/// прошлой версии игры — так видно, что добавил патч, даже если сайты ещё не обновились.
/// </summary>
public static class PatchSnapshot
{
    /// <summary>Таблицы, которые запоминаем: ключ, подпись и список «значимых» строк.</summary>
    public static readonly (string Key, string Title, Func<IEnumerable<uint>> Ids)[] Sheets =
    {
        ("item", "Предметы", () => Plugin.Data.GetExcelSheet<Item>().Where(r => !r.Name.IsEmpty).Select(r => r.RowId)),
        ("mount", "Маунты", () => Plugin.Data.GetExcelSheet<Mount>().Where(r => !r.Singular.IsEmpty).Select(r => r.RowId)),
        ("minion", "Минёны", () => Plugin.Data.GetExcelSheet<Companion>().Where(r => !r.Singular.IsEmpty).Select(r => r.RowId)),
        ("orchestrion", "Оркестрион", () => Plugin.Data.GetExcelSheet<Orchestrion>().Where(r => !r.Name.IsEmpty).Select(r => r.RowId)),
        ("card", "Карты Triple Triad", () => Plugin.Data.GetExcelSheet<TripleTriadCard>().Where(r => !r.Name.IsEmpty).Select(r => r.RowId)),
        ("emote", "Эмоции", () => Plugin.Data.GetExcelSheet<Emote>().Where(r => !r.Name.IsEmpty).Select(r => r.RowId)),
        ("quest", "Квесты", () => Plugin.Data.GetExcelSheet<Quest>().Where(r => !r.Name.IsEmpty).Select(r => r.RowId)),
        ("duty", "Контент (Duty Finder)", () => Plugin.Data.GetExcelSheet<ContentFinderCondition>().Where(r => !r.Name.IsEmpty).Select(r => r.RowId)),
        ("achievement", "Достижения", () => Plugin.Data.GetExcelSheet<Achievement>().Where(r => !r.Name.IsEmpty).Select(r => r.RowId)),
        ("recipe", "Рецепты", () => Plugin.Data.GetExcelSheet<Recipe>().Where(r => r.ItemResult.RowId != 0).Select(r => r.RowId)),
        ("fate", "FATE", () => Plugin.Data.GetExcelSheet<Fate>().Where(r => !r.Name.IsEmpty).Select(r => r.RowId)),
    };

    public static string GameVersion { get; private set; } = "";
    public static string? PreviousVersion { get; private set; }
    /// <summary>Новые строки по ключу таблицы (относительно снимка прошлой версии игры).</summary>
    public static Dictionary<string, List<uint>>? Added { get; private set; }
    public static bool Ready { get; private set; }
    public static string? Error { get; private set; }

    private static string Dir => Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "patches");

    public static void Start() => Task.Run(() =>
    {
        try { Take(); }
        catch (Exception e) { Error = e.Message; Plugin.Log.Warning(e, "Снимок патча: ошибка"); }
        finally { Ready = true; }
    });

    private static void Take()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? "";
        var verFile = Path.Combine(exeDir, "ffxivgame.ver");
        GameVersion = File.Exists(verFile) ? File.ReadAllText(verFile).Trim() : "";
        if (GameVersion.Length == 0) throw new InvalidDataException("не удалось узнать версию игры");

        Directory.CreateDirectory(Dir);
        var cur = Sheets.ToDictionary(s => s.Key, s => s.Ids().ToArray());
        var curFile = Path.Combine(Dir, GameVersion + ".json");
        if (!File.Exists(curFile)) File.WriteAllText(curFile, JsonSerializer.Serialize(cur));

        // Последний снимок более ранней версии игры
        var prev = Directory.EnumerateFiles(Dir, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(v => string.CompareOrdinal(v, GameVersion) < 0)
            .OrderByDescending(v => v, StringComparer.Ordinal).FirstOrDefault();
        if (prev is null) return;
        var old = JsonSerializer.Deserialize<Dictionary<string, uint[]>>(File.ReadAllText(Path.Combine(Dir, prev + ".json"))) ?? new();
        PreviousVersion = prev;
        Added = cur.ToDictionary(kv => kv.Key, kv =>
        {
            var before = old.TryGetValue(kv.Key, out var o) ? new HashSet<uint>(o) : new HashSet<uint>();
            return kv.Value.Where(id => !before.Contains(id)).ToList();
        });
    }
}
