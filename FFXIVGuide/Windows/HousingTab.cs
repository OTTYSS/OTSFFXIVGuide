using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>
/// Свободные участки под дом: район, размер, цена, лотерея и место на карте района.
/// Данные — PaissaDB (их собирают игроки с плагином PaissaHouse), карта и позиции участков — из файлов игры.
/// </summary>
internal sealed class HousingTab
{
    private const string Api = "https://paissadb.zhu.codes";
    private static readonly System.Net.Http.HttpClient Http = Net.Create(TimeSpan.FromSeconds(30));
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    static HousingTab() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("OTSFFXIVGuide (Dalamud plugin)");

    private sealed record WorldInfo(uint Id, string Name, uint DatacenterId, string DatacenterName);
    private sealed record Plot(uint WorldId, uint DistrictId, int WardNumber, int PlotNumber, int Size, long Price,
        double LastUpdatedTime, int PurchaseSystem, int? LottoEntries, int? LottoPhase, long? LottoPhaseUntil);
    private sealed record District(uint Id, string Name, List<Plot> OpenPlots);
    private sealed record WorldDetail(uint Id, string Name, List<District> Districts);

    private sealed record Row(Plot P, string World, string District);

    // Районы: территория → русское название
    private static readonly (uint Id, string Name)[] Districts =
    {
        (339, "Mist"), (340, "Lavender Beds"), (341, "Goblet"), (641, "Shirogane"), (979, "Empyreum"),
    };
    private static readonly string[] SizeNames = { "S", "M", "L" };

    private List<WorldInfo>? worlds;
    private List<Row>? rows;
    private bool loading;
    private string? error;
    private DateTime fetched;
    private uint worldId;          // выбранный мир (0 — ещё не выбран)
    private bool wholeDc;
    private int districtFilter;    // 0 — все
    private int sizeFilter;        // 0 — все
    private int buyerFilter;       // 0 — все, 1 — для себя, 2 — для FC
    private bool onlyOpenEntry;
    private (uint World, uint District, int Ward, int Plot)? selected;

    private void LoadWorlds()
    {
        if (loading) return;
        loading = true;
        error = null;
        Task.Run(async () =>
        {
            try
            {
                worlds = (await Http.GetFromJsonAsync<List<WorldInfo>>($"{Api}/worlds", Json).ConfigureAwait(false) ?? new())
                    .OrderBy(w => w.DatacenterName).ThenBy(w => w.Name).ToList();
            }
            catch (Exception e)
            {
                Plugin.Log.Warning(e, "Жильё: не удалось получить список миров");
                error = e.Message;
            }
            finally { loading = false; }
        });
    }

    private void LoadPlots()
    {
        if (loading || worlds is null || worldId == 0) return;
        var target = worlds.FirstOrDefault(w => w.Id == worldId);
        if (target is null) return;
        var ids = wholeDc ? worlds.Where(w => w.DatacenterId == target.DatacenterId).Select(w => w.Id).ToList() : new List<uint> { worldId };
        loading = true;
        error = null;
        Task.Run(async () =>
        {
            try
            {
                var tasks = ids.Select(id => Http.GetFromJsonAsync<WorldDetail>($"{Api}/worlds/{id}", Json)).ToList();
                var res = new List<Row>();
                foreach (var w in await Task.WhenAll(tasks).ConfigureAwait(false))
                    if (w is not null)
                        foreach (var d in w.Districts)
                            foreach (var p in d.OpenPlots)
                                res.Add(new Row(p, w.Name, DistrictName(d.Id, d.Name)));
                rows = res;
                fetched = DateTime.Now;
            }
            catch (Exception e)
            {
                Plugin.Log.Warning(e, "Жильё: не удалось получить участки");
                error = e.Message;
            }
            finally { loading = false; }
        });
    }

    private static string DistrictName(uint id, string fallback) =>
        Districts.FirstOrDefault(d => d.Id == id).Name ?? fallback;

