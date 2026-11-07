using System.Collections.Generic;
using Dalamud.Interface.ImGuiNotification;

namespace FFXIVGuide.Data;

/// <summary>Единый центр уведомлений: в чат, всплывающим окном или и то и другое — настраивается по типам.</summary>
public static class Notify
{
    public enum Kind { Weather, Retainer, Voyage, House, Event }

    public static readonly (Kind Kind, string Name)[] Kinds =
    {
        (Kind.Weather, "Погода (напоминания)"),
        (Kind.Retainer, "Ретейнеры вернулись"),
        (Kind.Voyage, "Субмарины и дирижабли вернулись"),
        (Kind.House, "Скоро снос дома"),
        (Kind.Event, "Началось сезонное событие"),
    };

    public static readonly string[] Modes = { "Выключено", "В чат", "Всплывающее", "Чат и всплывающее" };

    /// <summary>0 — выкл, 1 — чат, 2 — всплывающее, 3 — оба. По умолчанию — оба.</summary>
    public static int Mode(Kind k) => Plugin.Config.NotifyModes.TryGetValue(k.ToString(), out var m) ? m : 3;

    public static void SetMode(Kind k, int mode)
    {
        Plugin.Config.NotifyModes[k.ToString()] = mode;
        Plugin.Config.Save();
    }

    public static void Send(Kind k, string title, string text)
    {
        var mode = Mode(k);
        if ((mode & 1) != 0) Plugin.Chat.Print($"[FFXIV Guide] {text}");
        if ((mode & 2) != 0)
            Plugin.Notifications.AddNotification(new Notification
            {
                Title = "FFXIV Guide · " + title,
                Content = text,
                Type = NotificationType.Info,
            });
    }
}
