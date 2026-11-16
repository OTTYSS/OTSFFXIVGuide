using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>Сквадроны Grand Company: бойцы и их характеристики, миссии и подбор состава под требования миссии.</summary>
internal sealed class SquadronTab
{
    private sealed record Member(string Name, uint Job, int Level, int Phys, int Ment, int Tact);

    private uint mission;
    private int variant;

    private static unsafe (List<Member> Members, int BonusP, int BonusM, int BonusT)? Read()
    {
        var mgr = GcArmyManager.Instance();
        if (mgr == null || mgr->Data == null) return null;
        var d = mgr->Data;
        var grow = Plugin.Data.GetExcelSheet<GcArmyMemberGrow>();
        var res = Plugin.Data.GetExcelSheet<ENpcResident>();
        var list = new List<Member>();
        for (var i = 0; i < d->MemberCount && i < d->Members.Length; i++)
        {
            var m = d->Members[i];
            if (m.ENpcResidentId == 0) continue;
            var g = grow.FirstOrDefault(r => r.ClassJob.RowId == m.ClassJob);
            int p = 0, me = 0, t = 0;
            if (g.RowId != 0 && g.MemberParams.Count > 0)
            {
                // Строка таблицы роста = уровень бойца
                var row = g.MemberParams[Math.Clamp((int)m.Level, 0, g.MemberParams.Count - 1)];
                p = row.Physical; me = row.Mental; t = row.Tactical;
            }
            var name = res.GetRowOrDefault(m.ENpcResidentId)?.Singular.ExtractText() ?? $"Боец {i + 1}";
            list.Add(new Member(GuideData.Capitalize(name), m.ClassJob, m.Level, p, me, t));
        }
        return (list, d->BonusPhysical, d->BonusMental, d->BonusTactical);
    }

