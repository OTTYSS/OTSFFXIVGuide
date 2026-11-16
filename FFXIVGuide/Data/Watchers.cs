using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Game.DutyState;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

/// <summary>
/// Фоновые напоминания (только сообщения, ничего не нажимают): лотерея и новые участки под фильтр,
/// износ снаряжения и духовная связь, еда в данже.
/// </summary>
public static class Watchers
{
    public static void Tick()
    {
        if (!Plugin.ClientState.IsLoggedIn) return;
        try
        {
            HousingTick();
            GearTick();
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Напоминания: ошибка проверки");
        }
    }

    // ───────────── Жильё ─────────────

    private const string Api = "https://paissadb.zhu.codes";
    private static readonly System.Net.Http.HttpClient Http = Net.Create(TimeSpan.FromSeconds(30));
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static DateTime nextHousing = DateTime.MinValue;
    private static bool housingBusy;

    private sealed record Plot(uint WorldId, uint DistrictId, int WardNumber, int PlotNumber, int Size, long Price,
        int PurchaseSystem, int? LottoPhase, long? LottoPhaseUntil);
    private sealed record District(uint Id, string Name, List<Plot> OpenPlots);
    private sealed record WorldDetail(uint Id, string Name, List<District> Districts);

    private static readonly Dictionary<uint, string> DistrictNames = new()
    {
        [339] = "Mist", [340] = "Lavender Beds", [341] = "Goblet", [641] = "Shirogane", [979] = "Empyreum",
    };

    public static readonly uint[] DistrictIds = { 0, 339, 340, 341, 641, 979 };

    static Watchers() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("OTSFFXIVGuide (Dalamud plugin)");

    private static void HousingTick()
    {
        var cfg = Plugin.Config;
        if (!cfg.HousingWatch || housingBusy || DateTime.Now < nextHousing) return;
        var world = Plugin.PlayerState.CurrentWorld.RowId;
        if (world == 0) return;
        nextHousing = DateTime.Now.AddMinutes(15);
        housingBusy = true;
        Task.Run(async () =>
        {
            try
            {
                var w = await Http.GetFromJsonAsync<WorldDetail>($"{Api}/worlds/{world}", Json).ConfigureAwait(false);
                if (w is null) return;
                var plots = w.Districts.SelectMany(d => d.OpenPlots).ToList();
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                // Стадия лотереи — по большинству свежих данных
                var fresh = plots.Where(p => (p.PurchaseSystem & 1) != 0 && p.LottoPhase is not null && p.LottoPhaseUntil >= now)
                    .GroupBy(p => (p.LottoPhase, p.LottoPhaseUntil)).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
                if (fresh is { } ph && (ph.LottoPhase != cfg.HousingLastPhase || ph.LottoPhaseUntil != cfg.HousingLastPhaseUntil))
                {
                    var first = cfg.HousingLastPhase == 0;
                    cfg.HousingLastPhase = ph.LottoPhase ?? 0;
                    cfg.HousingLastPhaseUntil = ph.LottoPhaseUntil ?? 0;
                    var until = DateTimeOffset.FromUnixTimeSeconds(ph.LottoPhaseUntil ?? 0).LocalDateTime;
                    if (!first)
                        Notify.Send(Notify.Kind.Housing, "Лотерея жилья",
                            ph.LottoPhase == 1 ? $"Начался приём заявок на лотерею участков ({w.Name}) — до {until:dd.MM HH:mm}."
                                               : $"Приём заявок закрыт, идёт период итогов ({w.Name}) — до {until:dd.MM HH:mm}.");
                }

                // Новые участки под фильтр
                var match = plots.Where(p =>
                    (cfg.HousingWatchDistrict == 0 || p.DistrictId == cfg.HousingWatchDistrict)
                    && (cfg.HousingWatchSize < 0 || p.Size == cfg.HousingWatchSize)
                    && (cfg.HousingWatchBuyer == 0 || (p.PurchaseSystem & (cfg.HousingWatchBuyer == 1 ? 4 : 2)) != 0)).ToList();
                var keys = match.Select(p => $"{p.WorldId}:{p.DistrictId}:{p.WardNumber}:{p.PlotNumber}").ToHashSet();
                var fresh0 = cfg.HousingSeenPlots.Count == 0;
                var added = match.Where(p => !cfg.HousingSeenPlots.Contains($"{p.WorldId}:{p.DistrictId}:{p.WardNumber}:{p.PlotNumber}")).ToList();
                cfg.HousingSeenPlots = keys;
                if (!fresh0 && added.Count > 0)
                {
                    var lines = added.Take(3).Select(p => $"{DistrictNames.GetValueOrDefault(p.DistrictId, "?")} w{p.WardNumber + 1} #{p.PlotNumber + 1} ({"SML"[Math.Clamp(p.Size, 0, 2)]}, {p.Price:N0} гил)");
                    Notify.Send(Notify.Kind.Housing, "Свободный участок",
                        $"Новые свободные участки на {w.Name}: " + string.Join("; ", lines) + (added.Count > 3 ? $" и ещё {added.Count - 3}" : "") + ". Подробнее: /fguide участки");
                }
                cfg.Save();
            }
            catch (Exception e)
            {
                Plugin.Log.Debug(e, "Напоминания о жилье: не удалось получить данные");
            }
            finally { housingBusy = false; }
        });
    }

    // ───────────── Снаряжение ─────────────

    private static DateTime nextGear = DateTime.MinValue;
    private static bool repairWarned;
    private static readonly HashSet<uint> spiritWarned = new();

