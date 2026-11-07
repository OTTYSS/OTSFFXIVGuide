using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Lumina.Excel.Sheets;

namespace FFXIVGuide.Data;

public sealed class MechanicEntry
{
    public string Ability { get; set; } = "";
    public string What { get; set; } = "";
    public string[] Roles { get; set; } = [];
    public string Tip { get; set; } = "";

    public bool ForRole(PlayerRole role) => role switch
    {
        PlayerRole.Tank => Roles.Contains("tank") || Roles.Contains("all"),
        PlayerRole.Healer => Roles.Contains("healer") || Roles.Contains("all"),
        PlayerRole.Dps => Roles.Contains("dps") || Roles.Contains("all"),
        _ => true,
    };
}

public sealed class BossEntry
{
    public string Name { get; set; } = "";
    public List<MechanicEntry> Mechanics { get; set; } = new();
}

public sealed class DutyMechanics
{
    public string Duty { get; set; } = "";
    public List<BossEntry> Bosses { get; set; } = new();
}

public enum PlayerRole { Any, Tank, Healer, Dps }

/// <summary>
/// База механик боссов (встроена в плагин, собрана с ffxiv.consolegameswiki.com).
/// Покрывает данжи и триалы Stormblood и Shadowbringers.
/// </summary>
public static class MechanicsDb
{
    private static Dictionary<string, DutyMechanics>? byName;

    public static int Count => Load().Count;

    public static DutyMechanics? Find(string dutyName) =>
        Load().TryGetValue(Normalize(dutyName), out var m) ? m : null;

    /// <summary>Механики для данжа по ID территории (для окна внутри данжа).</summary>
    public static (DutyMechanics Mechanics, string DutyName)? FindByTerritory(uint territoryId)
    {
        foreach (var cfc in Plugin.Data.GetExcelSheet<ContentFinderCondition>())
        {
            if (cfc.TerritoryType.RowId != territoryId) continue;
            var name = cfc.Name.ExtractText();
            var m = Find(name);
            if (m is not null) return (m, GuideData.Capitalize(name));
        }
        return null;
    }

    public static PlayerRole CurrentRole()
    {
        if (!PlayerInfo.Ready) return PlayerRole.Any;
        return Plugin.PlayerState.ClassJob.ValueNullable?.Role switch
        {
            1 => PlayerRole.Tank,
            4 => PlayerRole.Healer,
            2 or 3 => PlayerRole.Dps,
            _ => PlayerRole.Any,
        };
    }

    private static string Normalize(string s)
    {
        s = s.Trim().ToLowerInvariant().Replace('’', '\'');
        return s.StartsWith("the ") ? s[4..] : s;
    }

    private static Dictionary<string, DutyMechanics> Load()
    {
        if (byName is not null) return byName;
        // Собираем в локальный словарь и публикуем целиком — загрузка может идти из фонового потока
        var result = new Dictionary<string, DutyMechanics>();

        var asm = typeof(MechanicsDb).Assembly;
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using var stream = asm.GetManifestResourceStream(res)!;
                var list = JsonSerializer.Deserialize<List<DutyMechanics>>(stream, options) ?? new();
                foreach (var d in list.Where(d => d.Duty.Length > 0))
                    result[Normalize(d.Duty)] = d;
            }
            catch (Exception e)
            {
                Plugin.Log.Warning(e, $"Не удалось загрузить механики из {res}");
            }
        }
        byName = result;
        return byName;
    }
}
