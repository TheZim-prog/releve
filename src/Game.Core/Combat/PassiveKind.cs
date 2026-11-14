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

    /// <summary>Voleur de temps (speedster) : ses coups retirent un peu de jauge à la cible (<see cref="CombatRules.TimeThiefGaugeLossPercent"/>).</summary>
    TimeThief,

    /// <summary>Sur sa lancée (colosse) : mettre un ennemi hors de combat lui rend de la jauge (<see cref="CombatRules.MomentumGaugeGainPercent"/>).</summary>
    Momentum,

    /// <summary>Récupération d'énergie (tech) : un peu de jauge pour chaque ennemi qu'il étourdit (<see cref="CombatRules.EnergyRecoveryGaugeGainPercent"/>).</summary>
    EnergyRecovery,

    /// <summary>Sillage (speedster) : après son action, les alliés à son contact gagnent un peu de jauge (<see cref="CombatRules.SlipstreamGaugeGainPercent"/>).</summary>
    Slipstream,

    /// <summary>Commandement (tech) : les alliés proches remplissent leur jauge plus vite (<see cref="CombatRules.CommandFillPercent"/>).</summary>
    Command,

    /// <summary>Champ de brouillage (tech) : les ennemis proches remplissent leur jauge plus lentement (<see cref="CombatRules.JammingFieldFillPercent"/>).</summary>
    JammingField,
}

/// <summary>Identifiants des passifs dans les données : le nom du <see cref="PassiveKind"/> en camelCase (« weakPoint »).</summary>
public static class Passives
{
    private static readonly IReadOnlyDictionary<string, PassiveKind> ById =
        Enum.GetValues<PassiveKind>().ToDictionary(kind => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString()));

    /// <summary>Le passif codé qui porte cet identifiant, s'il existe.</summary>
    public static bool TryParse(string id, out PassiveKind kind) => ById.TryGetValue(id, out kind);
}
