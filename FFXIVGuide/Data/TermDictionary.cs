using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FFXIVGuide.Data;

/// <summary>
/// Имена и термины, которые Google переводит плохо. Перед переводом английский термин
/// заменяется русским — переводчик оставляет кириллицу как есть.
/// </summary>
public static class TermDictionary
{
    private static readonly Dictionary<string, string> BuiltIn = new()
    {
        ["Scions of the Seventh Dawn"] = "Сыны Седьмой Зари",
        ["the Scions"] = "Сыны",
        ["Scions"] = "Сыны",
        ["Warrior of Light"] = "Воин Света",
        ["Alphinaud"] = "Альфино",
        ["Alisaie"] = "Ализа",
        ["Y'shtola"] = "Й'штола",
        ["Thancred"] = "Танкред",
        ["Urianger"] = "Уриенжер",
        ["Minfilia"] = "Минфилия",
        ["Tataru"] = "Татару",
        ["Estinien"] = "Эстиниен",
        ["Haurchefant"] = "Оршфан",
        ["Aymeric"] = "Эмерик",
        ["Lyse"] = "Лиза",
        ["Hien"] = "Хиэн",
        ["Yugiri"] = "Югири",
        ["Gosetsu"] = "Госэцу",
        ["Raubahn"] = "Раубан",
        ["Zenos yae Galvus"] = "Зенос яэ Гальвус",
        ["Zenos"] = "Зенос",
        ["Ardbert"] = "Ардберт",
        ["Emet-Selch"] = "Эмет-Селх",
        ["Ishgard"] = "Ишгард",
        ["Ul'dah"] = "Ул'да",
        ["Limsa Lominsa"] = "Лимса Ломинса",
        ["Gridania"] = "Гридания",
        ["Eorzea"] = "Эорзея",
        ["Garlemald"] = "Гарлемальд",
        ["Garlean"] = "гарлеанский",
        ["Ala Mhigo"] = "Ала Миго",
        ["Doma"] = "Дома",
        ["Kugane"] = "Кугане",
        ["Rhalgr's Reach"] = "Предел Ралгра",
        ["Revenant's Toll"] = "Ревенантс-Толл",
        ["Crystarium"] = "Кристариум",
        ["Norvrandt"] = "Норврандт",
        ["the First"] = "Первый мир",
        ["primal"] = "праймал",
        ["primals"] = "праймалы",
        ["aether"] = "эфир",
        ["aetheryte"] = "этерит",
        ["Ascian"] = "Асциан",
        ["Ascians"] = "Асцианы",
        ["Sin Eater"] = "Пожиратель грехов",
        ["Sin Eaters"] = "Пожиратели грехов",
        ["Lightwarden"] = "Страж Света",
        ["Grand Company"] = "Великая компания",
        ["Immortal Flames"] = "Бессмертное Пламя",
        ["Twin Adder"] = "Двуглавый Змей",
        ["Maelstrom"] = "Мальстрём",
    };

    public static int BuiltInCount => BuiltIn.Count;

    public static string Apply(string text)
    {
        if (!Plugin.Config.UseTermDictionary) return text;
        // Длинные термины первыми, чтобы «Scions of the Seventh Dawn» не разбилось на «Scions»
        foreach (var (en, ru) in Plugin.Config.CustomTerms.Concat(BuiltIn).OrderByDescending(kv => kv.Key.Length))
            text = Regex.Replace(text, $@"(?<![\w']){Regex.Escape(en)}(?![\w'])", ru, RegexOptions.IgnoreCase);
        return text;
    }
}
