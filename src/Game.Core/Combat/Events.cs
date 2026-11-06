using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>Ce qui s'est passé dans la simulation. Le client ne fait que rejouer les événements.</summary>
public abstract record CombatEvent(int Tick);

/// <summary>
/// L'unité peut agir : sa jauge vient de se remplir, ou son étourdissement vient de finir jauge pleine.
/// Elle cesse d'être prête quand elle agit (<see cref="UnitMoved"/>, <see cref="SkillUsed"/>), est étourdie
/// (<see cref="StatusApplied"/>) ou tombe : ces événements suffisent, il n'y a pas d'événement « plus prête ».
/// </summary>
public sealed record UnitReady(int Tick, UnitId Unit) : CombatEvent(Tick);

/// <summary>L'unité s'est déplacée le long de <paramref name="Path"/> (départ exclu, arrivée incluse).</summary>
public sealed record UnitMoved(int Tick, UnitId Unit, Cell From, IReadOnlyList<Cell> Path) : CombatEvent(Tick)
{
    public Cell To => Path[^1];

    // Égalité par valeur sur le chemin (par défaut, un record compare la référence de la liste).
    public bool Equals(UnitMoved? other) =>
        other is not null && base.Equals(other) && Unit == other.Unit && From == other.From && Path.SequenceEqual(other.Path);

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Unit, From, Path.Count);
}

/// <summary>
/// La compétence prend effet maintenant (pour une attaque chargée : à son déclenchement), sur une unité
/// (<paramref name="Target"/>) ou sur une zone (<paramref name="Zone"/>). Les effets suivent dans d'autres événements.
/// </summary>
public sealed record SkillUsed(int Tick, UnitId Unit, string SkillId, UnitId? Target, IReadOnlyList<Cell>? Zone = null)
    : CombatEvent(Tick)
{
    public bool Equals(SkillUsed? other) =>
        other is not null && base.Equals(other) && Unit == other.Unit && SkillId == other.SkillId && Target == other.Target
        && (Zone is null ? other.Zone is null : other.Zone is not null && Zone.SequenceEqual(other.Zone));

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Unit, SkillId, Target, Zone?.Count);
}

/// <summary>
/// L'unité commence à charger une compétence, qui se déclenchera au tick <paramref name="ReleaseTick"/>, sur une unité
/// (<paramref name="Target"/>) ou sur une zone (<paramref name="Zone"/> : les cases fixées au sol, à afficher).
/// </summary>
public sealed record ChargeStarted(int Tick, UnitId Unit, string SkillId, UnitId? Target, IReadOnlyList<Cell>? Zone, int ReleaseTick)
    : CombatEvent(Tick)
{
    public bool Equals(ChargeStarted? other) =>
        other is not null && base.Equals(other) && Unit == other.Unit && SkillId == other.SkillId && Target == other.Target
        && ReleaseTick == other.ReleaseTick
        && (Zone is null ? other.Zone is null : other.Zone is not null && Zone.SequenceEqual(other.Zone));

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Unit, SkillId, Target, ReleaseTick, Zone?.Count);
}

/// <summary>
/// L'unité IA fixe (ou change, si la précédente est devenue impossible) son intention ; <c>null</c> : elle n'en a
/// plus (plus aucun ennemi). Le serveur la connaît toujours ; c'est l'affichage côté joueurs qui la cache sauf
/// révélation. Une intention exécutée, ou celle d'une unité vaincue, disparaît sans événement dédié.
/// </summary>
public sealed record IntentionChosen(int Tick, UnitId Unit, Intention? Intention) : CombatEvent(Tick);

/// <summary>La charge est interrompue (lanceur étourdi ou vaincu) : l'attaque n'a pas lieu.</summary>
public sealed record ChargeInterrupted(int Tick, UnitId Unit, string SkillId) : CombatEvent(Tick);

/// <summary>Attaque chargée ciblée déclenchée, mais la cible n'est plus à portée, plus en vue ou plus debout.</summary>
public sealed record ChargeMissed(int Tick, UnitId Unit, string SkillId, UnitId Target) : CombatEvent(Tick);

/// <summary>L'unité perd <paramref name="Amount"/> PV (dégâts tirés) ; il lui en reste <paramref name="Hp"/>.</summary>
public sealed record UnitDamaged(int Tick, UnitId Unit, int Amount, int Hp) : CombatEvent(Tick);

/// <summary>
/// L'unité reçoit un état, jusqu'au tick <paramref name="EndTick"/> (exclu). Émis aussi quand l'état était déjà là :
/// <paramref name="EndTick"/> donne alors la durée retenue (la plus longue des deux).
/// </summary>
public sealed record StatusApplied(int Tick, UnitId Unit, StatusKind Status, int EndTick) : CombatEvent(Tick);

/// <summary>L'état de l'unité prend fin.</summary>
public sealed record StatusEnded(int Tick, UnitId Unit, StatusKind Status) : CombatEvent(Tick);

