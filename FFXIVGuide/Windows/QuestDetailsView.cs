using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVGuide.Data;

namespace FFXIVGuide.Windows;

/// <summary>Подробная карточка квеста: что сделать, описание, награды, цепочка.</summary>
internal sealed class QuestDetailsView
{
    private readonly GuideData data;
    private bool showSpoilers;

    // Язык текстов квеста: общий для всех вкладок, 0 — English, 1 — Русский
    private static int language = Plugin.Config.QuestTextRussian ? 1 : 0;
    private static readonly string[] LanguageNames = { "English", "Русский" };

    private static bool Russian => language == 1;

    /// <summary>Текст на выбранном языке; если перевода ещё нет — оригинал.</summary>
    private static string Tr(string text)
    {
        if (!Russian) return text;
        Translator.Instance.Request([text]);
        return Translator.Instance.Get(text) ?? text;
    }

    public QuestDetailsView(GuideData data) => this.data = data;

    /// <param name="open">Что делать при клике на связанный квест.</param>
    public void Draw(QuestEntry q, Action<QuestEntry> open)
    {
        WikiNav.Visit(new WikiLink(WikiKind.Quest, q.Id, q.Name));
        var details = QuestText.Load(q.Id);
        var done = PlayerInfo.IsQuestDone(q.Id);
        var seq = QuestText.CurrentSequence(q.Id);

        Theme.Panel($"qd{q.Id}", () =>
        {
            var pos = ImGui.GetCursorScreenPos();
            Theme.GameIcon(q.Icon, pos, 44 * Theme.S);
            ImGui.SetCursorScreenPos(pos + new Vector2(54 * Theme.S, 0));
            ImGui.BeginGroup();
            Theme.LargeText(Theme.Gold, q.Name);
            ImGui.TextColored(Theme.TextDim, $"{q.Genre} · {q.Category}");
            ImGui.EndGroup();
            ImGui.Dummy(new Vector2(0, 4 * Theme.S));

            var (badge, color) = QuestsTab.StateBadge(q);
            if (badge is not null) { Theme.InlineBadge(badge, color); ImGui.SameLine(); }
            Theme.InlineBadge($"Ур. {q.Level}", Theme.TextDim);
            if (q.Expansion.Length > 0) { ImGui.SameLine(); Theme.InlineBadge(q.Expansion, Theme.TextDim); }

            ImGui.Dummy(new Vector2(0, 2 * Theme.S));
            Theme.FaIcon(FontAwesomeIcon.MapMarkerAlt, Theme.GoldDim);
            ImGui.SameLine();
            ImGui.TextColored(Theme.Text, q.Place.Length > 0 ? q.Place : "место не указано");

            ImGui.Dummy(new Vector2(0, 2 * Theme.S));
            if (Theme.IconButton("flag", FontAwesomeIcon.Flag, "Флаг на карте"))
                PlayerInfo.FlagQuestGiver(q);
            Theme.Tooltip("Флаг у NPC, который выдаёт квест");
            ImGui.SameLine(0, 16 * Theme.S);
            Theme.Chips("qlang", LanguageNames, ref language);
            Theme.Tooltip("Перевод шагов и описания (Google Translate) прямо здесь");

            if (Russian)
            {
                var texts = details.Steps.Select(x => x.Text).Concat(details.Journal).ToList();
                var tr = Translator.Instance;
                if (texts.Any(tr.IsLoading))
                {
                    ImGui.TextColored(Theme.TextDim, "Перевожу…");
                }
                else if (texts.Any(tr.IsFailed))
                {
                    ImGui.TextColored(Theme.Red, "Часть текста не удалось перевести — показан оригинал.");
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Повторить"))
                        tr.RetryFailed();
                }
            }
        });

        // ── Что сделать
        if (details.Steps.Length > 0)
        {
            Theme.Panel($"qsteps{q.Id}", () =>
            {
                Theme.SubHeader("Что нужно сделать");
                var anyHidden = false;
                var shownFuture = 0;
                foreach (var step in details.Steps)
                {
                    var state = QuestText.StateOf(step, q.Id, seq);

                    // Без спойлеров: все сделанные, текущие и один следующий шаг
                    if (state == StepState.Future && !showSpoilers && !done)
                    {
                        if (shownFuture >= 1) { anyHidden = true; continue; }
                        shownFuture++;
                    }

                    var (icon, col, textCol) = state switch
                    {
                        StepState.Done => (FontAwesomeIcon.CheckCircle, Theme.Green, Theme.TextDim),
                        StepState.Active => (FontAwesomeIcon.ArrowRight, Theme.Gold, Theme.Gold),
                        _ => (FontAwesomeIcon.Lock, Theme.Gray, Theme.Text),
                    };
                    Theme.FaIcon(icon, col);
                    ImGui.SameLine();
                    ImGui.TextColored(textCol, Tr(step.Text));
                }

                if (anyHidden)
                    ImGui.TextColored(Theme.Gray, "…дальше скрыто, чтобы не спойлерить");
            });
        }

        // ── Описание из журнала
        if (details.Journal.Length > 0)
        {
            Theme.Panel($"qjournal{q.Id}", () =>
            {
                Theme.SubHeader("Описание");
                var visible = done || showSpoilers || seq == 255
                    ? details.Journal.Length
                    : Math.Clamp(seq + 1, 1, details.Journal.Length);

                for (var i = 0; i < visible; i++)
                {
                    ImGui.TextColored(i == visible - 1 && !done ? Theme.Text : Theme.TextDim, Tr(details.Journal[i]));
                    ImGui.Dummy(new Vector2(0, 2 * Theme.S));
                }

                if (visible < details.Journal.Length)
                    ImGui.TextColored(Theme.Gray, "…продолжение откроется по ходу квеста");

                ImGui.Checkbox("Показывать спойлеры", ref showSpoilers);
                ImGui.SameLine(0, 16 * Theme.S);

                var text = string.Join("\n\n", details.Journal.Take(visible).Select(Tr));
                if (ImGui.SmallButton("Копировать"))
                    ImGui.SetClipboardText(text);
            });
        }

        // ── Награды и что открывает
        var hasRewards = details.Gil > 0 || details.Rewards.Count > 0 || details.OptionalRewards.Count > 0 || q.Unlocks.Length > 0;
        if (hasRewards)
        {
            Theme.Panel($"qrew{q.Id}", () =>
            {
                Theme.SubHeader("Награды");
                if (details.Gil > 0)
                {
                    Theme.FaIcon(FontAwesomeIcon.Gem, Theme.Gold);
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.Text, $"{details.Gil:N0} гил");
                }
                foreach (var r in details.Rewards)
                {
                    Theme.FaIcon(FontAwesomeIcon.Star, Theme.Gold);
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.Text, r);
                }
                if (details.OptionalRewards.Count > 0)
                {
                    ImGui.TextColored(Theme.TextDim, "На выбор:");
                    foreach (var r in details.OptionalRewards)
                    {
                        Theme.FaIcon(FontAwesomeIcon.Star, Theme.GoldDim);
                        ImGui.SameLine();
                        ImGui.TextColored(Theme.Text, r);
                    }
                }
                foreach (var u in q.Unlocks.Split(", ", StringSplitOptions.RemoveEmptyEntries))
                {
                    Theme.FaIcon(FontAwesomeIcon.LockOpen, Theme.Blue);
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.Blue, $"Открывает {u}");
                }
            });
        }

        // ── Вики: награды и предметы кликабельно, что дальше по цепочке
        var wq = WikiService.Instance.Quest(q.Id);
        Theme.Panel($"qwiki{q.Id}", () =>
        {
            Theme.SubHeader("Предметы и цепочка");
            WikiNav.FavoriteButton(new WikiLink(WikiKind.Quest, q.Id, q.Name));
            if (!WikiView.Status(wq, "quest", q.Id)) return;
            var w = wq!;
            if (w.Issuer is { } iss && iss.Name.Length > 0)
            {
                Theme.FaIcon(FontAwesomeIcon.User, Theme.GoldDim);
                ImGui.SameLine();
                if (ImGui.Selectable($"Выдаёт: {iss.Name}##qiss", false, ImGuiSelectableFlags.None, new Vector2(ImGui.CalcTextSize($"Выдаёт: {iss.Name}").X, 0)))
                    WikiNav.Open(WikiKind.Npc, iss.Id, iss.Name);
                Theme.Tooltip("Открыть карточку NPC");
            }
            if (w.Xp > 0) ImGui.TextColored(Theme.TextDim, $"Опыт: {w.Xp:N0}");
            if (w.Rewards.Count > 0)
            {
                ImGui.TextColored(Theme.TextDim, "Награда:");
                foreach (var r in w.Rewards) WikiView.ItemLine(r.Item, r.Amount, uid: "qr");
            }
            if (w.Optional.Count > 0)
            {
                ImGui.TextColored(Theme.TextDim, "На выбор (одна вещь):");
                foreach (var r in w.Optional) WikiView.ItemLine(r.Item, r.Amount, uid: "qo");
            }
            if (w.UsedItems.Count > 0)
            {
                ImGui.TextColored(Theme.TextDim, "Понадобятся предметы:");
                foreach (var r in w.UsedItems) WikiView.ItemLine(r, uid: "qu");
            }
            if (w.Next.Count > 0)
            {
                ImGui.TextColored(Theme.TextDim, "Дальше по цепочке:");
                foreach (var n in w.Next)
                {
                    if (!uint.TryParse(n.Id, out var nid) || !data.Quests.TryGetValue(nid, out var nq)) continue;
                    var (b, c) = QuestsTab.StateBadge(nq);
                    if (Theme.Card($"##qn{nid}", nq.Icon, nq.Name, $"Ур. {nq.Level} · {nq.Place}", b, c, false, 40 * Theme.S))
                        open(nq);
                }
            }
        });

        // ── Цепочка
        if (q.Previous.Length > 0)
        {
            Theme.Panel($"qprev{q.Id}", () =>
            {
                Theme.SubHeader("Требует");
                foreach (var id in q.Previous)
                {
                    if (!data.Quests.TryGetValue(id, out var prev)) continue;
                    var prevDone = PlayerInfo.IsQuestDone(id);
                    Theme.FaIcon(prevDone ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.Lock, prevDone ? Theme.Green : Theme.Gray);
                    ImGui.SameLine();
                    if (ImGui.Selectable($"{prev.Name}##prev{id}"))
                        open(prev);
                }
            });
        }

        if (!PlayerInfo.Ready || done)
            return;

        var next = PlayerInfo.NextStepTowards(data, q.Id);
        if (next is null || next.Id == q.Id)
            return;

        Theme.Panel($"qnext{q.Id}", () =>
        {
            Theme.SubHeader("Сначала сделать");
            if (Theme.Card("##next", next.Icon, next.Name, $"Ур. {next.Level} · {next.Place}", null, Theme.Gray, false, 44 * Theme.S))
                open(next);
            if (Theme.IconButton("nextflag", FontAwesomeIcon.Flag, "Флаг на карте"))
                PlayerInfo.FlagQuestGiver(next);
        });
    }
}
