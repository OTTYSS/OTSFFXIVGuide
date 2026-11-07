using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Сезонные события, Custom Deliveries и хаузинг (таймер сноса).</summary>
internal sealed unsafe class WeeklyTab
{
    private static readonly string[] Sections = { "Сезонные события", "Custom Deliveries", "Хаузинг" };
    private int section;
    private readonly GuideData data;

    public WeeklyTab(GuideData data) => this.data = data;

    public void Draw()
    {
        Theme.Header("События, поставки, дом", "Что идёт сейчас, кому сдавать поставки на этой неделе и когда заходить в дом");
        Theme.Chips("wksec", Sections, ref section);
        if (!ImGui.BeginChild("##weekly", Vector2.Zero)) { ImGui.EndChild(); return; }
        switch (section)
        {
            case 0: DrawEvents(); break;
            case 1: DrawDeliveries(); break;
            default: DrawHousing(); break;
        }
        ImGui.EndChild();
    }

    // ───────────── Сезонные события ─────────────

    private void DrawEvents()
    {
        var gm = GameMain.Instance();
        var active = new List<uint>();
        if (gm != null)
            foreach (var f in gm->ActiveFestivals)
                if (f.Id != 0) active.Add(f.Id);

        if (active.Count == 0)
        {
            Theme.EmptyState(FontAwesomeIcon.Gift, "Сейчас событий нет", "Когда в игре идёт сезонное событие, здесь появятся его квесты, где их взять и награды.");
            return;
        }

        var quests = Plugin.Data.GetExcelSheet<Quest>().Where(q => q.Festival.RowId != 0 && active.Contains(q.Festival.RowId) && q.Name.ExtractText().Length > 0).ToList();
        Theme.Panel("evtop", () =>
        {
            Theme.FaIcon(FontAwesomeIcon.Gift, Theme.Gold);
            ImGui.SameLine();
            ImGui.TextColored(Theme.Text, $"Идёт событий: {active.Count}. Квестов события: {quests.Count}.");
            ImGui.TextColored(Theme.Gray, "Даты окончания игра заранее не сообщает — смотри новости Lodestone.");
        });
        foreach (var q in quests)
            Theme.Panel($"ev{q.RowId}", () =>
            {
                var done = PlayerInfo.IsQuestDone(q.RowId);
                if (data.Quests.TryGetValue(q.RowId, out var qe))
                {
                    var (b, c) = QuestsTab.StateBadge(qe);
                    if (Theme.Card($"##evq{q.RowId}", qe.Icon, qe.Name, $"Ур. {qe.Level} · {qe.Place}", b, c, false, 42 * Theme.S))
                        WikiNav.Open(WikiKind.Quest, q.RowId, qe.Name);
                    if (!done && ImGui.SmallButton($"Флаг на карте##evf{q.RowId}")) PlayerInfo.FlagQuestGiver(qe);
                }
                else ImGui.TextColored(done ? Theme.Green : Theme.Text, q.Name.ExtractText() + (done ? " ✓" : ""));
                if (OfflineDb.Instance.Index?.QuestRewards.GetValueOrDefault(q.RowId) is { Count: > 0 } rw)
                {
                    ImGui.TextColored(Theme.TextDim, "Награды:");
                    WikiView.ItemGrid(rw.Select(r => r.Item).Where(i => i > 1), $"evr{q.RowId}");
                }
            });
    }

    // ───────────── Custom Deliveries ─────────────

    private void DrawDeliveries()
    {
        var sm = SatisfactionSupplyManager.Instance();
        if (sm != null)
            Theme.Panel("cdtop", () =>
            {
                var left = sm->GetRemainingAllowances();
                Theme.FaIcon(FontAwesomeIcon.Truck, Theme.Gold);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Text, $"Поставок осталось на этой неделе: {left} из 12 (общие на всех заказчиков, сброс во вторник).");
            });

