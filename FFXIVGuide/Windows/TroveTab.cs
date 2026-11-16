using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>
/// Moogle Treasure Trove: сколько «Irregular Tomestone» есть, что за них дают, что ещё не получено и сколько томов на это нужно.
/// </summary>
internal sealed class TroveTab
{
    private sealed record Reward(uint Item, string Name, uint Icon, int Cost, int Count, bool Unlockable);
    private sealed record Tome(uint Id, string Name, uint Icon, List<Reward> Rewards);

    private List<Tome>? tomes;
    private int pick = -1;
    private bool hideOwned = true;

    private static List<Tome> Build()
    {
        var items = Plugin.Data.GetExcelSheet<Item>();
        var en = Plugin.Data.GetExcelSheet<Item>(Dalamud.Game.ClientLanguage.English);
        var tomeIds = en.Where(i => i.Name.ExtractText().StartsWith("Irregular Tomestone", StringComparison.OrdinalIgnoreCase)).Select(i => i.RowId).ToHashSet();
        var rewards = tomeIds.ToDictionary(id => id, _ => new Dictionary<uint, Reward>());
        foreach (var shop in Plugin.Data.GetExcelSheet<SpecialShop>())
            foreach (var entry in shop.Item)
            {
                var cost = entry.ItemCosts.FirstOrDefault(c => tomeIds.Contains(c.ItemCost.RowId));
                if (cost.ItemCost.RowId == 0 || cost.CurrencyCost == 0) continue;
                foreach (var rec in entry.ReceiveItems)
                {
                    if (rec.Item.RowId == 0 || items.GetRowOrDefault(rec.Item.RowId) is not { } it || it.Name.IsEmpty) continue;
                    rewards[cost.ItemCost.RowId].TryAdd(it.RowId, new Reward(it.RowId, it.Name.ExtractText(), it.Icon, (int)cost.CurrencyCost, (int)Math.Max(1, rec.ReceiveCount), it.ItemAction.RowId != 0));
                }
            }
        return tomeIds.Where(id => rewards[id].Count > 0).OrderByDescending(id => id)
            .Select(id => new Tome(id, items.GetRow(id).Name.ExtractText(), items.GetRow(id).Icon, rewards[id].Values.OrderByDescending(r => r.Cost).ToList()))
            .ToList();
    }

    private static unsafe int Have(uint id)
    {
        var im = InventoryManager.Instance();
        return im == null ? 0 : im->GetInventoryItemCount(id);
    }

    private static bool Owned(Reward r)
    {
        if (!PlayerInfo.Ready) return false;
        var row = Plugin.Data.GetExcelSheet<Item>().GetRowOrDefault(r.Item);
        if (row is not { } it) return false;
        if (r.Unlockable && Plugin.Unlocks.IsItemUnlocked(it)) return true;
        return PlayerInfo.ItemCount(r.Item) > 0 || RetainerTracker.CountOf(r.Item).Total > 0;
    }

    public void Draw()
    {
        Theme.Header("Moogle Treasure Trove", "Событие с томами: сколько есть, что ещё не получено и сколько томов на это нужно");
        tomes ??= Build();
        if (tomes.Count == 0) { Theme.EmptyState(FontAwesomeIcon.Gift, "Нет данных", "В файлах игры не нашлось обмена Irregular Tomestone."); return; }

        // По умолчанию — тот том, который у тебя есть, иначе самый новый
        if (pick < 0)
        {
            pick = tomes.FindIndex(t => Have(t.Id) > 0);
            if (pick < 0) pick = 0;
        }
        var tome = tomes[Math.Clamp(pick, 0, tomes.Count - 1)];
        var have = Have(tome.Id);

        Theme.Panel("mttop", () =>
        {
            ImGui.SetNextItemWidth(340 * Theme.S);
            if (ImGui.BeginCombo("##mttome", tome.Name))
            {
                for (var i = 0; i < tomes.Count; i++)
                    if (ImGui.Selectable($"{tomes[i].Name}##mtt{i}", i == pick)) pick = i;
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            ImGui.Checkbox("Скрыть уже полученное", ref hideOwned);
            var missing = tome.Rewards.Where(r => !Owned(r)).ToList();
            var need = missing.Sum(r => r.Cost);
            Theme.LargeText(Theme.Gold, $"У тебя: {have} томов");
            ImGui.TextColored(Theme.Text, $"Не получено наград: {missing.Count} из {tome.Rewards.Count} · нужно томов на всё: {need}" + (need > have ? $" (не хватает {need - have})" : " ✓"));
            ImGui.TextColored(Theme.TextDim, "Томы дают за задания события в журнале (вкладка Moogle Treasure Trove) — обычно за старые данжи, триалы и рейды. Где обменивать — сказано в анонсе события в игре.");
        });

        Theme.Panel("mtlist", () =>
        {
            foreach (var r in tome.Rewards)
            {
                var owned = Owned(r);
                if (hideOwned && owned) continue;
                Theme.GameIcon(r.Icon, ImGui.GetCursorScreenPos(), 28 * Theme.S, 4f);
                ImGui.Dummy(new System.Numerics.Vector2(32 * Theme.S, 28 * Theme.S));
                ImGui.SameLine();
                if (ImGui.Selectable($"{r.Name}{(r.Count > 1 ? $" ×{r.Count}" : "")}##mtr{r.Item}")) WikiNav.Open(WikiKind.Item, r.Item, r.Name);
                ImGui.SameLine();
                ImGui.TextColored(have >= r.Cost ? Theme.Green : Theme.TextDim, $"{r.Cost} томов");
                if (owned) { ImGui.SameLine(); ImGui.TextColored(Theme.Green, "есть ✓"); }
            }
        });
    }
}