    public void Draw()
    {
        Theme.Header("Сквадроны", "Бойцы Grand Company, миссии и какой состав из 4 человек проходит требования");
        if (!PlayerInfo.Ready) { Theme.NeedLogin("Зайди в игру персонажем: состав сквадрона берётся из игры."); return; }
        var data = Read();
        if (data is null || data.Value.Members.Count == 0)
        {
            Theme.EmptyState(FontAwesomeIcon.Users, "Сквадрон не загружен", "Открой в казарме Grand Company доску Squadron Missions или список бойцов — плагин увидит состав. Сквадрон открывается квестом на 2nd Lieutenant ранге GC.");
            return;
        }
        var (members, bp, bm, bt) = data.Value;
        var jobs = Plugin.Data.GetExcelSheet<ClassJob>();

        Theme.Panel("sqmem", () =>
        {
            Theme.SubHeader($"Бойцы ({members.Count})");
            ImGui.TextColored(Theme.TextDim, $"Бонус обучения отряда: физика +{bp}, ментал +{bm}, тактика +{bt}. Характеристики бойцов — по таблице роста игры, примерные.");
            if (!ImGui.BeginTable("##sqm", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) return;
            ImGui.TableSetupColumn("Боец", ImGuiTableColumnFlags.None, 2f);
            ImGui.TableSetupColumn("Класс", ImGuiTableColumnFlags.None, 1.2f);
            ImGui.TableSetupColumn("Физика");
            ImGui.TableSetupColumn("Ментал");
            ImGui.TableSetupColumn("Тактика");
            ImGui.TableHeadersRow();
            foreach (var m in members)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextUnformatted(m.Name);
                ImGui.TableNextColumn(); ImGui.TextColored(Theme.TextDim, $"{GuideData.Capitalize(jobs.GetRowOrDefault(m.Job)?.Name.ExtractText() ?? "")} {m.Level}");
                ImGui.TableNextColumn(); ImGui.TextUnformatted(m.Phys.ToString());
                ImGui.TableNextColumn(); ImGui.TextUnformatted(m.Ment.ToString());
                ImGui.TableNextColumn(); ImGui.TextUnformatted(m.Tact.ToString());
            }
            ImGui.EndTable();
        });

        var missions = Plugin.Data.GetExcelSheet<GcArmyExpedition>().Where(e => e.RowId > 0 && !e.Name.IsEmpty).ToList();
        if (mission == 0) mission = missions.FirstOrDefault(e => e.RequiredLevel <= members.Max(m => m.Level) && e.GcArmyExpeditionType.RowId == 3).RowId;
        var cur = missions.FirstOrDefault(e => e.RowId == mission);
        Theme.Panel("sqmis", () =>
        {
            Theme.SubHeader("Подбор состава под миссию");
            ImGui.SetNextItemWidth(360 * Theme.S);
            if (ImGui.BeginCombo("##sqmission", cur.RowId == 0 ? "Миссия" : $"{cur.Name.ExtractText()} (ур. {cur.RequiredLevel})", ImGuiComboFlags.HeightLarge))
            {
                foreach (var g in missions.GroupBy(e => e.GcArmyExpeditionType.ValueNullable?.Name.ExtractText() ?? ""))
                {
                    ImGui.TextColored(Theme.Gold, g.Key);
                    foreach (var e in g)
                        if (ImGui.Selectable($"   {e.Name.ExtractText()} · ур. {e.RequiredLevel}##sqe{e.RowId}", e.RowId == mission)) { mission = e.RowId; variant = 0; }
                }
                ImGui.EndCombo();
            }
            if (cur.RowId == 0) return;
            var vars = cur.ExpeditionParams.Select(p => (P: (int)p.RequiredPhysical, M: (int)p.RequiredMental, T: (int)p.RequiredTactical)).Distinct().ToList();
            if (vars.Count > 1)
            {
                ImGui.TextColored(Theme.TextDim, "Требования у миссии меняются — выбери те, что видишь в игре:");
                for (var i = 0; i < vars.Count; i++)
                {
                    if (i > 0) ImGui.SameLine();
                    if (ImGui.RadioButton($"{vars[i].P}/{vars[i].M}/{vars[i].T}##sqv{i}", variant == i)) variant = i;
                }
            }
            variant = Math.Clamp(variant, 0, vars.Count - 1);
            var req = vars[variant];
            ImGui.TextColored(Theme.Text, $"Нужно: физика {req.P}, ментал {req.M}, тактика {req.T}");

            // Перебираем все составы из 4 бойцов
            var best = Combos(members.Count, 4)
                .Select(c =>
                {
                    var p = c.Sum(i => members[i].Phys) + bp;
                    var m = c.Sum(i => members[i].Ment) + bm;
                    var t = c.Sum(i => members[i].Tact) + bt;
                    var met = (p >= req.P ? 1 : 0) + (m >= req.M ? 1 : 0) + (t >= req.T ? 1 : 0);
                    var deficit = Math.Max(0, req.P - p) + Math.Max(0, req.M - m) + Math.Max(0, req.T - t);
                    return (c, p, m, t, met, deficit, surplus: p + m + t);
                })
                .OrderByDescending(x => x.met).ThenBy(x => x.deficit).ThenByDescending(x => x.surplus).Take(3).ToList();
            if (best.Count == 0) { ImGui.TextColored(Theme.TextDim, "Нужно минимум 4 бойца."); return; }
            var n = 0;
            foreach (var b in best)
            {
                n++;
                var col = b.met == 3 ? Theme.Green : b.met == 2 ? Theme.Yellow : Theme.Red;
                ImGui.TextColored(col, $"{n}. {string.Join(", ", b.c.Select(i => members[i].Name))}");
                ImGui.TextColored(Theme.TextDim, $"    итого {b.p}/{b.m}/{b.t} — выполнено требований: {b.met} из 3" + (b.deficit > 0 ? $", не хватает {b.deficit}" : ""));
            }
            if (best[0].met < 3)
                ImGui.TextColored(Theme.Yellow, "Ни один состав не проходит всё: смени обучение отряда (Squadron Training) в сторону нехватки или подними бойцов.");
            var rewards = cur.ExpeditionParams.Where(p => p.RewardItem.RowId != 0).Select(p => $"{p.RewardItem.ValueNullable?.Name.ExtractText()} ×{p.RewardQuantity}").Distinct().ToList();
            if (rewards.Count > 0) ImGui.TextColored(Theme.Gray, "Награды: " + string.Join(", ", rewards));
        });
    }

    private static IEnumerable<int[]> Combos(int n, int k)
    {
        if (n < k) yield break;
        var idx = Enumerable.Range(0, k).ToArray();
        while (true)
        {
            yield return (int[])idx.Clone();
            var i = k - 1;
            while (i >= 0 && idx[i] == n - k + i) i--;
            if (i < 0) yield break;
            idx[i]++;
            for (var j = i + 1; j < k; j++) idx[j] = idx[j - 1] + 1;
        }
    }
}