/// <summary>
/// <paramref name="Attacker"/> frappe gratuitement <paramref name="Target"/>, qui quitte une case adjacente,
/// avec son attaque de base (les effets suivent dans d'autres événements).
/// Un déplacement interrompu est découpé : <see cref="UnitMoved"/> jusqu'à la case quittée, l'attaque, puis la suite.
/// </summary>
public sealed record OpportunityAttack(int Tick, UnitId Attacker, UnitId Target, string SkillId) : CombatEvent(Tick);

/// <summary>
/// Le déplacement de l'unité s'arrête en chemin (étourdie par une attaque d'opportunité) : elle est sur
/// <paramref name="Position"/>, la case atteinte, ou la dernière case libre du trajet si elle traversait un allié.
/// Son action est annulée.
/// </summary>
public sealed record UnitStopped(int Tick, UnitId Unit, Cell Position) : CombatEvent(Tick);

/// <summary>
/// Une unité apparaît en combat : <paramref name="Unit"/>, du type <paramref name="DefinitionId"/> (celui de la
/// compétence d'invocation), posée par <paramref name="Owner"/>, de son équipe <paramref name="Team"/>.
/// </summary>
public sealed record UnitSummoned(int Tick, UnitId Unit, string DefinitionId, UnitId Owner, int Team, Cell Position) : CombatEvent(Tick);

/// <summary>
/// <paramref name="By"/> met à l'abri l'allié à terre <paramref name="Unit"/> (Sauvetage éclair) ; les déplacements
/// suivent (<see cref="UnitDisplaced"/>).
/// </summary>
public sealed record UnitRescued(int Tick, UnitId Unit, UnitId By) : CombatEvent(Tick);

/// <summary><paramref name="By"/> relève l'allié à terre <paramref name="Unit"/> : il est debout avec <paramref name="Hp"/> PV, jauge vide.</summary>
public sealed record UnitRevived(int Tick, UnitId Unit, UnitId By, int Hp) : CombatEvent(Tick);

/// <summary>L'unité change de camp (dominée, ou fin de la domination) : elle est maintenant dans l'équipe <paramref name="Team"/>.</summary>
public sealed record TeamChanged(int Tick, UnitId Unit, int Team) : CombatEvent(Tick);

/// <summary>Un robot pris dans une zone mentale y résiste : elle n'a aucun effet sur lui.</summary>
public sealed record UnitResisted(int Tick, UnitId Unit) : CombatEvent(Tick);

/// <summary>L'unité regagne <paramref name="Amount"/> PV ; elle en a maintenant <paramref name="Hp"/>.</summary>
public sealed record UnitHealed(int Tick, UnitId Unit, int Amount, int Hp) : CombatEvent(Tick);

/// <summary>Le dôme de <paramref name="Owner"/> se lève sur ces cases.</summary>
public sealed record DomeRaised(int Tick, UnitId Owner, IReadOnlyList<Cell> Cells) : CombatEvent(Tick)
{
    public bool Equals(DomeRaised? other) =>
        other is not null && base.Equals(other) && Owner == other.Owner && Cells.SequenceEqual(other.Cells);

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Owner, Cells.Count);
}

/// <summary>Le dôme de <paramref name="Owner"/> bloque un coup sur <paramref name="Unit"/> : ce coup n'a aucun effet sur elle.</summary>
public sealed record HitBlocked(int Tick, UnitId Unit, UnitId Owner) : CombatEvent(Tick);

/// <summary>Le dôme de <paramref name="Owner"/> disparaît (il a absorbé une attaque, son lanceur a agi ou est tombé).</summary>
public sealed record DomeEnded(int Tick, UnitId Owner) : CombatEvent(Tick);

/// <summary>
/// L'unité est à couvert derrière l'obstacle bas <paramref name="Cover"/> : il absorbe <paramref name="Absorbed"/> dégâts
/// du coup (les dégâts restants suivent) et n'a plus que <paramref name="CoverHp"/> PV.
/// </summary>
public sealed record HitCovered(int Tick, UnitId Unit, Cell Cover, int Absorbed, int CoverHp) : CombatEvent(Tick);

/// <summary>L'obstacle bas <paramref name="Cell"/> est détruit : c'est désormais du sol.</summary>
public sealed record CoverDestroyed(int Tick, Cell Cell) : CombatEvent(Tick);

/// <summary>L'unité est achevée par une exécution (ses PV restants suivent en dégâts).</summary>
public sealed record UnitExecuted(int Tick, UnitId Attacker, UnitId Target) : CombatEvent(Tick);

/// <summary>
/// L'unité est déplacée de force (projection : en ligne droite ; télékinésie : posée directement), sans attaque
/// d'opportunité.
/// </summary>
public sealed record UnitDisplaced(int Tick, UnitId Unit, Cell From, Cell To) : CombatEvent(Tick);

/// <summary>
/// L'unité projetée percute un obstacle : une autre unité (<paramref name="With"/>), ou un mur ou le bord (<c>null</c>).
/// Les dégâts de collision suivent.
/// </summary>
public sealed record UnitCollided(int Tick, UnitId Unit, UnitId? With) : CombatEvent(Tick);

