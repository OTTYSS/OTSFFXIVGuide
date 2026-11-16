using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dalamud.Game;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

/// <summary>
/// Русские названия от сообщества XIV Rus (https://xivrus.ru, открытый репозиторий переводов на GitHub).
/// Берём только уже переведённые строки и сопоставляем их с английскими названиями игры по номеру строки таблицы.
/// Они важнее машинного перевода: что есть у XIV Rus — показываем их вариант, остальное переводит Google.
/// </summary>
public static class XivRusNames
{
    private const string Base = "https://raw.githubusercontent.com/xivrus/xiv_ru_weblate/main/exd/";
    private static readonly ConcurrentDictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
    private static string FilePath => Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "ru-xivrus.json");

    public static int Count => names.Count;
    public static bool Busy { get; private set; }
    public static string Status { get; private set; } = "";
    public static DateTime? Updated { get; private set; }

    /// <summary>Таблица перевода: путь к файлу, английское название по номеру строки и какое поле из перевода брать.</summary>
    private sealed record Sheet(string Path, string Title, Func<uint, string?> English, int Field = 0);

    private static string? Cap(string? s) => string.IsNullOrEmpty(s) ? null : char.ToUpperInvariant(s[0]) + s[1..];

    private static List<Sheet> Sheets()
    {
        string? Get<T>(uint id, Func<T, string> f) where T : struct, Lumina.Excel.IExcelRow<T> =>
            Plugin.Data.GetExcelSheet<T>(ClientLanguage.English).GetRowOrDefault(id) is { } r ? f(r) : null;
        return new()
        {
            new("Item/ItemName/ru.xlf", "предметы", id => Get<Item>(id, r => r.Name.ExtractText()), 2),
            new("Quest/ru.xlf", "квесты", id => Get<Quest>(id, r => r.Name.ExtractText())),
            new("ContentFinderCondition/ru.xlf", "данжи и бои", id => Cap(Get<ContentFinderCondition>(id, r => r.Name.ExtractText()))),
            new("PlaceName/ru.xlf", "зоны и места", id => Get<PlaceName>(id, r => r.Name.ExtractText())),
            new("Fate/ru.xlf", "FATE", id => Get<Fate>(id, r => r.Name.ExtractText())),
            new("BNpcName/ru.xlf", "мобы", id => Cap(Get<BNpcName>(id, r => r.Singular.ExtractText()))),
            new("ENpcResident/ru.xlf", "NPC", id => Get<ENpcResident>(id, r => r.Singular.ExtractText())),
            new("Mount/ru.xlf", "маунты", id => Cap(Get<Mount>(id, r => r.Singular.ExtractText()))),
            new("Companion/ru.xlf", "минионы", id => Cap(Get<Companion>(id, r => r.Singular.ExtractText()))),
            new("Status/ru.xlf", "статусы", id => Get<Lumina.Excel.Sheets.Status>(id, r => r.Name.ExtractText())),
            new("Achievement/ru.xlf", "достижения", id => Get<Achievement>(id, r => r.Name.ExtractText())),
            new("Action/ru.xlf", "навыки", id => Get<Lumina.Excel.Sheets.Action>(id, r => r.Name.ExtractText())),
            new("TripleTriadCard/ru.xlf", "карты Triple Triad", id => Get<TripleTriadCard>(id, r => r.Name.ExtractText())),
            new("BeastTribe/ru.xlf", "племена", id => Cap(Get<BeastTribe>(id, r => r.Name.ExtractText()))),
            new("Leve/ru.xlf", "левы", id => Get<Leve>(id, r => r.Name.ExtractText())),
            new("Emote/ru.xlf", "эмоции", id => Get<Emote>(id, r => r.Name.ExtractText())),
            new("Ornament/ru.xlf", "аксессуары", id => Cap(Get<Ornament>(id, r => r.Singular.ExtractText()))),
            new("Weather/ru.xlf", "погода", id => Get<Weather>(id, r => r.Name.ExtractText())),
        };
    }

    // Строго в пределах одной записи: <source> и сразу за ним <target> (иначе можно «перескочить» на соседнюю запись)
    private static readonly Regex Unit = new(@"<trans-unit id=""(\d+)""[^>]*>\s*<source>[^<]*</source>\s*<target state=""(?:translated|final|reviewed|signed-off)"">([^<]*)</target>", RegexOptions.Compiled);

    /// <summary>Русских букв не меньше, чем латинских (полупереводы вроде «Silver Circlet (аметист)» пропускаем).</summary>
    private static bool MostlyRussian(string s)
    {
        int cyr = 0, lat = 0;
        foreach (var c in s)
        {
            if (c is >= 'А' and <= 'я' or 'Ё' or 'ё') cyr++;
            else if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z') lat++;
        }
        return cyr > 0 && cyr >= lat;
    }

    public static string? Get(string en) => en.Length > 0 && names.TryGetValue(en, out var r) ? r : null;

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));
            if (d is null) return;
            foreach (var (k, v) in d) names[k] = v;
            Updated = File.GetLastWriteTime(FilePath);
        }
        catch (Exception e) { Plugin.Log.Warning(e, "XIV Rus: не удалось прочитать сохранённые названия"); }
    }

    /// <summary>Обновлять раз в неделю (переводы сообщества пополняются постоянно).</summary>
    public static void AutoUpdate()
    {
        if (!Plugin.Config.UseXivRus || Busy) return;
        if (Updated is { } u && DateTime.Now - u < TimeSpan.FromDays(7)) return;
        Update();
    }

    public static void Update()
    {
        if (Busy) return;
        Busy = true;
        Task.Run(async () =>
        {
            using var http = Net.Create(TimeSpan.FromSeconds(120));
            http.DefaultRequestHeaders.UserAgent.ParseAdd("OTSFFXIVGuide (Dalamud plugin)");
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var failed = 0;
            try
            {
                foreach (var sheet in Sheets())
                {
                    Status = $"Скачиваю перевод XIV Rus: {sheet.Title}…";
                    string text;
                    try { text = await http.GetStringAsync(Base + sheet.Path).ConfigureAwait(false); }
                    catch (Exception e)
                    {
                        failed++;
                        Plugin.Log.Warning(e, $"XIV Rus: не удалось скачать {sheet.Path}");
                        continue;
                    }
                    foreach (Match m in Unit.Matches(text))
                    {
                        var parts = WebUtility.HtmlDecode(m.Groups[2].Value).Split("<tab>");
                        var ru = parts.Length > sheet.Field && parts[sheet.Field].Trim().Length > 0 ? parts[sheet.Field].Trim() : parts[0].Trim();
                        // Строка помечена переведённой, но иногда там всё ещё английский — такие пропускаем
                        if (ru.Length == 0 || !MostlyRussian(ru)) continue;
                        if (!uint.TryParse(m.Groups[1].Value, out var id)) continue;
                        var en = sheet.English(id);
                        if (string.IsNullOrWhiteSpace(en)) continue;
                        result[en.Trim()] = char.ToUpperInvariant(ru[0]) + ru[1..];
                    }
                }
                if (result.Count == 0) { Status = "XIV Rus: не удалось получить переводы (нет сети?)."; return; }
                names.Clear();
                foreach (var (k, v) in result) names[k] = v;
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                await File.WriteAllTextAsync(FilePath, JsonSerializer.Serialize(result)).ConfigureAwait(false);
                Updated = DateTime.Now;
                Status = $"XIV Rus: {result.Count:N0} названий" + (failed > 0 ? $" (не скачалось таблиц: {failed})" : "") + ".";
            }
            catch (Exception e)
            {
                Plugin.Log.Warning(e, "XIV Rus: ошибка обновления");
                Status = "XIV Rus: ошибка — " + e.Message;
            }
            finally { Busy = false; }
        });
    }
}
