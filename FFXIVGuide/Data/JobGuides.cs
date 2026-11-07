using System.Collections.Generic;

namespace FFXIVGuide.Data;

/// <summary>Раздел гайда: заголовок и пункты.</summary>
public sealed record GuideSection(string Title, string[] Points);

/// <summary>
/// Короткие гайды по ротации. Для хилов — подробно по профессиям, для остальных — общие принципы роли.
/// Названия навыков на английском, как в клиенте игры.
/// </summary>
public static class JobGuides
{
    private static readonly Dictionary<string, GuideSection[]> ByJob = new()
    {
        ["WHM"] = new[]
        {
            new GuideSection("Урон (это основная работа, когда группа сыта)", new[]
            {
                "Держи DoT Dia (Aero на низких уровнях) на боссе — обновляй, когда осталось ~3 секунды.",
                "Остальное время — Glare III (Stone на низких уровнях).",
                "Assize — на откате: урон по врагам и хил группе одновременно.",
                "Presence of Mind — в начале боя и в окна рейдовых баффов.",
                "Afflatus Misery (после 3 лилий) — сильный урон по области, не держи кровавую лилию заполненной.",
            }),
            new GuideSection("Хил без потери урона (oGCD — между кастами)", new[]
            {
                "Танку: Divine Benison (щит), Aquaveil (−15% урона), Tetragrammaton (мгновенный хил).",
                "Benediction — полный хил, спасает танка в критический момент.",
                "Лилии (Afflatus Solace / Rapture) — бесплатный хил, тратить вместо Cure II/Medica.",
                "Группе: Asylum (зона хила на земле), Liturgy of the Bell, Plenary Indulgence.",
                "Temperance — +20% хил и −10% урона группе: под тяжёлые серии ударов.",
            }),
            new GuideSection("Порядок выбора хила", new[]
            {
                "1) Лилии и oGCD → 2) Regen/Medica II → 3) Cure II / Cure III — только если ничего не осталось.",
                "Cure (I) почти не нужен после получения Cure II; Freecure даёт бесплатный Cure II.",
                "Thin Air — бесплатный следующий каст: под Raise или Cure III.",
            }),
        },
        ["SCH"] = new[]
        {
            new GuideSection("Урон", new[]
            {
                "Держи Biolysis (Bio) на боссе, в остальное время — Broil.",
                "Energy Drain — сливай Aetherflow перед новым откатом Aetherflow.",
                "Chain Stratagem — в окна рейдовых баффов.",
            }),
            new GuideSection("Хил", new[]
            {
                "Фея лечит сама: Whispering Dawn, Fey Illumination, Fey Blessing — первые инструменты.",
                "Lustrate, Indomitability, Excogitation — тратят Aetherflow, это основа хила.",
                "Sacred Soil — зона −10% урона, ставь под групповой урон.",
                "Adloquium — щит перед танкбастером; Deployment Tactics раскидает его на всех.",
            }),
        },
        ["AST"] = new[]
        {
            new GuideSection("Урон", new[]
            {
                "Держи Combust на боссе, в остальное время — Malefic.",
                "Карты (Draw/Play) раздавай союзникам; Divination — в окна рейдовых баффов.",
                "Lord of Crowns и Earthly Star (детонация) — дополнительный урон.",
            }),
            new GuideSection("Хил", new[]
            {
                "Essential Dignity — сильнее на низком HP, спасает танка.",
                "Celestial Intersection и Exaltation — щит/митигация на танка.",
                "Earthly Star ставь заранее — через 10 секунд он сильнее.",
                "Horoscope, Neutral Sect, Macrocosmos — под тяжёлый групповой урон.",
            }),
        },
        ["SGE"] = new[]
        {
            new GuideSection("Урон", new[]
            {
                "Держи Eukrasian Dosis на боссе, в остальное время — Dosis.",
                "Phlegma — мгновенный урон с зарядами; Toxikon при движении.",
                "Kardia на танке — урон по врагу лечит его.",
            }),
            new GuideSection("Хил", new[]
            {
                "Addersgall: Druochole, Taurochole (танку), Ixochole, Kerachole (группе).",
                "Physis II, Holos, Panhaima — под тяжёлый групповой урон.",
                "Haima — щиты танку на серию ударов; Krasis усиливает хил по цели.",
                "Eukrasian Prognosis — групповой щит перед уроном.",
            }),
        },
    };

    private static readonly Dictionary<int, GuideSection[]> ByRole = new()
    {
        [1] = new[]
        {
            new GuideSection("Танк: главное", new[]
            {
                "Держи танковую стойку (Iron Will, Defiance, Grit, Royal Guard) включённой.",
                "Комбо из 3 ударов — основа урона; не прерывай его.",
                "На паках: AoE-комбо, митигация по очереди (сначала мелкая, потом большая).",
                "Танкбастер — заранее митигация; Rampart и личные кулдауны чередуй.",
                "Разворачивай босса от группы, держи его на месте.",
            }),
        },
        [2] = new[]
        {
            new GuideSection("Ближний бой: главное", new[]
            {
                "Комбо-удары — основа урона; соблюдай позиционки (спина/бок), если они есть.",
                "Баффы урона используй вместе с рейдовыми баффами группы.",
                "Не стой в AoE: потеря нескольких ударов дешевле, чем смерть.",
            }),
        },
        [3] = new[]
        {
            new GuideSection("Дальний бой / маги: главное", new[]
            {
                "Не прерывай каст лишним движением — заранее вставай в безопасное место.",
                "Держи DoT-ы и главные кулдауны в окнах рейдовых баффов.",
                "На паках — AoE-навыки с 3 целей.",
            }),
        },
    };

    public static GuideSection[] For(string jobAbbr, string parentAbbr, int role) =>
        ByJob.TryGetValue(jobAbbr, out var g) ? g
        : ByJob.TryGetValue(parentAbbr, out var p) ? p
        : jobAbbr == "CNJ" ? ByJob["WHM"]
        : ByRole.TryGetValue(role, out var r) ? r
        : System.Array.Empty<GuideSection>();
}
