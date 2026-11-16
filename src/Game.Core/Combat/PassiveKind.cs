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

    /// <summary>Rage (colosse) : plus ses PV sont bas, plus il frappe fort (<see cref="CombatRules.RageMaxDamageBonusPercent"/>).</summary>
    Rage,

    /// <summary>Opportuniste (combattant) : plus de dégâts contre une cible étourdie, déséquilibrée ou immobilisée (<see cref="CombatRules.OpportunistDamagePercent"/>).</summary>
    Opportunist,

    /// <summary>Premier sang (combattant) : plus de dégâts contre un ennemi dont les PV sont intacts (<see cref="CombatRules.FirstBloodDamagePercent"/>).</summary>
    FirstBlood,

    /// <summary>Seul contre tous (combattant) : moins de dégâts subis quand plusieurs ennemis sont à son contact (<see cref="CombatRules.AloneAgainstAllDamageTakenPercent"/>).</summary>
    AloneAgainstAll,

    /// <summary>Perforant (énergétique) : ses attaques ignorent la couverture.</summary>
    Piercing,

    /// <summary>Écran cinétique (télépathe) : moins de dégâts subis des coups venus de loin (<see cref="CombatRules.KineticScreenDamageTakenPercent"/>).</summary>
    KineticScreen,

    /// <summary>Embusqué (combattant) : collé à un mur ou à un obstacle, ses coups de loin frappent plus fort (<see cref="CombatRules.AmbusherDamagePercent"/>).</summary>
    Ambusher,

    /// <summary>Fournaise (énergétique) : après chacune de ses actions, les ennemis à son contact prennent de petits dégâts (<see cref="CombatRules.FurnaceMinDamage"/>).</summary>
    Furnace,

    /// <summary>Réaction en chaîne (énergétique) : un ennemi qu'il met hors de combat explose et blesse ses voisins (<see cref="CombatRules.ChainReactionMinDamage"/>).</summary>
    ChainReaction,

    /// <summary>Givre (énergétique) : son attaque de base ralentit brièvement la cible (<see cref="CombatRules.FrostDurationMs"/>).</summary>
    Frost,

    /// <summary>Carapace de givre (énergétique) : un ennemi qui le frappe au contact est ralenti (<see cref="CombatRules.FrostShellDurationMs"/>).</summary>
    FrostShell,

    /// <summary>Garde du corps : il prend à la place d'un allié adjacent une attaque ciblée, une fois par cycle.</summary>
    Bodyguard,

    /// <summary>Réflexes : il esquive la première attaque ciblée qui le vise à chaque cycle (pas les zones).</summary>
    Reflexes,

    /// <summary>Riposte : attaqué au contact, il contre-attaque avec son attaque de base, une fois par cycle.</summary>
    Riposte,

    /// <summary>Coup d'arrêt : ses attaques d'opportunité qui touchent arrêtent net le déplacement de la cible.</summary>
    StoppingBlow,

    /// <summary>Surcadençage (tech) : ses gadgets remplissent leur jauge plus vite (<see cref="CombatRules.OverclockingFillPercent"/>), tant qu'il est debout.</summary>
    Overclocking,

    /// <summary>Double déploiement (tech) : il peut avoir plusieurs gadgets du même type à la fois (<see cref="CombatRules.DoubleDeploymentCount"/>).</summary>
    DoubleDeployment,

    /// <summary>
    /// Inébranlable (colosse) : un adversaire ne peut ni le déplacer de force, ni le déséquilibrer (un allié peut encore
    /// le lancer ou le déplacer).
    /// </summary>
    Unshakable,

    /// <summary>
    /// Départ canon (speedster) : il commence chaque combat avec la jauge pleine (posée par le scénario, voir
    /// <see cref="Scenarios.ScenarioDefinition.ToSetup"/>).
    /// </summary>
    FlyingStart,

    /// <summary>Diversion (speedster) : les adversaires à son contact ne font plus d'attaques d'opportunité contre ses alliés.</summary>
    Diversion,

    /// <summary>
    /// Analyse tactique (tech) : les intentions des adversaires proches de lui sont révélées à son camp
    /// (<see cref="CombatRules.TacticalAnalysisRange"/>).
    /// </summary>
    TacticalAnalysis,

    /// <summary>
    /// Démesure (énergétique) : ses coups font plus de dégâts à ses adversaires (<see cref="CombatRules.ExcessDamagePercent"/>),
    /// mais l'obstacle qui couvre sa cible s'use plus vite (<see cref="CombatRules.ExcessCoverWearPercent"/>).
    /// </summary>
    Excess,

    /// <summary>
    /// Contagion (télépathe) : un adversaire qu'il a étourdi ou rendu confus, qui tombe, passe cet état (durée restante)
    /// à l'adversaire le plus proche.
    /// </summary>
    Contagion,

    /// <summary>Impact (télépathe) : un ennemi qu'il déplace de force prend des dégâts à l'arrivée (<see cref="CombatRules.ImpactMinDamage"/>).</summary>
    Impact,

    /// <summary>Présence (télépathe) : après chacune de ses actions, ses alliés proches récupèrent un peu de PV (<see cref="CombatRules.PresenceRange"/>).</summary>
    Presence,

    /// <summary>
    /// Ange gardien (télépathe) : une fois par combat, un allié proche qui devrait tomber reste à 1 PV
    /// (<see cref="CombatRules.GuardianAngelRange"/>).
    /// </summary>
    GuardianAngel,
}

/// <summary>Identifiants des passifs dans les données : le nom du <see cref="PassiveKind"/> en camelCase (« weakPoint »).</summary>
public static class Passives
{
    private static readonly IReadOnlyDictionary<string, PassiveKind> ById =
        Enum.GetValues<PassiveKind>().ToDictionary(kind => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString()));

    /// <summary>Le passif codé qui porte cet identifiant, s'il existe.</summary>
    public static bool TryParse(string id, out PassiveKind kind) => ById.TryGetValue(id, out kind);
}
