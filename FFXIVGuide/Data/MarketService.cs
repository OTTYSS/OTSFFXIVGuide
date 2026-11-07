using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace FFXIVGuide.Data;

/// <summary>Самый дешёвый лот предмета в датацентре.</summary>
public sealed record MarketPrice(uint ItemId, long Price, string World, bool Hq, int Listings, DateTime FetchedAt);

/// <summary>
/// Цены с Universalis (публичный API, тот же источник, что у Price Insight).
/// Только чтение: ничего не отправляет в игру.
/// </summary>
public sealed class MarketService : IDisposable
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private readonly HttpClient http = Net.Create(TimeSpan.FromSeconds(15));
    private readonly ConcurrentDictionary<(string Region, uint Item), MarketPrice?> cache = new();
    private readonly ConcurrentDictionary<(string Region, uint Item), byte> inFlight = new();

    public MarketService()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FFXIVGuide/0.3");
    }

    /// <summary>Текущий мир персонажа (worldOnly) или его датацентр.</summary>
    public static string? CurrentRegion(bool worldOnly)
    {
        if (!PlayerInfo.Ready) return null;
        var world = Plugin.PlayerState.CurrentWorld.ValueNullable;
        var name = worldOnly
            ? world?.Name.ExtractText()
            : world?.DataCenter.ValueNullable?.Name.ExtractText();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>Есть ли цена в кеше. null — ещё грузится или лотов нет (см. IsLoading).</summary>
    public MarketPrice? Get(string region, uint itemId) =>
        cache.TryGetValue((region, itemId), out var p) ? p : null;

    public bool IsLoading(string region, uint itemId) => inFlight.ContainsKey((region, itemId));

    public bool IsKnown(string region, uint itemId) => cache.ContainsKey((region, itemId));

    /// <summary>Запросить цены пачкой (недостающие или устаревшие).</summary>
    public void Request(string region, IEnumerable<uint> itemIds)
    {
        var now = DateTime.UtcNow;
        var need = itemIds
            .Distinct()
            .Where(id => !inFlight.ContainsKey((region, id)))
            .Where(id => !cache.TryGetValue((region, id), out var p) || (p is not null && now - p.FetchedAt > Ttl))
            .ToList();
        if (need.Count == 0) return;

        foreach (var id in need) inFlight[(region, id)] = 0;

        foreach (var chunk in need.Chunk(50))
            _ = Task.Run(() => Fetch(region, chunk));
    }

    private async Task Fetch(string region, uint[] ids)
    {
        try
        {
            var url = $"https://universalis.app/api/v2/{Uri.EscapeDataString(region)}/{string.Join(",", ids)}?listings=1&entries=0";
            var json = await http.GetStringAsync(url).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in items.EnumerateObject())
                    if (uint.TryParse(prop.Name, out var id))
                        Store(region, id, prop.Value);
            }
            else if (ids.Length == 1)
            {
                Store(region, ids[0], root);
            }

            // Предметы, о которых Universalis ничего не вернул, считаем «лотов нет».
            foreach (var id in ids)
                cache.TryAdd((region, id), null);
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Universalis: не удалось получить цены");
        }
        finally
        {
            foreach (var id in ids) inFlight.TryRemove((region, id), out _);
        }
    }

    private void Store(string region, uint id, JsonElement el)
    {
        MarketPrice? price = null;
        if (el.TryGetProperty("listings", out var listings) && listings.ValueKind == JsonValueKind.Array && listings.GetArrayLength() > 0)
        {
            var first = listings[0];
            var count = el.TryGetProperty("listingsCount", out var c) && c.TryGetInt32(out var n) ? n : listings.GetArrayLength();
            price = new MarketPrice(
                id,
                first.TryGetProperty("pricePerUnit", out var ppu) ? ppu.GetInt64() : 0,
                first.TryGetProperty("worldName", out var w) ? w.GetString() ?? region : region,
                first.TryGetProperty("hq", out var hq) && hq.GetBoolean(),
                count,
                DateTime.UtcNow);
        }

        cache[(region, id)] = price;
    }

    // ───────────── История продаж ─────────────

    public sealed record PricePoint(DateTime Day, double Avg, int Sold);

    private readonly ConcurrentDictionary<(string, uint), List<PricePoint>?> history = new();
    private readonly ConcurrentDictionary<(string, uint), byte> historyLoading = new();

    /// <summary>Средняя цена продаж по дням за 4 недели (null — грузится или нет данных).</summary>
    public List<PricePoint>? History(string region, uint itemId)
    {
        if (history.TryGetValue((region, itemId), out var h)) return h;
        if (historyLoading.TryAdd((region, itemId), 0))
            _ = Task.Run(async () =>
            {
                try
                {
                    var url = $"https://universalis.app/api/v2/history/{Uri.EscapeDataString(region)}/{itemId}?entriesToReturn=1800&entriesWithin=2419200";
                    var json = await http.GetStringAsync(url).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var list = new List<(DateTime Day, long Price, int Qty)>();
                    if (doc.RootElement.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
                        foreach (var e in entries.EnumerateArray())
                        {
                            var ts = e.TryGetProperty("timestamp", out var t) ? t.GetInt64() : 0;
                            var ppu = e.TryGetProperty("pricePerUnit", out var p) ? p.GetInt64() : 0;
                            var qty = e.TryGetProperty("quantity", out var q) ? q.GetInt32() : 1;
                            if (ts > 0 && ppu > 0) list.Add((DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime.Date, ppu, qty));
                        }
                    history[(region, itemId)] = list.GroupBy(x => x.Day).OrderBy(g => g.Key)
                        .Select(g => new PricePoint(g.Key, g.Sum(x => (double)x.Price * x.Qty) / Math.Max(1, g.Sum(x => x.Qty)), g.Sum(x => x.Qty)))
                        .ToList();
                }
                catch (Exception e)
                {
                    Plugin.Log.Warning(e, "Universalis: не удалось получить историю");
                    history[(region, itemId)] = new List<PricePoint>();
                }
                finally { historyLoading.TryRemove((region, itemId), out _); }
            });
        return null;
    }

    public void Dispose() => http.Dispose();
}