/// <summary>La jauge de l'unité gagne <paramref name="Amount"/> points d'un coup (ex. Ordre tactique) et vaut <paramref name="Gauge"/>.</summary>
public sealed record GaugeGained(int Tick, UnitId Unit, int Amount, int Gauge) : CombatEvent(Tick);

/// <summary>
/// L'unité tombe à terre sur <paramref name="Position"/> : elle y reste, sa jauge est gelée.
/// C'est en général sa case actuelle ; si elle tombe en traversant un allié, la dernière case libre de son trajet.
/// </summary>
public sealed record UnitDowned(int Tick, UnitId Unit, Cell Position) : CombatEvent(Tick);

/// <summary>L'unité est retirée de la grille : vaincue, remplacée (gadget) ou épuisée (activations).</summary>
public sealed record UnitRemoved(int Tick, UnitId Unit) : CombatEvent(Tick);

/// <summary>L'unité passe sous son seuil de fuite : elle ne fait plus que courir vers le bord de carte.</summary>
public sealed record UnitFleeing(int Tick, UnitId Unit) : CombatEvent(Tick);

/// <summary>L'unité en fuite atteint le bord de carte et quitte la grille : enfuie, pas vaincue.</summary>
public sealed record UnitEscaped(int Tick, UnitId Unit) : CombatEvent(Tick);

/// <summary>Commande refusée : rien n'a changé, la jauge de l'unité reste pleine.</summary>
public sealed record CommandRejected(int Tick, CombatCommand Command, RejectionReason Reason) : CombatEvent(Tick);

/// <summary>Ordre à l'avance enregistré (il remplace le précédent de l'unité) ; il partira dès que l'unité pourra agir.</summary>
public sealed record OrderGiven(int Tick, UnitId Unit, ActivateCommand Activation) : CombatEvent(Tick);

/// <summary>Ordre à l'avance annulé par le joueur.</summary>
public sealed record OrderCancelled(int Tick, UnitId Unit) : CombatEvent(Tick);

/// <summary>
/// Ordre à l'avance parti : l'activation est appliquée juste après (ses propres événements suivent). S'il était devenu
/// impossible, c'est un <see cref="CommandRejected"/> à la place, et l'ordre est retiré.
/// </summary>
public sealed record OrderExecuted(int Tick, UnitId Unit) : CombatEvent(Tick);

public enum RejectionReason
{
    UnknownUnit,

    /// <summary>L'unité est à terre ou retirée.</summary>
    Incapacitated,

    /// <summary>L'unité est commandée par l'IA : un joueur ne peut pas lui donner d'ordre.</summary>
    NotControllable,

    /// <summary>L'unité est étourdie : elle ne peut pas agir, même jauge pleine.</summary>
    Stunned,

    /// <summary>L'unité charge une attaque : elle est immobilisée jusqu'au déclenchement.</summary>
    Charging,

    NotReady,

    /// <summary>Ni déplacement ni action.</summary>
    Empty,

    /// <summary>Une compétence et une interaction dans la même activation : une seule action à la fois.</summary>
    TwoActions,

    /// <summary>
    /// Déplacement vers la case où l'unité se trouve déjà, ou case de télékinésie impossible (sa propre case,
    /// trop loin, occupée, mur, ou pas en vue depuis la cible).
    /// </summary>
    InvalidDestination,

    /// <summary>Case d'arrivée bloquée, occupée, inaccessible ou hors de portée de la mobilité.</summary>
    Unreachable,

    /// <summary>L'unité ne possède pas cette compétence.</summary>
    UnknownSkill,

    /// <summary>Compétence en recharge.</summary>
    OnCooldown,

    /// <summary>
    /// Cible inconnue, alliée, l'unité elle-même, à terre ou retirée ; ou cible du mauvais type (unité pour une
    /// compétence à zone, case pour une attaque ciblée) ; ou case non alignée pour une zone en ligne ; ou, pour
    /// relever, autre chose qu'un allié à terre.
    /// </summary>
    InvalidTarget,

    /// <summary>La compétence n'aurait aucun effet sur cette cible (ex. Ordre tactique sur un allié déjà prêt ou qui charge).</summary>
    NoEffect,

    /// <summary>Cible trop loin depuis la case d'arrivée.</summary>
    OutOfRange,

    /// <summary>
    /// Cible à portée mais cachée depuis la case d'arrivée : par un mur ou un ennemi debout, ou, au contact
    /// en diagonale, par un mur sur un coin.
    /// </summary>
    NoLineOfSight,
}

public static class RejectionReasons
{
    /// <summary>
    /// L'unité ne peut pas agir maintenant, mais il suffit d'attendre (jauge pas pleine, étourdie, en charge, compétence
    /// en recharge) : un ordre donné à l'avance reste en attente au lieu d'être annulé.
    /// </summary>
    public static bool OnlyNeedsTime(this RejectionReason reason) =>
        reason is RejectionReason.NotReady or RejectionReason.Stunned or RejectionReason.Charging or RejectionReason.OnCooldown;
}

/// <summary>Commande telle qu'appliquée, avec son tick : de quoi rejouer le combat à l'identique.</summary>
public sealed record LoggedCommand(int Tick, CombatCommand Command);