    public sealed record GearState(uint ItemId, string Name, uint Icon, float Condition, float Spiritbond, bool Equipped);

    /// <summary>Надетое снаряжение и вещи из оружейного шкафа с полной духовной связью.</summary>
    public static unsafe List<GearState> ReadGear(bool withArmoury)
    {
        var res = new List<GearState>();
        var im = InventoryManager.Instance();
        if (im == null) return res;
        var items = Plugin.Data.GetExcelSheet<Item>();
        void Scan(InventoryType t, bool equipped)
        {
            var c = im->GetInventoryContainer(t);
            if (c == null) return;
            for (var i = 0; i < c->Size; i++)
            {
                if (equipped && i == 13) continue; // кристалл души не изнашивается
                var s = c->GetInventorySlot(i);
                if (s == null || s->ItemId == 0) continue;
                var id = s->GetBaseItemId();
                if (items.GetRowOrDefault(id) is not { } it) continue;
                var cond = s->Condition / 300f;
                var sb = s->SpiritbondOrCollectability / 100f;
                if (!equipped && sb < 100f) continue;
                res.Add(new GearState(id, it.Name.ExtractText(), it.Icon, cond, sb, equipped));
            }
        }
        Scan(InventoryType.EquippedItems, true);
        if (withArmoury)
            foreach (var t in new[] { InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead, InventoryType.ArmoryBody,
                         InventoryType.ArmoryHands, InventoryType.ArmoryLegs, InventoryType.ArmoryFeets, InventoryType.ArmoryEar,
                         InventoryType.ArmoryNeck, InventoryType.ArmoryWrist, InventoryType.ArmoryRings })
                Scan(t, false);
        return res;
    }

    private static void GearTick()
    {
        var cfg = Plugin.Config;
        if (DateTime.Now < nextGear || !Plugin.PlayerState.IsLoaded) return;
        nextGear = DateTime.Now.AddSeconds(30);
        var gear = ReadGear(false);
        if (gear.Count == 0) return;

        if (cfg.RepairWarnPercent > 0)
        {
            var worst = gear.OrderBy(g => g.Condition).First();
            if (worst.Condition < cfg.RepairWarnPercent && !repairWarned)
            {
                repairWarned = true;
                Notify.Send(Notify.Kind.Gear, "Пора чиниться",
                    $"Прочность снаряжения упала до {worst.Condition:0}% ({worst.Name}). Почини у NPC-ремонтника или Dark Matter.");
            }
            else if (worst.Condition >= cfg.RepairWarnPercent + 5) repairWarned = false;
        }

        if (cfg.SpiritbondWarn)
            foreach (var g in gear.Where(g => g.Spiritbond >= 100f))
                if (spiritWarned.Add(g.ItemId))
                    Notify.Send(Notify.Kind.Gear, "Можно извлечь материю", $"{g.Name}: духовная связь 100% — можно извлечь материю (Materia Extraction).");
        spiritWarned.RemoveWhere(id => gear.All(g => g.ItemId != id || g.Spiritbond < 100f));
    }

    // ───────────── Еда в данже ─────────────

    private const uint WellFed = 48;

    public static void OnDutyStarted(IDutyStateEventArgs _) => CheckFood(true);

    /// <summary>Сколько еды (Meal) в сумках.</summary>
    public static unsafe List<(uint Id, string Name, int Count)> FoodInBags()
    {
        var res = new Dictionary<uint, (string, int)>();
        var im = InventoryManager.Instance();
        if (im == null) return new();
        var items = Plugin.Data.GetExcelSheet<Item>();
        foreach (var t in new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 })
        {
            var c = im->GetInventoryContainer(t);
            if (c == null) continue;
            for (var i = 0; i < c->Size; i++)
            {
                var s = c->GetInventorySlot(i);
                if (s == null || s->ItemId == 0) continue;
                var id = s->GetBaseItemId();
                if (items.GetRowOrDefault(id) is not { } it || it.ItemUICategory.RowId != 46) continue;
                res[id] = (it.Name.ExtractText(), (res.TryGetValue(id, out var cur) ? cur.Item2 : 0) + (int)s->Quantity);
            }
        }
        return res.Select(kv => (kv.Key, kv.Value.Item1, kv.Value.Item2)).OrderByDescending(x => x.Item3).ToList();
    }

    /// <summary>Сколько секунд осталось эффекта еды (0 — нет еды).</summary>
    public static float FoodSecondsLeft()
    {
        if (Plugin.Objects.LocalPlayer is not { } me) return 0;
        foreach (var s in me.StatusList)
            if (s.StatusId == WellFed) return s.RemainingTime;
        return 0;
    }

    public static void CheckFood(bool dutyStart)
    {
        var cfg = Plugin.Config;
        if (!cfg.FoodReminder || !dutyStart) return;
        var left = FoodSecondsLeft();
        var food = FoodInBags();
        var hint = food.Count > 0 ? $" В сумке: {food[0].Name} ×{food[0].Count}." : " Еды в сумке нет.";
        if (left <= 0)
            Notify.Send(Notify.Kind.Food, "Нет еды", "На тебе нет эффекта еды (Well Fed)." + hint);
        else if (left < cfg.FoodWarnMinutes * 60)
            Notify.Send(Notify.Kind.Food, "Еда скоро закончится", $"Эффект еды закончится через {Math.Ceiling(left / 60)} мин." + hint);
    }
}
