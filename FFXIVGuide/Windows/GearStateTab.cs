using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

/// <summary>Износ снаряжения, духовная связь (извлечение материи) и еда: состояние и настройки напоминаний.</summary>
internal sealed class GearStateTab
{
    public void Draw()
    {
        Theme.Header("Износ и духовная связь", "Прочность снаряжения, что можно извлечь в материю, еда — и напоминания об этом");
        if (!PlayerInfo.Ready) { Theme.NeedLogin("Зайди в игру персонажем: снаряжение и эффекты берутся из игры."); return; }
        var cfg = Plugin.Config;
        var gear = Watchers.ReadGear(true);
        var equipped = gear.Where(g => g.Equipped).ToList();
        var extract = gear.Where(g => g.Spiritbond >= 100f).ToList();

        Theme.Grid("gscols", 2, 380f, col =>
        {
            if (col == 0)
                Theme.Panel("gseq", () =>
                {
                    Theme.SubHeader("Надетое снаряжение");
                    if (equipped.Count == 0) { ImGui.TextColored(Theme.TextDim, "Ничего не надето."); return; }
                    var minCond = equipped.Min(g => g.Condition);
                    ImGui.TextColored(minCond < cfg.RepairWarnPercent ? Theme.Red : Theme.Green, $"Худшая прочность: {minCond:0}%");
                    foreach (var g in equipped)
                    {
                        Theme.GameIcon(g.Icon, ImGui.GetCursorScreenPos(), 24 * Theme.S, 4f);
                        ImGui.Dummy(new Vector2(28 * Theme.S, 24 * Theme.S));
                        ImGui.SameLine();
                        ImGui.BeginGroup();
                        ImGui.TextUnformatted(g.Name);
                        Bar(g.Condition, g.Condition < cfg.RepairWarnPercent ? Theme.Red : g.Condition < 50 ? Theme.Yellow : Theme.Green, $"прочность {g.Condition:0}%");
                        ImGui.SameLine();
                        Bar(g.Spiritbond, g.Spiritbond >= 100 ? Theme.Gold : Theme.Blue, $"связь {g.Spiritbond:0}%");
                        ImGui.EndGroup();
                    }
                });
            else
            {
                Theme.Panel("gsext", () =>
                {
                    Theme.SubHeader($"Можно извлечь материю: {extract.Count}");
                    ImGui.TextColored(Theme.TextDim, "Духовная связь 100% — в меню предмета «Extract Materia» (открывается квестом «Forging the Spirit»).");
                    foreach (var g in extract)
                    {
                        Theme.FaIcon(FontAwesomeIcon.Gem, Theme.Gold);
                        ImGui.SameLine();
                        ImGui.TextUnformatted(g.Name);
                        ImGui.SameLine();
                        ImGui.TextColored(Theme.Gray, g.Equipped ? "надето" : "в шкафу");
                    }
                });
                Theme.Panel("gsfood", () =>
                {
                    Theme.SubHeader("Еда");
                    var left = Watchers.FoodSecondsLeft();
                    ImGui.TextColored(left > 0 ? Theme.Green : Theme.Yellow, left > 0 ? $"Эффект еды: ещё {Math.Ceiling(left / 60)} мин" : "Сейчас на тебе нет еды");
                    var food = Watchers.FoodInBags();
                    if (food.Count == 0) ImGui.TextColored(Theme.TextDim, "Еды в сумках нет.");
                    foreach (var f in food.Take(8))
                    {
                        if (ImGui.Selectable($"{f.Name} ×{f.Count}##gsf{f.Id}")) WikiNav.Open(WikiKind.Item, f.Id, f.Name);
                    }
                });
            }
        });

        Theme.Panel("gscfg", () =>
        {
            Theme.SubHeader("Напоминания");
            var changed = false;
            var rep = cfg.RepairWarnPercent;
            ImGui.SetNextItemWidth(200 * Theme.S);
            if (ImGui.SliderInt("Предупреждать, когда прочность ниже (0 — выкл)##gsrep", ref rep, 0, 90, "%d%%")) { cfg.RepairWarnPercent = rep; changed = true; }
            var sb = cfg.SpiritbondWarn;
            if (ImGui.Checkbox("Сообщать, когда можно извлечь материю##gssb", ref sb)) { cfg.SpiritbondWarn = sb; changed = true; }
            var fr = cfg.FoodReminder;
            if (ImGui.Checkbox("Напоминать о еде в начале данжа или рейда##gsfr", ref fr)) { cfg.FoodReminder = fr; changed = true; }
            if (fr)
            {
                ImGui.SameLine();
                var fm = cfg.FoodWarnMinutes;
                ImGui.SetNextItemWidth(120 * Theme.S);
                if (ImGui.SliderInt("если меньше, мин##gsfm", ref fm, 1, 30)) { cfg.FoodWarnMinutes = fm; changed = true; }
            }
            ImGui.TextColored(Theme.Gray, "Куда присылать (чат или всплывающее окно) — Настройки → Уведомления. Плагин только напоминает и ничего не нажимает.");
            if (changed) cfg.Save();
        });
    }

    private static void Bar(float pct, Vector4 color, string label)
    {
        var w = 120 * Theme.S;
        var h = ImGui.GetTextLineHeight();
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(w, h), Theme.U32(Theme.CardBg), 3f * Theme.S);
        dl.AddRectFilled(pos, pos + new Vector2(w * Math.Clamp(pct / 100f, 0f, 1f), h), Theme.U32(Theme.Alpha(color, 0.7f)), 3f * Theme.S);
        var ts = ImGui.CalcTextSize(label);
        dl.AddText(pos + new Vector2((w - ts.X) / 2, 0), Theme.U32(Theme.Text), label);
        ImGui.Dummy(new Vector2(w, h));
    }
}
