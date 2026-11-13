using System.Text.Json;

namespace Game.Core.Combat;

/// <summary>
/// Passifs codés (docs/design.md 2.7, arbres de compétences). Un héros en équipe un seul. Ceux que les arbres prévoient
/// mais qui ne sont pas encore codés n'apparaissent pas ici : ils sont marqués « à venir » dans data/trees.
/// </summary>
public enum PassiveKind
{
    /// <summary>Insaisissable (speedster) : l'unité ne subit pas d'attaques d'opportunité.</summary>
    IgnoresOpportunityAttacks,

    /// <summary>
    /// Point faible (combattant) : chaque coup qui fait des dégâts rend la cible vulnérable
    /// (<see cref="StatusKind.Exposed"/>, durée <see cref="CombatRules.WeakPointExposedMs"/>).
    /// </summary>
    WeakPoint,

    /// <summary>Emprise (télépathe) : ses états néfastes durent plus longtemps (<see cref="CombatRules.GripDurationPercent"/>).</summary>
    Grip,

    /// <summary>
    /// Vigilance (colosse) : des attaques d'opportunité en plus à chaque cycle
    /// (<see cref="CombatRules.VigilanceExtraOpportunityAttacks"/>).
    /// </summary>
    Vigilance,
}

/// <summary>Identifiants des passifs dans les données : le nom du <see cref="PassiveKind"/> en camelCase (« weakPoint »).</summary>
public static class Passives
{
    private static readonly IReadOnlyDictionary<string, PassiveKind> ById =
        Enum.GetValues<PassiveKind>().ToDictionary(kind => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString()));

    /// <summary>Le passif codé qui porte cet identifiant, s'il existe.</summary>
    public static bool TryParse(string id, out PassiveKind kind) => ById.TryGetValue(id, out kind);
}