        var npcs = Plugin.Data.GetExcelSheet<SatisfactionNpc>().Where(n => n.Npc.RowId != 0).ToList();
        var supplies = Plugin.Data.GetSubrowExcelSheet<SatisfactionSupply>();
        Theme.Grid("cdgrid", npcs.Count, 380f, i =>
        {
            var n = npcs[i];
            Theme.Panel($"cd{n.RowId}", () =>
            {
                var name = GuideData.Capitalize(n.Npc.ValueNullable?.Singular.ExtractText() ?? "");
                ImGui.TextColored(Theme.Gold, NameTranslations.Show(name));
                var rank = sm != null && n.RowId - 1 < sm->SatisfactionRanks.Length ? sm->SatisfactionRanks[(int)n.RowId - 1] : (byte)0;
                ImGui.SameLine();
                ImGui.TextColored(Theme.TextDim, rank == 0 ? "не открыт" : $"ранг {rank} · {n.DeliveriesPerWeek} поставок в неделю");
                if (n.QuestRequired.RowId != 0 && !PlayerInfo.IsQuestDone(n.QuestRequired.RowId))
                    ImGui.TextColored(Theme.Gray, $"Открывается квестом «{n.QuestRequired.ValueNullable?.Name.ExtractText()}» (ур. {n.LevelUnlock})");
                if (n.Level.RowId != 0 && ImGui.SmallButton($"Флаг##cdf{n.RowId}")) ItemSources.FlagLevel(n.Level.RowId);

                // Что принимает на текущем ранге
                var r = Math.Max(0, Math.Min(rank == 0 ? 0 : rank - 1, n.SatisfactionNpcParams.Count - 1));
                if (n.SatisfactionNpcParams.Count == 0) return;
                var idx = n.SatisfactionNpcParams[r].SupplyIndex;
                if (idx <= 0) return;
                var rows = supplies.GetRowOrDefault((uint)idx);
                if (rows is null) return;
                var items = rows.Value.Where(s => s.Item.RowId != 0).ToList();
                if (items.Count == 0) return;
                ImGui.TextColored(Theme.TextDim, "Принимает (коллекционные; порог низкий / средний / высокий):");
                foreach (var s in items)
                    WikiView.ItemLine(s.Item.RowId, 0, $"{s.CollectabilityLow} / {s.CollectabilityMid} / {s.CollectabilityHigh}" + (s.IsBonus ? " · бонус" : ""), $"cds{n.RowId}");
            });
        });
    }

    // ───────────── Хаузинг ─────────────

    private static DateTime nextCheck;

    /// <summary>Запоминает, когда ты заходил в свой дом (для таймера сноса через 45 дней).</summary>
    public static void Tick()
    {
        if (DateTime.Now < nextCheck || !PlayerInfo.Ready) return;
        nextCheck = DateTime.Now.AddSeconds(15);
        Reminders();
        try
        {
            var hm = HousingManager.Instance();
            if (hm == null || !hm->IsInside()) return;
            var current = hm->GetCurrentIndoorHouseId();
            var personal = HousingManager.GetOwnedHouseId(EstateType.PersonalEstate);
            var fc = HousingManager.GetOwnedHouseId(EstateType.FreeCompanyEstate);
            var cfg = Plugin.Config;
            var changed = false;
            if (personal.Id != 0 && current.Id == personal.Id) { cfg.HouseVisitPersonal = DateTime.Now; changed = true; }
            if (fc.Id != 0 && current.Id == fc.Id) { cfg.HouseVisitFc = DateTime.Now; changed = true; }
            if (changed) cfg.Save();
        }
        catch { /* не в доме */ }
    }

    /// <summary>Напоминания: скоро снос дома (раз в день), началось новое событие.</summary>
    private static void Reminders()
    {
        var cfg = Plugin.Config;
        foreach (var (label, visit) in new[] { ("личный дом", cfg.HouseVisitPersonal), ("дом компании", cfg.HouseVisitFc) })
        {
            if (visit is null) continue;
            var left = visit.Value.AddDays(45) - DateTime.Now;
            if (left.TotalDays <= 7 && (cfg.HouseWarned is null || cfg.HouseWarned.Value.Date != DateTime.Today))
            {
                cfg.HouseWarned = DateTime.Now;
                cfg.Save();
                Notify.Send(Notify.Kind.House, "Хаузинг", $"Зайди в {label}: до сноса {(int)Math.Max(0, left.TotalDays)} дн.");
            }
        }
        var gm = GameMain.Instance();
        if (gm == null) return;
        foreach (var f in gm->ActiveFestivals)
        {
            if (f.Id == 0 || cfg.SeenFestivals.Contains(f.Id)) continue;
            cfg.SeenFestivals.Add(f.Id);
            cfg.Save();
            Notify.Send(Notify.Kind.Event, "События", "Началось сезонное событие — квесты и награды в «Активности» → «События и поставки».");
        }
    }

    private static void DrawHousing()
    {
        Theme.Panel("hs", () =>
        {
            Theme.SubHeader("Таймер сноса");
            ImGui.TextColored(Theme.TextDim, "Если 45 дней не заходить в свой дом, его снесут. Плагин запоминает каждый визит.");
            void Row(string label, DateTime? visit)
            {
                Theme.FaIcon(FontAwesomeIcon.Home, Theme.GoldDim);
                ImGui.SameLine();
                if (visit is null) { ImGui.TextColored(Theme.Gray, $"{label}: ещё не заходил с плагином"); return; }
                var left = visit.Value.AddDays(45) - DateTime.Now;
                ImGui.TextColored(left.TotalDays < 7 ? Theme.Red : left.TotalDays < 20 ? Theme.Yellow : Theme.Green,
                    $"{label}: был {visit:dd.MM.yyyy}, до сноса {(int)Math.Max(0, left.TotalDays)} дн.");
            }
            Row("Личный дом", Plugin.Config.HouseVisitPersonal);
            Row("Дом компании", Plugin.Config.HouseVisitFc);
            ImGui.Dummy(new Vector2(0, 4 * Theme.S));
            Theme.SubHeader("Лотерея участков");
            ImGui.TextColored(Theme.TextDim, "Фаза лотереи (приём заявок или результаты) и время её окончания написаны на табличке участка — подойди к табличке.");
            ImGui.TextColored(Theme.Gray, "Обычный цикл: 5 дней приём заявок, затем 4 дня объявление результатов.");
        });
    }
}