    private static DateTime Local(long unix) => DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;

    private static string Ago(double unix)
    {
        var t = DateTime.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds((long)(unix * 1000)).UtcDateTime;
        if (t.TotalMinutes < 60) return $"{Math.Max(1, (int)t.TotalMinutes)} мин назад";
        if (t.TotalHours < 48) return $"{(int)t.TotalHours} ч назад";
        return $"{(int)t.TotalDays} дн назад";
    }

    private static string Gil(long v) => v.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");

    /// <summary>Стадия лотереи для участка с учётом устаревших данных.</summary>
    private static (string Text, Vector4 Color) Phase(Plot p)
    {
        if ((p.PurchaseSystem & 1) == 0) return ("Кто первый купит", Theme.Blue);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (p.LottoPhase is null || p.LottoPhaseUntil is null || p.LottoPhaseUntil < now)
            return ("Стадия неизвестна (данные устарели)", Theme.Gray);
        return p.LottoPhase switch
        {
            1 => ($"Приём заявок до {Local(p.LottoPhaseUntil.Value):dd.MM HH:mm}", Theme.Green),
            2 => ($"Итоги: победитель оформляет покупку до {Local(p.LottoPhaseUntil.Value):dd.MM HH:mm}", Theme.Yellow),
            _ => ($"Заявки не принимаются до {Local(p.LottoPhaseUntil.Value):dd.MM HH:mm}", Theme.Red),
        };
    }

    private static string Buyers(int ps) => (ps & 6) switch
    {
        6 => "для себя и FC",
        4 => "только для себя",
        2 => "только для FC",
        _ => "—",
    };

