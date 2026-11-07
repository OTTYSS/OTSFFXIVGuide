using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace FFXIVGuide.Windows;

public enum WikiKind { Item, Mob, Duty, Zone, Npc, Quest }

/// <summary>Ссылка на карточку вики. Для данжа Id — ContentFinderCondition, для зоны — PlaceName.</summary>
public sealed record WikiLink(WikiKind Kind, ulong Id, string Name)
{
    public string Key => $"{Kind}|{Id}";
    public override string ToString() => $"{Kind}|{Id}|{Name}";

    public static WikiLink? Parse(string s)
    {
        var p = s.Split('|', 3);
        return p.Length == 3 && Enum.TryParse<WikiKind>(p[0], out var k) && ulong.TryParse(p[1], out var id) ? new WikiLink(k, id, p[2]) : null;
    }

    public FontAwesomeIcon Icon => Kind switch
    {
        WikiKind.Item => FontAwesomeIcon.Gem,
        WikiKind.Mob => FontAwesomeIcon.Skull,
        WikiKind.Duty => FontAwesomeIcon.Dungeon,
        WikiKind.Zone => FontAwesomeIcon.Map,
        WikiKind.Npc => FontAwesomeIcon.User,
        _ => FontAwesomeIcon.Scroll,
    };

    public string KindName => Kind switch
    {
        WikiKind.Item => "предмет",
        WikiKind.Mob => "моб",
        WikiKind.Duty => "данж",
        WikiKind.Zone => "зона",
        WikiKind.Npc => "NPC",
        _ => "квест",
    };
}

/// <summary>
/// Навигация по вики: «Назад/Вперёд» через все разделы, недавние карточки и избранное.
/// </summary>
internal static class WikiNav
{
    /// <summary>Открывает карточку в нужном разделе (задаёт MainWindow).</summary>
    public static Action<WikiLink>? Navigate;

    public static WikiLink? Current { get; private set; }
    private static readonly List<WikiLink> back = new();
    private static readonly List<WikiLink> forward = new();
    private static readonly List<WikiLink> recent = new();

    public static IReadOnlyList<WikiLink> Recent => recent;
    public static bool CanBack => back.Count > 0;
    public static bool CanForward => forward.Count > 0;

    /// <summary>Отметить, что пользователь смотрит карточку (без перехода).</summary>
    public static void Visit(WikiLink link)
    {
        if (Current?.Key == link.Key) { Current = link; return; }
        if (Current is not null)
        {
            back.Add(Current);
            if (back.Count > 100) back.RemoveAt(0);
        }
        forward.Clear();
        Current = link;
        recent.RemoveAll(r => r.Key == link.Key);
        recent.Insert(0, link);
        if (recent.Count > 40) recent.RemoveAt(recent.Count - 1);
    }

    /// <summary>Перейти к карточке из любого места.</summary>
    public static void Open(WikiLink link)
    {
        Visit(link);
        Navigate?.Invoke(link);
    }

    public static void Open(WikiKind kind, ulong id, string name) => Open(new WikiLink(kind, id, name));

    public static void Back()
    {
        if (back.Count == 0) return;
        var to = back[^1];
        back.RemoveAt(back.Count - 1);
        if (Current is not null) forward.Add(Current);
        Current = to;
        Navigate?.Invoke(to);
    }

    public static void Forward()
    {
        if (forward.Count == 0) return;
        var to = forward[^1];
        forward.RemoveAt(forward.Count - 1);
        if (Current is not null) back.Add(Current);
        Current = to;
        Navigate?.Invoke(to);
    }

    // ───────────── Избранное ─────────────

    public static IEnumerable<WikiLink> Favorites =>
        Plugin.Config.WikiFavorites.Select(WikiLink.Parse).Where(l => l is not null)!;

    public static bool IsFavorite(WikiLink l) => Plugin.Config.WikiFavorites.Any(s => s.StartsWith(l.Key + "|"));

    public static void ToggleFavorite(WikiLink l)
    {
        if (IsFavorite(l)) Plugin.Config.WikiFavorites.RemoveAll(s => s.StartsWith(l.Key + "|"));
        else Plugin.Config.WikiFavorites.Insert(0, l.ToString());
        Plugin.Config.Save();
    }

    /// <summary>Звёздочка «в избранное» для карточки.</summary>
    public static void FavoriteButton(WikiLink l)
    {
        var fav = IsFavorite(l);
        if (Theme.IconButton($"fav{l.Key}", fav ? FontAwesomeIcon.Star : FontAwesomeIcon.StarHalfAlt, fav ? "В избранном" : "В избранное"))
            ToggleFavorite(l);
        Theme.Tooltip(fav ? "Убрать из избранного" : "Добавить в избранное (раздел «Вики»)");
    }
}
