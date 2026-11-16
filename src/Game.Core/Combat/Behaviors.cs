namespace Game.Core.Combat;

/// <summary>Comportements d'IA connus, désignés par leur identifiant dans les données.</summary>
public static class Behaviors
{
    /// <summary>
    /// Comportement de base (docs/design.md) : vise l'ennemi le plus fragile qu'il peut frapper à son prochain tour,
    /// avec la compétence la plus forte qui l'atteint ; sinon se rapproche de l'ennemi le plus proche à pied.
    /// Évite de prendre ses alliés dans ses zones. N'utilise pas encore les zones en ligne ni le soutien.
    /// </summary>
    public const string Basic = "basic";

    /// <summary>
    /// Balise (gadget immobile) : jauge pleine, elle lance sa zone autour d'elle, seulement si elle sert (un allié
    /// à soigner ou à protéger ; un adversaire à frapper, pour l'Astre) ; sinon elle attend.
    /// </summary>
    public const string Beacon = "beacon";

    /// <summary>
    /// Tireur : comme le comportement de base, mais tire depuis la case atteignable la plus éloignée de l'ennemi le
    /// plus proche (il recule si on l'approche), et utilise aussi ses zones en ligne (Tir appuyé).
    /// </summary>
    public const string Gunner = "gunner";

    /// <summary>Inerte (un leurre) : n'agit jamais et ne bouge pas.</summary>
    public const string Inert = "inert";

    internal static readonly IUnitBehavior BasicInstance = new BasicBehavior(BasicBehavior.Mode.Basic);
    private static readonly IUnitBehavior BeaconInstance = new BeaconBehavior();
    private static readonly IUnitBehavior GunnerInstance = new BasicBehavior(BasicBehavior.Mode.Gunner);
    private static readonly IUnitBehavior InertInstance = new InertBehavior();

    /// <summary>Unité confuse (état, pas une donnée) : attaque l'unité debout la plus proche, quel que soit son camp.</summary>
    internal static readonly IUnitBehavior ConfusedInstance = new BasicBehavior(BasicBehavior.Mode.Confused);

    /// <summary>
    /// Fuite : ne fait que courir vers le bord de carte le plus proche, et quitte le combat en l'atteignant. Celui des
    /// civils, et de toute unité passée sous son seuil de fuite.
    /// </summary>
    public const string Flee = "flee";

    internal static readonly IUnitBehavior FleeInstance = new FleeBehavior();

    public static bool IsKnown(string id) => Find(id) is not null;

    internal static IUnitBehavior? Find(string id) => id switch
    {
        Basic => BasicInstance,
        Beacon => BeaconInstance,
        Gunner => GunnerInstance,
        Inert => InertInstance,
        Flee => FleeInstance,
        _ => null,
    };
}