    public void Draw()
    {
        Theme.Header("Участки под дом", "Свободные участки в жилых районах: цена, размер, лотерея и место на карте");
        if (worlds is null && !loading && error is null) LoadWorlds();

        if (worlds is not null && worldId == 0)
        {
            var mine = Plugin.PlayerState.CurrentWorld.ValueNullable?.Name.ExtractText();
            worldId = worlds.FirstOrDefault(w => w.Name == mine)?.Id ?? worlds.First().Id;
            LoadPlots();
        }
        // Обновляем раз в 10 минут, пока вкладка открыта
        if (rows is not null && !loading && DateTime.Now - fetched > TimeSpan.FromMinutes(10)) LoadPlots();

        Theme.Panel("hstop", () =>
        {
            if (worlds is not null)
            {
                var cur = worlds.FirstOrDefault(w => w.Id == worldId);
                ImGui.SetNextItemWidth(220 * Theme.S);
                if (ImGui.BeginCombo("##hsworld", cur is null ? "Мир" : $"{cur.Name} ({cur.DatacenterName})", ImGuiComboFlags.HeightLarge))
                {
                    foreach (var dc in worlds.GroupBy(w => w.DatacenterName))
                    {
                        ImGui.TextColored(Theme.Gold, dc.Key);
                        foreach (var w in dc)
                            if (ImGui.Selectable($"   {w.Name}##hw{w.Id}", w.Id == worldId))
                            {
                                worldId = w.Id;
                                selected = null;
                                LoadPlots();
                            }
                    }
                    ImGui.EndCombo();
                }
                ImGui.SameLine();
                if (ImGui.Checkbox("Весь дата-центр", ref wholeDc)) LoadPlots();
                ImGui.SameLine();
                if (ImGui.SmallButton(loading ? "Загружаю…##hsr" : "Обновить##hsr") && !loading) LoadPlots();
            }
            ImGui.TextColored(Theme.TextDim, "Данные: PaissaDB. Их присылают игроки с плагином PaissaHouse, поэтому участок может быть уже занят. Свежесть указана у каждого.");
            ImGui.TextColored(Theme.TextDim, "Лотерея идёт циклами по 9 дней: 5 дней приём заявок, 4 дня — итоги и оформление покупки победителем.");
            if (rows is not null) ImGui.TextColored(Theme.TextDim, $"Обновлено: {fetched:HH:mm}.");
            if (error is not null) ImGui.TextColored(Theme.Red, $"Не удалось загрузить: {error}");
        });

        if (rows is null)
        {
            if (loading) Theme.Panel("hsload", () => ImGui.TextColored(Theme.TextDim, "Загружаю участки…"));
            return;
        }

        DrawLotterySummary();

        var dNames = new[] { "Все районы" }.Concat(Districts.Select(d => d.Name)).ToArray();
        Theme.Chips("hsdist", dNames, ref districtFilter);
        Theme.Chips("hssize", new[] { "Любой размер", "S", "M", "L" }, ref sizeFilter);
        Theme.Chips("hsbuy", new[] { "Все", "Можно для себя", "Можно для FC" }, ref buyerFilter);
        ImGui.Checkbox("Только где сейчас принимают заявки", ref onlyOpenEntry);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var list = rows.Where(r =>
                (districtFilter == 0 || r.P.DistrictId == Districts[districtFilter - 1].Id)
                && (sizeFilter == 0 || r.P.Size == sizeFilter - 1)
                && (buyerFilter == 0 || (r.P.PurchaseSystem & (buyerFilter == 1 ? 4 : 2)) != 0)
                && (!onlyOpenEntry || ((r.P.PurchaseSystem & 1) != 0 && r.P.LottoPhase == 1 && r.P.LottoPhaseUntil >= now)))
            .OrderByDescending(r => r.P.Size).ThenBy(r => r.P.Price).ToList();

        if (list.Count == 0)
        {
            Theme.EmptyState(FontAwesomeIcon.Home, "Свободных участков нет", "Под эти фильтры ничего не нашлось. Попробуй другой район, размер или весь дата-центр.");
            return;
        }

        var avail = ImGui.GetContentRegionAvail();
        var leftW = selected is null ? avail.X : Theme.SplitWidth("hssplit", avail.X, avail.X * 0.55f);
        ImGui.BeginChild("##hslist", new Vector2(leftW, 0));
        DrawTable(list);
        ImGui.EndChild();

        var sel = selected is { } s ? list.FirstOrDefault(r => r.P.WorldId == s.World && r.P.DistrictId == s.District && r.P.WardNumber == s.Ward && r.P.PlotNumber == s.Plot) : null;
        if (sel is null) { selected = null; return; }
        ImGui.SameLine();
        Theme.Splitter("hssplit", avail.X, avail.X * 0.55f);
        ImGui.SameLine();
        ImGui.BeginChild("##hsdetail", new Vector2(0, 0));
        DrawDetail(sel);
        ImGui.EndChild();
    }

