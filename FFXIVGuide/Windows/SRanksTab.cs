using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>
/// S-ранги охоты: условия появления, зона и время респауна. Таблица берётся с ffxiv.consolegameswiki.com
/// (там её поддерживает сообщество) и сохраняется на диск, чтобы работать без сети.
/// </summary>
internal sealed class SRanksTab
{
    private sealed record SRank(string Expansion, string Name, string Area, string Trigger, string Respawn);

    private const string Url = "https://ffxiv.consolegameswiki.com/mediawiki/index.php?title=Elite_Marks&action=raw";
    private static readonly System.Net.Http.HttpClient Http = Net.Create(TimeSpan.FromSeconds(30));

    private List<SRank>? ranks;
    private bool loading;
    private string? error;
    private DateTime? fetched;
    private string search = "";
    private bool translate = true;

    private static string CachePath => Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "sranks.txt");

    static SRanksTab() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("OTSFFXIVGuide (Dalamud plugin)");

    private void Load(bool force)
    {
        if (loading) return;
        if (!force && File.Exists(CachePath))
        {
            try
            {
                ranks = Parse(File.ReadAllText(CachePath));
                fetched = File.GetLastWriteTime(CachePath);
                if (DateTime.Now - fetched < TimeSpan.FromDays(7)) return;
            }
            catch { /* перекачаем */ }
        }
        loading = true;
        error = null;
        Task.Run(async () =>
        {
            try
            {
                var text = await Http.GetStringAsync(Url).ConfigureAwait(false);
                var parsed = Parse(text);
                if (parsed.Count == 0) throw new InvalidDataException("в таблице не нашлось S-рангов");
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                await File.WriteAllTextAsync(CachePath, text).ConfigureAwait(false);
                ranks = parsed;
                fetched = DateTime.Now;
            }
            catch (Exception e)
            {
                Plugin.Log.Warning(e, "S-ранги: не удалось загрузить таблицу");
                error = e.Message;
            }
            finally { loading = false; }
        });
    }

    private static readonly Regex Row = new(@"^\|\s*\[\[(?<name>[^\]]+)\]\]\s*\|\|\s*S\s*\|\|\s*(?<area>.+?)\s*\|\|\s*(?<trig>.+?)\s*\|\|\s*(?<resp>.+?)\s*$", RegexOptions.Compiled);

    private static List<SRank> Parse(string wiki)
    {
        var res = new List<SRank>();
        var exp = "";
        foreach (var raw in wiki.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var h = Regex.Match(line, @"^==\s*([^=].*?)\s*==\s*$");
            if (h.Success) { exp = h.Groups[1].Value; continue; }
            var m = Row.Match(line);
            if (!m.Success) continue;
            res.Add(new SRank(exp, Clean("[[" + m.Groups["name"].Value + "]]"), Clean(m.Groups["area"].Value), Clean(m.Groups["trig"].Value), Clean(m.Groups["resp"].Value)));
        }
        return res;
    }

    /// <summary>Вики-разметка → обычный текст.</summary>
    private static string Clean(string s)
    {
        s = Regex.Replace(s, @"\{\{(?:weather icon|map icon)\|[^}]*\}\}", "");
        s = Regex.Replace(s, @"\{\{coords\|([^}]*)\}\}", m => "(" + m.Groups[1].Value.Replace(",", ", ") + ")");
        s = Regex.Replace(s, @"\{\{[^|}]*\|([^|}]*)(?:\|[^}]*)?\}\}", "$1");
        s = Regex.Replace(s, @"\[\[[^\]|]*\|([^\]]*)\]\]", "$1");
        s = Regex.Replace(s, @"\[\[([^\]]*)\]\]", "$1");
        s = Regex.Replace(s, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
        s = s.Replace("'''", "").Replace("''", "");
        return Regex.Replace(s, @"\s{2,}", " ").Trim();
    }

    public IEnumerable<SearchHit> Search(string term, Action<MainTab> go)
    {
        if (ranks is null && !loading && error is null) Load(false);
        if (ranks is null) yield break;
        foreach (var r in ranks.Where(r => r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            yield return new SearchHit("S-ранги", r.Name, $"{r.Area} — {r.Trigger}", 0, () => { search = r.Name; go(MainTab.SRanks); });
    }

    public void Draw()
    {
        Theme.Header("S-ранги охоты", "Как вызвать каждый S-ранг, где он появляется и когда снова будет доступен");
        if (ranks is null && !loading && error is null) Load(false);

        var here = "";
        if (PlayerInfo.Ready)
            here = Plugin.Data.GetExcelSheet<TerritoryType>(Dalamud.Game.ClientLanguage.English)
                .GetRowOrDefault(Plugin.ClientState.TerritoryType)?.PlaceName.ValueNullable?.Name.ExtractText() ?? "";

        Theme.Panel("srtop", () =>
        {
            ImGui.TextColored(Theme.Gray, "Респаун — сколько часов после смерти до открытия окна (после техобслуживания — короче). Пока окно не открылось, условие не сработает.");
            if (ranks is not null)
                ImGui.TextColored(Theme.TextDim, $"Данные: Console Games Wiki, обновлено {fetched:dd.MM.yyyy}. S-рангов: {ranks.Count}.");
            ImGui.SameLine();
            if (ImGui.SmallButton(loading ? "Загружаю…##srld" : "Обновить##srld") && !loading) Load(true);
            ImGui.SameLine();
            ImGui.Checkbox("Переводить условия", ref translate);
            if (error is not null) ImGui.TextColored(Theme.Red, $"Не удалось загрузить таблицу: {error}");
        });
        if (ranks is null)
        {
            if (loading) Theme.Panel("srload", () => ImGui.TextColored(Theme.TextDim, "Загружаю таблицу S-рангов…"));
            return;
        }

        ImGui.SetNextItemWidth(Math.Min(420 * Theme.S, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##srsearch", "Имя, зона или условие…", ref search, 64);
        var list = ranks.Where(r => search.Length == 0 || r.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                                    || r.Area.Contains(search, StringComparison.OrdinalIgnoreCase)
                                    || NameTranslations.Show(r.Area).Contains(search, StringComparison.OrdinalIgnoreCase)
                                    || r.Trigger.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        if (translate) Translator.Instance.Request(list.Select(r => r.Trigger));

        if (!ImGui.BeginChild("##sranks", Vector2.Zero)) { ImGui.EndChild(); return; }
        foreach (var g in list.OrderByDescending(r => r.Area.Equals(here, StringComparison.OrdinalIgnoreCase)).GroupBy(r => r.Area.Equals(here, StringComparison.OrdinalIgnoreCase) ? "Здесь, в текущей зоне" : r.Expansion))
        {
            Theme.SubHeader(g.Key);
            foreach (var r in g)
                Theme.Panel($"sr{r.Name}", () =>
                {
                    Theme.FaIcon(FontAwesomeIcon.Skull, Theme.Red);
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.Gold, NameTranslations.Show(r.Name));
                    ImGui.SameLine();
                    var resp = r.Respawn.Replace("Maintenance", "после ТО").Replace(",", ";");
                    ImGui.TextColored(Theme.TextDim, $"{NameTranslations.Show(r.Area)} · респаун {resp} ч");
                    var text = translate ? Translator.Instance.Get(r.Trigger) ?? r.Trigger : r.Trigger;
                    ImGui.PushTextWrapPos(0);
                    ImGui.TextColored(Theme.Text, "Условие: " + text);
                    ImGui.PopTextWrapPos();
                    if (translate && Translator.Instance.Get(r.Trigger) is not null && ImGui.IsItemHovered())
                        ImGui.SetTooltip(r.Trigger);
                });
        }
        ImGui.EndChild();
    }
}
