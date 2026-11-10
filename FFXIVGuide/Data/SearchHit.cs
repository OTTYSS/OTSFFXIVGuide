using System;

namespace FFXIVGuide.Data;

/// <summary>Результат общего поиска из какого-либо раздела плагина: по нажатию открывает раздел на этой записи.</summary>
public sealed record SearchHit(string Group, string Name, string Sub, uint Icon, Action Open);
