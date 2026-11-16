using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Ранги Grand Company: что нужно для следующего ранга, печати и их лимит, что из сумок выгодно сдать в Expert Delivery.</summary>
internal sealed class GcRankTab
{
    private static readonly string[] GcNames = { "", "Maelstrom (Limsa Lominsa)", "Order of the Twin Adder (Gridania)", "Immortal Flames (Ul'dah)" };
    private DateTime nextScan = DateTime.MinValue;
    private List<(InvItem Item, uint Seals)> delivery = new();

    private static string RankName(int gc, uint rank)
    {
        if (rank == 0) return "—";
        return gc switch
        {
            1 => Plugin.Data.GetExcelSheet<GCRankLimsaMaleText>().GetRowOrDefault(rank)?.Singular.ExtractText(),
            2 => Plugin.Data.GetExcelSheet<GCRankGridaniaMaleText>().GetRowOrDefault(rank)?.Singular.ExtractText(),
            _ => Plugin.Data.GetExcelSheet<GCRankUldahMaleText>().GetRowOrDefault(rank)?.Singular.ExtractText(),
        } is { Length: > 0 } s ? GuideData.Capitalize(s) : $"Ранг {rank}";
    }

    public unsafe void Draw()
    {
        Theme.Header("Ранги Grand Company", "Что нужно для следующего ранга, сколько печатей и что из сумок выгодно сдать за печати");
        if (!PlayerInfo.Ready) { Theme.NeedLogin("Зайди в игру персонажем: ранг и печати берутся из игры."); return; }
        var ps = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
        var im = InventoryManager.Instance();
        if (ps == null || im == null) return;
        int gc = ps->GrandCompany;
        if (gc is < 1 or > 3)
        {
            Theme.EmptyState(FontAwesomeIcon.Flag, "Ты не в Grand Company", "Вступить можно по сюжету (после квеста «The Company You Keep») в любом из трёх городов.");
            return;
        }
        var rank = (uint)ps->GetGrandCompanyRank();
        var seals = (int)im->GetCompanySeals((byte)gc);
        var maxSeals = (int)im->GetMaxCompanySeals((byte)gc);
        var ranks = Plugin.Data.GetExcelSheet<GrandCompanyRank>();
        var next = ranks.GetRowOrDefault(rank + 1);

        Theme.Panel("gcnow", () =>
        {
            Theme.LargeText(Theme.Gold, $"{GcNames[gc]} · {RankName(gc, rank)}");
            Theme.Progress($"Печати: {seals:N0} / {maxSeals:N0}", seals, Math.Max(1, maxSeals), seals >= maxSeals ? Theme.Red : Theme.Gold);
            if (seals >= maxSeals) ImGui.TextColored(Theme.Red, "Лимит печатей! Потрать их (Company Seals → обмен) или повысь ранг — новые не начисляются.");
        });

        Theme.Panel("gcnext", () =>
        {
            Theme.SubHeader("Следующий ранг");
            if (next is not { } n || n.RowId == 0) { ImGui.TextColored(Theme.Green, "Максимальный ранг ✓"); return; }
            ImGui.TextColored(Theme.Text, RankName(gc, n.RowId));
            if (n.RequiredSeals > 0)
            {
                var ok = seals >= n.RequiredSeals;
                ImGui.TextColored(ok ? Theme.Green : Theme.Yellow, $"{(ok ? "✓" : "•")} Печатей: {n.RequiredSeals:N0}" + (ok ? "" : $" (не хватает {n.RequiredSeals - seals:N0})"));
            }
            var quest = gc switch { 1 => n.QuestMaelstrom.RowId, 2 => n.QuestSerpents.RowId, _ => n.QuestFlames.RowId };
            if (quest != 0)
            {
                var q = Plugin.Data.GetExcelSheet<Quest>().GetRowOrDefault(quest);
                var done = PlayerInfo.IsQuestDone(quest);
                ImGui.TextColored(done ? Theme.Green : Theme.Yellow, $"{(done ? "✓" : "•")} Квест повышения: {q?.Name.ExtractText()}");
                if (!done && q?.IssuerLocation.RowId is { } lvl and not 0)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Флаг##gcq")) ItemSources.FlagLevel(lvl);
                }
            }
            if (n.RequiredHuntingLogRank > 0)
                ImGui.TextColored(Theme.Yellow, $"• Hunting Log Grand Company: ранг {n.RequiredHuntingLogRank} (вкладка «Hunting Log»)");
            if (n.RequiredSeals == 0 && quest == 0)
                ImGui.TextColored(Theme.TextDim, "Этот ранг дают за особые миссии сквадрона (Flagged Missions) — см. вкладку «Сквадроны».");
            ImGui.TextColored(Theme.Gray, "Повышение — у офицера по персоналу в штабе своей Grand Company.");
        });

        // Что сдать за печати (Expert Delivery)
        if (DateTime.Now >= nextScan)
        {
            nextScan = DateTime.Now.AddSeconds(3);
            var scan = InventoryAnalyzer.Scan();
            var rew = Plugin.Data.GetExcelSheet<GCSupplyDutyReward>();
            delivery = scan is null ? new() : scan.Items
                .Where(i => i.IsGear && i.Row.Rarity is 2 or 3 && !i.Row.IsIndisposable && i.Row.PriceLow > 0 && !scan.GearsetItems.Contains(i.ItemId))
                .Select(i => (i, rew.GetRowOrDefault(i.Row.LevelItem.RowId)?.SealsExpertDelivery ?? 0))
                .Where(x => x.Item2 > 0).OrderByDescending(x => x.Item2).ToList();
        }
        Theme.Panel("gcexp", () =>
        {
            var total = delivery.Sum(d => (long)d.Seals);
            Theme.SubHeader($"Expert Delivery: {delivery.Count} вещей ≈ {total:N0} печатей");
            ImGui.TextColored(Theme.TextDim, "Зелёное и синее снаряжение не из комплектов (gearset). Сдаётся у Personnel Officer → Expert Delivery. Проверь, что вещь точно не нужна.");
            foreach (var (it, s) in delivery.Take(40))
            {
                Theme.GameIcon(it.Icon, ImGui.GetCursorScreenPos(), 22 * Theme.S, 4f);
                ImGui.Dummy(new System.Numerics.Vector2(26 * Theme.S, 22 * Theme.S));
                ImGui.SameLine();
                ImGui.TextUnformatted(it.Name);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Gold, $"{s:N0}");
                ImGui.SameLine();
                ImGui.TextColored(Theme.Gray, it.Location);
            }
            if (delivery.Count > 40) ImGui.TextColored(Theme.Gray, $"…и ещё {delivery.Count - 40}");
            if (total > 0 && seals + total > maxSeals) ImGui.TextColored(Theme.Yellow, $"Всё сразу не влезет: до лимита осталось {Math.Max(0, maxSeals - seals):N0} печатей.");
        });
    }
}
