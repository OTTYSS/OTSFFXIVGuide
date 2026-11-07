using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.MJI;
using FFXIVGuide.Data;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Windows;

/// <summary>
/// Island Sanctuary: что выгодно делать в мастерской (цена × популярность / время, темы для бонуса),
/// что дают животные. Популярность и спрос игра отдаёт, когда ты на острове.
/// </summary>
internal sealed unsafe class IslandTab
{
    private static readonly string[] Sections = { "Мастерская", "Животные" };
    private int section;
    private int sort;
    private static readonly string[] Sorts = { "Выгода в час", "Цена", "Время" };

    private sealed record Craft(uint Id, uint Item, string Name, uint Icon, int Value, int Hours, int Level, uint[] Themes);
    private List<Craft>? crafts;

    private static string ThemeName(uint id) =>
        Plugin.Data.GetExcelSheet<MJICraftworksObjectTheme>().GetRowOrDefault(id)?.Name.ExtractText() ?? "";

    private List<Craft> Build() => Plugin.Data.GetExcelSheet<MJICraftworksObject>()
        .Where(c => c.Item.RowId != 0 && c.Value > 0)
        .Select(c => new Craft(c.RowId, c.Item.RowId, WikiView.ItemInfo(c.Item.RowId).Name.Replace("Isleworks ", ""), WikiView.ItemInfo(c.Item.RowId).Icon,
            c.Value, c.CraftingTime, c.LevelReq, c.Theme.Where(t => t.RowId != 0).Select(t => t.RowId).ToArray()))
        .ToList();

    public void Draw()
    {
        Theme.Header("Island Sanctuary", "Что выгодно делать в мастерской на этой неделе и что дают животные");
        Theme.Chips("isec", Sections, ref section);
        if (section == 1) { DrawAnimals(); return; }

        crafts ??= Build();
        var mji = MJIManager.Instance();
        var loaded = mji != null && mji->CurrentPopularity != 0;
        var pop = loaded ? Plugin.Data.GetExcelSheet<MJICraftworksPopularity>().GetRowOrDefault(mji->CurrentPopularity) : null;
        var next = loaded ? Plugin.Data.GetExcelSheet<MJICraftworksPopularity>().GetRowOrDefault(mji->NextPopularity) : null;

        double Ratio(MJICraftworksPopularity? p, uint id)
        {
            if (p is null || id >= p.Value.Popularity.Count) return 1;
            return (p.Value.Popularity[(int)id].ValueNullable?.Ratio ?? 100) / 100.0;
        }
        string PopName(double r) => r switch { >= 1.4 => "очень высокая", >= 1.2 => "высокая", >= 1.0 => "средняя", _ => "низкая" };

        Theme.Panel("itop", () =>
        {
            if (!loaded)
                ImGui.TextColored(Theme.Yellow, "Популярность и спрос видны, когда ты на острове. Сейчас — базовые цены без популярности.");
            else
                ImGui.TextColored(Theme.TextDim, "Популярность этой недели учтена. Бонус ×2 к следующему предмету, если у соседних предметов общая тема и время отличается.");
            Theme.Chips("isort", Sorts, ref sort);
        });

        var rows = crafts.Select(c =>
        {
            var r = Ratio(pop, c.Id);
            var supply = loaded ? mji->GetSupplyForCraftwork(c.Id) : (byte)0;
            return (c, r, rNext: Ratio(next, c.Id), supply, perHour: c.Value * r / Math.Max(1, c.Hours));
        }).ToList();
        rows = sort switch
        {
            1 => rows.OrderByDescending(x => x.c.Value * x.r).ToList(),
            2 => rows.OrderBy(x => x.c.Hours).ThenByDescending(x => x.perHour).ToList(),
            _ => rows.OrderByDescending(x => x.perHour).ToList(),
        };

        if (!ImGui.BeginChild("##isl", Vector2.Zero)) { ImGui.EndChild(); return; }
        if (ImGui.BeginTable("##icraft", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Предмет", ImGuiTableColumnFlags.WidthStretch, 3);
            ImGui.TableSetupColumn("Часов");
            ImGui.TableSetupColumn("Цена");
            ImGui.TableSetupColumn("Популярность");
            ImGui.TableSetupColumn("В час");
            ImGui.TableSetupColumn("Темы", ImGuiTableColumnFlags.WidthStretch, 2);
            ImGui.TableHeadersRow();
            foreach (var (c, r, rNext, supply, perHour) in rows)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); WikiView.ItemLine(c.Item, uid: "isl");
                ImGui.TableNextColumn(); ImGui.TextColored(Theme.Text, c.Hours.ToString());
                ImGui.TableNextColumn(); ImGui.TextColored(Theme.Text, $"{c.Value * r:0}");
                ImGui.TableNextColumn();
                ImGui.TextColored(r >= 1.2 ? Theme.Green : r < 1 ? Theme.Red : Theme.Text, loaded ? PopName(r) : "—");
                if (loaded && ImGui.IsItemHovered()) ImGui.SetTooltip($"Сейчас ×{r:0.0}, на следующей неделе ×{rNext:0.0}\nЗапас (supply): уровень {supply}");
                ImGui.TableNextColumn(); ImGui.TextColored(Theme.Gold, $"{perHour:0.0}");
                ImGui.TableNextColumn(); ImGui.TextColored(Theme.TextDim, string.Join(", ", c.Themes.Select(ThemeName)));
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();
    }

    private sealed record Animal(uint NameId, uint[] Rewards, int Size);
    private List<Animal>? animals;
    private int animalsVersion = -1;

    private void DrawAnimals()
    {
        if (animals is null || animalsVersion != OfflineDb.Version)
        {
            animalsVersion = OfflineDb.Version;
            animals = new List<Animal>();
            try
            {
                var f = Path.Combine(OfflineDb.Instance.Dir, "island-animals.json");
                if (File.Exists(f))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(f));
                    foreach (var p in doc.RootElement.EnumerateObject())
                    {
                        var o = p.Value;
                        var rewards = o.TryGetProperty("rewards", out var r) ? r.EnumerateArray().Select(x => x.GetUInt32()).ToArray() : Array.Empty<uint>();
                        animals.Add(new Animal(o.TryGetProperty("bnpcName", out var n) ? n.GetUInt32() : 0, rewards, o.TryGetProperty("size", out var s) ? s.GetInt32() : 1));
                    }
                }
            }
            catch (Exception e) { Plugin.Log.Warning(e, "Остров: не удалось прочитать животных"); }
        }
        if (animals.Count == 0) { ImGui.TextColored(Theme.TextDim, "Нет данных — обнови офлайн-базу в Настройках."); return; }
        if (!ImGui.BeginChild("##animals", Vector2.Zero)) { ImGui.EndChild(); return; }
        Theme.Grid("agrid", animals.Count, 340f, i =>
        {
            var a = animals[i];
            Theme.Panel($"an{i}", () =>
            {
                ImGui.TextColored(Theme.Gold, NameTranslations.Show(OfflineIndex.BNpcName(a.NameId)));
                ImGui.SameLine();
                ImGui.TextColored(Theme.TextDim, a.Size > 1 ? "крупное (2 места)" : "обычное");
                ImGui.TextColored(Theme.TextDim, "Даёт (leavings):");
                foreach (var r in a.Rewards) WikiView.ItemLine(r, uid: $"anr{i}");
            });
        });
        ImGui.EndChild();
    }
}