    private void DrawLotterySummary()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var fresh = rows!.Where(r => (r.P.PurchaseSystem & 1) != 0 && r.P.LottoPhase is not null && r.P.LottoPhaseUntil >= now).ToList();
        Theme.Panel("hslotto", () =>
        {
            var total = rows!.Count;
            var bySize = string.Join(" · ", Enumerable.Range(0, 3).Select(i => $"{SizeNames[i]}: {rows!.Count(r => r.P.Size == i)}"));
            ImGui.TextColored(Theme.Text, $"Свободно участков: {total} ({bySize})");
            if (fresh.Count == 0)
            {
                ImGui.TextColored(Theme.Gray, "Текущая стадия лотереи неизвестна: нет свежих данных.");
                return;
            }
            // Стадия всего цикла — по самым частым свежим данным
            var cycle = fresh.GroupBy(r => (r.P.LottoPhase, r.P.LottoPhaseUntil)).OrderByDescending(g => g.Count()).First().Key;
            var until = Local(cycle.LottoPhaseUntil!.Value);
            var left = until - DateTime.Now;
            var leftText = left.TotalHours >= 24 ? $"{(int)left.TotalDays} д {left.Hours} ч" : $"{(int)left.TotalHours} ч {left.Minutes} мин";
            var (text, color) = cycle.LottoPhase switch
            {
                1 => ($"Сейчас идёт приём заявок на лотерею — до {until:dd.MM HH:mm} (осталось {leftText}).", Theme.Green),
                2 => ($"Сейчас период итогов: заявки не принимаются, победители оформляют покупку до {until:dd.MM HH:mm} (осталось {leftText}).", Theme.Yellow),
                _ => ($"Заявки сейчас не принимаются, до {until:dd.MM HH:mm}.", Theme.Red),
            };
            ImGui.TextColored(color, text);
            var open = fresh.Count(r => r.P.LottoPhase == 1);
            if (open > 0) ImGui.TextColored(Theme.TextDim, $"Участков, где можно подать заявку: {open}.");
        });
    }

    private void DrawTable(List<Row> list)
    {
        var multiWorld = list.Select(r => r.P.WorldId).Distinct().Count() > 1;
        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("##hstable", multiWorld ? 7 : 6, flags)) return;
        if (multiWorld) ImGui.TableSetupColumn("Мир", ImGuiTableColumnFlags.None, 0.9f);
        ImGui.TableSetupColumn("Участок", ImGuiTableColumnFlags.None, 1.5f);
        ImGui.TableSetupColumn("Размер", ImGuiTableColumnFlags.None, 0.5f);
        ImGui.TableSetupColumn("Цена", ImGuiTableColumnFlags.None, 1f);
        ImGui.TableSetupColumn("Покупка", ImGuiTableColumnFlags.None, 1.8f);
        ImGui.TableSetupColumn("Заявок", ImGuiTableColumnFlags.None, 0.5f);
        ImGui.TableSetupColumn("Данные", ImGuiTableColumnFlags.None, 0.8f);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();
        foreach (var r in list)
        {
            var p = r.P;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (multiWorld)
            {
                ImGui.TextUnformatted(r.World);
                ImGui.TableNextColumn();
            }
            var key = (p.WorldId, p.DistrictId, p.WardNumber, p.PlotNumber);
            if (ImGui.Selectable($"{r.District}, ward {p.WardNumber + 1}, участок {p.PlotNumber + 1}##hp{p.WorldId}_{p.DistrictId}_{p.WardNumber}_{p.PlotNumber}",
                    selected == key, ImGuiSelectableFlags.SpanAllColumns))
                selected = selected == key ? null : key;
            ImGui.TableNextColumn();
            ImGui.TextColored(p.Size == 2 ? Theme.Gold : p.Size == 1 ? Theme.Blue : Theme.Text, SizeNames[Math.Clamp(p.Size, 0, 2)]);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Gil(p.Price));
            ImGui.TableNextColumn();
            var (ph, col) = Phase(p);
            ImGui.TextColored(col, ph);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted((p.PurchaseSystem & 1) != 0 && p.LottoEntries is { } n ? n.ToString() : "—");
            ImGui.TableNextColumn();
            ImGui.TextColored(Theme.TextDim, Ago(p.LastUpdatedTime));
        }
        ImGui.EndTable();
    }

    private static void DrawDetail(Row r)
    {
        var p = r.P;
        Theme.SubHeader($"{r.District}, ward {p.WardNumber + 1}, участок {p.PlotNumber + 1}");
        Theme.Panel("hsinfo", () =>
        {
            ImGui.TextColored(Theme.TextDim, "Мир:");
            ImGui.SameLine();
            ImGui.TextUnformatted(r.World);
            ImGui.TextColored(Theme.TextDim, "Размер:");
            ImGui.SameLine();
            ImGui.TextUnformatted(p.Size switch { 0 => "S (маленький)", 1 => "M (средний)", _ => "L (большой)" });
            ImGui.TextColored(Theme.TextDim, "Цена:");
            ImGui.SameLine();
            ImGui.TextColored(Theme.Gold, Gil(p.Price) + " гил");
            ImGui.TextColored(Theme.TextDim, "Кто может купить:");
            ImGui.SameLine();
            ImGui.TextUnformatted(Buyers(p.PurchaseSystem));
            ImGui.TextColored(Theme.TextDim, "Способ:");
            ImGui.SameLine();
            ImGui.TextUnformatted((p.PurchaseSystem & 1) != 0 ? "лотерея" : "кто первый купит");
            var (ph, col) = Phase(p);
            ImGui.TextColored(col, ph);
            if ((p.PurchaseSystem & 1) != 0 && p.LottoEntries is { } n)
                ImGui.TextColored(Theme.TextDim, n == 0 ? "Заявок пока нет." : $"Заявок: {n} — шанс выиграть примерно 1 из {n + 1}, если подать заявку.");
            ImGui.TextColored(Theme.TextDim, $"Данные обновлены {Ago(p.LastUpdatedTime)}.");
            ImGui.TextColored(Theme.TextDim, $"Как добраться: телепорт в город района → аэтернет «Residential District» → {r.District}, ward {p.WardNumber + 1}"
                + (p.PlotNumber >= 30 ? " (Subdivision)." : "."));
        });

        // Позиция участка из файлов игры
        var marker = PlotMarker(p.DistrictId, p.PlotNumber);
        if (marker is null) return;
        var (mapRow, x, z) = marker.Value;
        if (Theme.IconButton("hsflag", FontAwesomeIcon.MapMarkerAlt, "Флаг на карте"))
            Plugin.GameGui.OpenMapWithMapLink(new MapLinkPayload(p.DistrictId, mapRow.RowId, (int)(x * 1000f), (int)(z * 1000f)));
        ImGui.SameLine();
        ImGui.TextColored(Theme.TextDim, "Фото домов в данных игры нет — ниже карта района с отмеченным участком.");

        var path = $"ui/map/{mapRow.Id.ExtractText()}/{mapRow.Id.ExtractText().Replace("/", "")}_m.tex";
        var wrap = Plugin.Textures.GetFromGame(path).GetWrapOrEmpty();
        if (wrap.Width <= 1) return;
        var size = Math.Min(ImGui.GetContentRegionAvail().X, 640 * Theme.S);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.Image(wrap.Handle, new Vector2(size));
        // Пиксель на текстуре 2048×2048: (координата + смещение) × масштаб + 1024
        var scale = mapRow.SizeFactor / 100f;
        var px = ((x + mapRow.OffsetX) * scale + 1024f) / 2048f * size;
        var pz = ((z + mapRow.OffsetY) * scale + 1024f) / 2048f * size;
        var c = pos + new Vector2(px, pz);
        var dl = ImGui.GetWindowDrawList();
        var t = (float)(ImGui.GetTime() % 1.2) / 1.2f;
        dl.AddCircle(c, (8 + 14 * t) * Theme.S, Theme.U32(Theme.Alpha(Theme.Red, 1 - t)), 24, 2.5f * Theme.S);
        dl.AddCircleFilled(c, 6 * Theme.S, Theme.U32(Theme.Red));
        dl.AddCircle(c, 6 * Theme.S, Theme.U32(Theme.Text), 16, 1.5f * Theme.S);
    }

    /// <summary>Карта (основная часть района или Subdivision) и координаты участка.</summary>
    private static (Map Map, float X, float Z)? PlotMarker(uint territory, int plot)
    {
        var sheet = Plugin.Data.GetSubrowExcelSheet<HousingMapMarkerInfo>();
        if (!sheet.HasRow(territory)) return null;
        var rowSet = sheet.GetRow(territory);
        if (plot < 0 || plot >= rowSet.Count) return null;
        var m = rowSet[plot];
        if (m.Map.ValueNullable is not { } map) return null;
        return (map, m.X, m.Z);
    }
}
