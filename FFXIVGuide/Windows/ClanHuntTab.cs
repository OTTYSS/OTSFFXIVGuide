using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Клановая охота: какие марки в твоих листах, сколько осталось убить, где они и сколько печатей дадут.</summary>
internal sealed class ClanHuntTab
{
    private readonly Action<string> openBestiary;
    private bool hideDone = true;

    public ClanHuntTab(Action<string> openBestiary) => this.openBestiary = openBestiary;

    private sealed record Target(string Name, string Zone, string? Fate, int Kills, int Needed, uint Icon);
    private sealed record Bill(int Type, string Name, string Currency, int Reward, List<Target> Targets);

    private static string CurrencyFor(uint expansion) => expansion switch
    {
        0 => "Allied Seals",
        1 or 2 => "Centurio Seals",
        _ => "Sacks of Nuts",
    };

    private static unsafe List<Bill> Read()
    {
        var res = new List<Bill>();
        var mh = MobHunt.Instance();
        if (mh == null) return res;
        var types = Plugin.Data.GetExcelSheet<MobHuntOrderType>();
        var orders = Plugin.Data.GetSubrowExcelSheet<MobHuntOrder>();
        foreach (var t in types)
        {
            var i = (int)t.RowId;
            if (i >= mh->ObtainedMarkId.Length || mh->ObtainedMarkId[i] == 0) continue;
            var rowId = (uint)mh->GetObtainedHuntOrderRowId((byte)i);
            if (!orders.HasRow(rowId)) continue;
            var targets = new List<Target>();
            var reward = 0;
            uint ex = 0;
            var sub = 0;
            foreach (var o in orders.GetRow(rowId))
            {
                var tg = o.Target.ValueNullable;
                if (tg is not { } target) { sub++; continue; }
                var kills = mh->GetKillCount((byte)i, (byte)sub);
                targets.Add(new Target(GuideData.Capitalize(target.Name.ValueNullable?.Singular.ExtractText() ?? "?"),
                    target.PlaceName.ValueNullable?.Name.ExtractText() ?? "",
                    target.FATE.RowId != 0 ? target.FATE.ValueNullable?.Name.ExtractText() : null,
                    kills, o.NeededKills, (uint)target.Icon));
                if (o.MobHuntReward.ValueNullable is { } rw) { reward = rw.CurrencyReward; ex = rw.Expansion.RowId; }
                sub++;
            }
            res.Add(new Bill(i, t.EventItem.ValueNullable?.Name.ExtractText() ?? $"Лист #{i}", CurrencyFor(ex), reward, targets));
        }
        return res;
    }

    public void Draw()
    {
        Theme.Header("Клановая охота", "Марки из твоих листов (Mark Bills): кого и сколько осталось убить, где они водятся и сколько печатей дадут");
        if (!PlayerInfo.Ready) { Theme.NeedLogin("Зайди в игру персонажем: листы охоты берутся из игры."); return; }
        var bills = Read();
        if (bills.Count == 0)
        {
            Theme.EmptyState(FontAwesomeIcon.Crosshairs, "Нет взятых листов", "Возьми листы охоты на доске Clan Hunt (Mark Bills) в городе — здесь появится, кого убить. Листы обновляются каждый день, элитные — раз в неделю.");
            return;
        }
        ImGui.Checkbox("Скрыть уже выполненные цели", ref hideDone);
        var totalLeft = bills.Sum(b => b.Targets.Sum(t => Math.Max(0, t.Needed - t.Kills)));
        var ready = bills.Count(b => b.Targets.All(t => t.Kills >= t.Needed));
        Theme.Panel("chsum", () =>
        {
            ImGui.TextColored(Theme.Text, $"Листов: {bills.Count} · осталось убить: {totalLeft} · можно сдать: {ready}");
            foreach (var g in bills.Where(b => b.Targets.All(t => t.Kills >= t.Needed)).GroupBy(b => b.Currency))
                ImGui.TextColored(Theme.Green, $"За готовые листы: {g.Sum(b => b.Reward)} {g.Key}");
        });

        foreach (var b in bills)
        {
            var done = b.Targets.All(t => t.Kills >= t.Needed);
            Theme.Panel($"chb{b.Type}", () =>
            {
                Theme.FaIcon(done ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.Scroll, done ? Theme.Green : Theme.Gold);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Text, b.Name);
                ImGui.SameLine();
                ImGui.TextColored(Theme.TextDim, $"· награда: {b.Reward} {b.Currency}");
                if (done) { ImGui.SameLine(); ImGui.TextColored(Theme.Green, "готово — сдай лист"); }
                var n = 0;
                foreach (var t in b.Targets)
                {
                    n++;
                    if (hideDone && t.Kills >= t.Needed) continue;
                    var ok = t.Kills >= t.Needed;
                    ImGui.Dummy(new System.Numerics.Vector2(8 * Theme.S, 0));
                    ImGui.SameLine();
                    Theme.InlineIcon(t.Icon, 0, null, 1.5f);
                    ImGui.TextColored(ok ? Theme.Green : Theme.Text, t.Name);
                    ImGui.SameLine();
                    Theme.MiniBar(t.Kills, t.Needed);
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.TextDim, t.Zone + (t.Fate is not null ? $" · FATE «{t.Fate}»" : ""));
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Где искать##ch{b.Type}_{n}")) openBestiary(t.Name);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Открыть в бестиарии: места появления и флаг на карте");
                }
            });
        }
    }
}
