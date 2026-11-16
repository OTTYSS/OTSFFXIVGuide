using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Мини-игры Gold Saucer: гоночный чокобо, Lord of Verminion, Doman Mahjong — прогресс достижений и краткие правила.</summary>
internal sealed class MiniGamesTab
{
    private int game;
    private static readonly string[] Games = { "Chocobo Racing", "Lord of Verminion", "Doman Mahjong" };
    private static readonly string[][] Keys =
    {
        new[] { "chocobo race", "chocobo racing", "race chocobo", "racing" },
        new[] { "verminion" },
        new[] { "mahjong" },
    };

    private sealed record Ach(uint Id, string Name, string Desc, uint Icon);
    private static List<Ach>[]? achs;

    private static List<Ach>[] Achievements()
    {
        if (achs is not null) return achs;
        achs = new[] { new List<Ach>(), new List<Ach>(), new List<Ach>() };
        var en = Plugin.Data.GetExcelSheet<Achievement>(Dalamud.Game.ClientLanguage.English);
        var loc = Plugin.Data.GetExcelSheet<Achievement>();
        foreach (var a in en)
        {
            var text = (a.Name.ExtractText() + " " + a.Description.ExtractText()).ToLowerInvariant();
            if (text.Length < 3) continue;
            for (var g = 0; g < Keys.Length; g++)
                if (Keys[g].Any(text.Contains))
                {
                    var l = loc.GetRowOrDefault(a.RowId);
                    achs[g].Add(new Ach(a.RowId, l?.Name.ExtractText() ?? a.Name.ExtractText(), l?.Description.ExtractText() ?? "", a.Icon));
                    break;
                }
        }
        return achs;
    }

    private static readonly string[] Rules =
    {
        "Гонки на своём гоночном чокобо в Gold Saucer (Chocobo Square). Чокобо качается гонками до 40 ранга; сильнее всего характеристики растут " +
        "от скрещивания (pedigree): от пары родителей с хорошими звёздами рождается чокобо с более высоким потолком. Тренировочные корма поднимают характеристики до потолка.",
        "Тактическая мини-игра на твоих минионах: у каждого миниона свой тип (монстр, гаджет, бестия и др.), скорость, здоровье и навык. " +
        "Задача — разрушить ворота или «сердце» соперника. Режим Challenge против NPC даёт награды и достижения; чем больше у тебя минионов, тем больше вариантов отряда.",
        "Японский маджонг на 4 игроков в Gold Saucer: собираешь комбинации из 14 тайлов. Рейтинговые партии повышают ранг, за ранги и игры — MGP и достижения. " +
        "Для начала хватит режима против NPC.",
    };

    public unsafe void Draw()
    {
        Theme.Header("Мини-игры Gold Saucer", "Гоночный чокобо, Lord of Verminion и Doman Mahjong: правила коротко, прогресс и достижения");
        Theme.Chips("mggame", Games, ref game);
        Theme.Panel("mgrules", () => ImGui.TextColored(Theme.TextDim, Rules[game]));

        if (game == 0 && PlayerInfo.Ready)
        {
            var rc = RaceChocoboManager.Instance();
            Theme.Panel("mgchoco", () =>
            {
                Theme.SubHeader("Твой гоночный чокобо");
                if (rc == null || rc->State != RaceChocoboManager.RaceChocoboState.Loaded)
                {
                    ImGui.TextColored(Theme.TextDim, "Данные появятся после того, как откроешь окно чокобо у Race Chocobo Registrar в Gold Saucer.");
                    return;
                }
                ImGui.TextColored(Theme.Text, $"Рейтинг: {rc->GetRating()} · Родословная (pedigree): {rc->GetPedigreeLevel()}");
                RaceChocoboAttributeValues cur, max, stars;
                rc->GetAttributesCurrent(&cur);
                rc->GetAttributesMaximum(&max);
                rc->GetAttributesStars(&stars);
                void Row(string name, uint c, uint m, uint s)
                {
                    ImGui.TextColored(Theme.TextDim, name);
                    ImGui.SameLine(140 * Theme.S);
                    Theme.Progress($"{c} / {m}  ·  звёзд {s}", (int)c, (int)Math.Max(1, m), c >= m ? Theme.Green : Theme.Gold);
                }
                Row("Скорость", cur.MaximumSpeed, max.MaximumSpeed, stars.MaximumSpeed);
                Row("Ускорение", cur.Acceleration, max.Acceleration, stars.Acceleration);
                Row("Выносливость", cur.Endurance, max.Endurance, stars.Endurance);
                Row("Стамина", cur.Stamina, max.Stamina, stars.Stamina);
                Row("Хитрость", cur.Cunning, max.Cunning, stars.Cunning);
                ImGui.TextColored(Theme.Gray, "Когда характеристики упёрлись в максимум — пора скрещивать (потолок растёт от звёзд родителей).");
            });
        }

        var list = Achievements()[game];
        var ach = PlayerInfo.Ready ? FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement.Instance() : null;
        Theme.Panel("mgach", () =>
        {
            var done = ach == null ? 0 : list.Count(a => ach->IsComplete((int)a.Id));
            Theme.SubHeader("Достижения");
            Theme.Progress("Получено", done, list.Count, Theme.Gold);
            if (ach == null) ImGui.TextColored(Theme.TextDim, "Зайди персонажем, чтобы увидеть, что получено. Если всё по нулям — открой в игре окно достижений один раз.");
            foreach (var a in list)
            {
                var ok = ach != null && ach->IsComplete((int)a.Id);
                Theme.GameIcon(a.Icon, ImGui.GetCursorScreenPos(), 24 * Theme.S, 4f);
                ImGui.Dummy(new System.Numerics.Vector2(28 * Theme.S, 24 * Theme.S));
                ImGui.SameLine();
                ImGui.BeginGroup();
                ImGui.TextColored(ok ? Theme.Green : Theme.Text, a.Name + (ok ? " ✓" : ""));
                ImGui.TextColored(Theme.TextDim, a.Desc);
                ImGui.EndGroup();
            }
        });
    }
}
