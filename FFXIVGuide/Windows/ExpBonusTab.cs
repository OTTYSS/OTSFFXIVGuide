using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Бонусы опыта: что сейчас добавляет опыт текущей профессии и чего не хватает.</summary>
internal sealed class ExpBonusTab
{
    private static HashSet<uint>? expStatuses;

    /// <summary>Статусы, в описании которых говорится об опыте (Well Fed, бафы FC, «Road to …» и т.п.).</summary>
    private static HashSet<uint> ExpStatuses()
    {
        if (expStatuses is not null) return expStatuses;
        expStatuses = Plugin.Data.GetExcelSheet<Status>(Dalamud.Game.ClientLanguage.English)
            .Where(s => s.Description.ExtractText().Contains("EXP", StringComparison.Ordinal) && !s.Name.IsEmpty)
            .Select(s => s.RowId).ToHashSet();
        return expStatuses;
    }

    private static void Row(bool ok, string title, string detail, bool neutral = false)
    {
        Theme.FaIcon(ok ? FontAwesomeIcon.CheckCircle : neutral ? FontAwesomeIcon.InfoCircle : FontAwesomeIcon.TimesCircle, ok ? Theme.Green : neutral ? Theme.Blue : Theme.Gray);
        ImGui.SameLine();
        ImGui.TextColored(ok ? Theme.Text : Theme.TextDim, title);
        if (detail.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.Gray, "— " + detail);
        }
    }

    public unsafe void Draw()
    {
        Theme.Header("Бонусы опыта", "Что сейчас добавляет опыт текущей профессии, а что можно включить перед прокачкой");
        if (!PlayerInfo.Ready) { Theme.NeedLogin("Зайди в игру персонажем: бонусы и статусы берутся из игры."); return; }

        var job = Plugin.PlayerState.ClassJob.ValueNullable;
        var level = Plugin.PlayerState.Level;
        var cat = job?.ClassJobCategory.RowId ?? 0;
        var isCombat = job is { } j && j.RowId is not (>= 8 and <= 18);
        var isCraft = job is { } j2 && j2.RowId is >= 8 and <= 15;
        var jobs = Plugin.Data.GetExcelSheet<ClassJob>();
        _ = cat;

        Theme.Panel("ebjob", () =>
        {
            Theme.LargeText(Theme.Gold, $"{GuideData.Capitalize(job?.Name.ExtractText() ?? "")} · ур. {level}");
            var need = Plugin.Data.GetExcelSheet<ParamGrow>().GetRowOrDefault((uint)level)?.ExpToNext ?? 0;
            var exp = job is { } jj ? Plugin.PlayerState.GetClassJobExperience(jj) : 0;
            if (need > 0) Theme.Progress($"Опыт: {exp:N0} / {need:N0}", exp, need, Theme.Gold);
        });

        var me = Plugin.Objects.LocalPlayer;
        var statuses = Plugin.Data.GetExcelSheet<Status>();
        var active = new List<(string Name, float Left, uint Id)>();
        if (me is not null)
            foreach (var s in me.StatusList)
                if (s.StatusId != 0 && ExpStatuses().Contains(s.StatusId) && statuses.GetRowOrDefault(s.StatusId) is { } st)
                    active.Add((st.Name.ExtractText(), s.RemainingTime, s.StatusId));
        string Left(float sec) => sec <= 0 ? "" : sec >= 3600 ? $"ещё {(int)(sec / 3600)} ч {(int)(sec % 3600 / 60)} мин" : $"ещё {Math.Ceiling(sec / 60)} мин";

        Theme.Panel("ebnow", () =>
        {
            Theme.SubHeader("Сейчас действует");
            // Отдых
            var rested = Plugin.PlayerState.BaseRestedExperience;
            var need = Plugin.Data.GetExcelSheet<ParamGrow>().GetRowOrDefault((uint)level)?.ExpToNext ?? 0;
            Row(rested > 0, "Отдых (Rested EXP)", rested > 0 ? $"{rested:N0} опыта в запасе" + (need > 0 ? $" (≈{rested * 100.0 / need:0}% уровня)" : "") + " — +50% к опыту за убийства, пока не кончится"
                : "копится, когда персонаж в городе, гостинице или вышел из игры там", rested == 0);

            // Еда
            var food = active.FirstOrDefault(a => a.Id == 48);
            Row(food.Id != 0, "Еда (Well Fed)", food.Id != 0 ? $"+3% опыта, {Left(food.Left)}" : "любая еда даёт +3% опыта");

            // Бафы FC
            var fcName = isCraft ? "Helping Hand" : job?.RowId is >= 16 and <= 18 ? "Earth and Water" : "Heat of Battle";
            var fc = active.FirstOrDefault(a => a.Name.Contains(fcName, StringComparison.OrdinalIgnoreCase));
            Row(fc.Id != 0, $"Баф FC «{fcName}»", fc.Id != 0 ? Left(fc.Left) : "покупается за кредиты FC (вкладка «Действия FC»)");

            // Бонус оружейного шкафа (только боевые профессии)
            if (isCombat)
            {
                var maxCombat = jobs.Where(c => c.RowId is not 0 and not (>= 8 and <= 18) && !c.Name.IsEmpty)
                    .Max(c => (int)Plugin.PlayerState.GetClassJobLevel(c));
                var armoury = level < maxCombat;
                Row(armoury, "Бонус оружейного шкафа (Armoury Bonus)", armoury ? $"профессия ниже твоей самой прокачанной (ур. {maxCombat}) — опыта больше"
                    : "действует на профессии, которые ниже твоей самой прокачанной боевой", !armoury);
            }

            // Прочие статусы с опытом
            foreach (var a in active.Where(a => a.Id != 48 && !a.Name.Contains(fcName, StringComparison.OrdinalIgnoreCase)))
                Row(true, a.Name, Left(a.Left));

            // Снаряжение с бонусом опыта
            var items = Plugin.Data.GetExcelSheet<Item>(Dalamud.Game.ClientLanguage.English);
            var loc = Plugin.Data.GetExcelSheet<Item>();
            var expGear = new List<string>();
            for (var i = 0; i < 14; i++)
            {
                var id = PlayerInfo.EquippedItem(i);
                if (id != 0 && items.GetRowOrDefault(id) is { } it && it.Description.ExtractText().Contains("EXP", StringComparison.Ordinal))
                    expGear.Add(loc.GetRowOrDefault(id)?.Name.ExtractText() ?? "");
            }
            Row(expGear.Count > 0, "Снаряжение с бонусом опыта", expGear.Count > 0 ? string.Join(", ", expGear) : "кольца и серьги на опыт (если есть) — надень перед прокачкой", expGear.Count == 0);
        });

        if (isCombat)
            Theme.Panel("ebroul", () =>
            {
                Theme.SubHeader("Ежедневные рулетки (большой бонус за первый проход)");
                var roul = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.ContentRoulette>();
                var ui = UIState.Instance();
                foreach (var id in new byte[] { 1, 2, 3, 5, 6, 8, 9, 15, 17 })
                {
                    var name = roul.GetRowOrDefault(id)?.Name.ExtractText();
                    if (string.IsNullOrWhiteSpace(name) || ui == null) continue;
                    var done = ui->InstanceContent.IsRouletteComplete(id);
                    Row(done, name, done ? "сделано сегодня" : id == 1 ? "самый выгодный бонус опыта на прокачке" : "", false);
                }
            });
        else
            Theme.Panel("ebcraft", () =>
            {
                Theme.SubHeader("Для ремесла и сбора");
                ImGui.TextColored(Theme.TextDim, "Ежедневно: поставки Grand Company (большой опыт), левы; с 60 ур. — Custom Deliveries; Ishgard Restoration и Cosmic Exploration. Подробнее — «Прокачка ремесла и сбора».");
            });
    }
}
