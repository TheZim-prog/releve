using Game.Core.Grids;

namespace Game.Core.Combat;

// Visée : ce qu'une compétence ou une interaction peut viser depuis une case (validation et prévisualisation).
public sealed partial class CombatSimulation
{
    /// <summary>
    /// Prévisualisation d'une action de <paramref name="unitId"/> lancée depuis <paramref name="from"/> (sa case, ou celle
    /// du fantôme) : ce qu'elle viserait (unité, cases de la zone ou de la course, case d'arrivée ou de dépôt), ou la
    /// raison du refus. Ne regarde ni la jauge ni la recharge (voir <see cref="Check"/> pour la commande entière).
    /// </summary>
    public ActionPreview PreviewAction(UnitId unitId, Cell from, SkillUse use)
    {
        ArgumentNullException.ThrowIfNull(use);
        if (FindUnit(unitId) is not { } unit)
            return new ActionPreview(RejectionReason.UnknownUnit);
        if (unit.FindSkill(use.SkillId) is not { } skill)
            return new ActionPreview(RejectionReason.UnknownSkill);
        var (action, reason) = PlanAction(unit, from, skill, use);
        return action is null
            ? new ActionPreview(reason)
            : new ActionPreview(null, action.Target?.Id, action.Zone, action.Destination);
    }

    /// <summary>
    /// Action validée : sur une unité (<paramref name="Target"/>) ou sur des cases (<paramref name="Zone"/>) ;
    /// <paramref name="Destination"/> : case où la télékinésie pose sa cible ; <paramref name="Origin"/> : d'où part une
    /// zone ordinaire (centre d'un carré, lanceur pour une ligne), pour la couverture.
    /// </summary>
    internal sealed record PlannedAction(SkillDefinition Skill, Unit? Target, IReadOnlyList<Cell>? Zone, Cell? Destination = null,
        Cell? Origin = null);

    /// <summary>
    /// Valide la cible d'une compétence utilisée depuis <paramref name="position"/> (case d'arrivée du lanceur).
    /// Compétence à zone : une case, à portée (0 = sur soi), libre et en vue pour un carré ou un mur, alignée pour une ligne,
    /// et une zone non vide. Attaque ciblée : voir <see cref="CheckTarget"/>.
    /// </summary>
    internal (PlannedAction? Action, RejectionReason? Reason) PlanAction(Unit unit, Cell position, SkillDefinition skill, SkillUse use)
    {
        if (skill.SelfMove is { } selfMove)
            return PlanSelfMove(unit, position, skill, use, selfMove);

        if (skill.IsRescue)
            return PlanRescue(unit, position, skill, use);

        if (skill.Summon is not null)
        {
            // Une case libre (une fois le lanceur arrivé), à portée et en vue.
            if (use.Cell is not { } spot || use.Target is not null)
                return (null, RejectionReason.InvalidTarget);
            if (!IsWithinRange(position, spot, skill.Range))
                return (null, RejectionReason.OutOfRange);
            // Libre, ou occupée par le lanceur (il en part) ou par son ancien gadget du même type (il sera remplacé).
            if (!_grid.IsWalkable(spot)
                || (UnitAt(spot) is { } occupant && occupant != unit
                    && !(occupant.SummonedBy == unit.Id && occupant.Definition.Id == skill.Summon.Id)))
                return (null, RejectionReason.InvalidDestination);
            if (!HasLineOfSight(unit, position, spot))
                return (null, RejectionReason.NoLineOfSight);
            return (new PlannedAction(skill, null, null, spot), null);
        }

        if (skill.Zone is { } zone)
        {
            if (use.Cell is not { } cell || use.Target is not null)
                return (null, RejectionReason.InvalidTarget);
            if (position.DistanceTo(cell) > skill.Range)
                return (null, RejectionReason.OutOfRange);
            var cells = zone.Cells(_grid, position, cell);
            if (cells is null)
                return (null, RejectionReason.InvalidTarget);
            cells = StoppedByGround(skill, position, cells);
            if (cells.Count == 0)
                return (null, RejectionReason.InvalidTarget);
            if (zone.Shape != ZoneShape.Line && cell != position && !HasLineOfSight(unit, position, cell, skill))
                return (null, RejectionReason.NoLineOfSight);
            // Un piège se pose sur une case sans unité ; un obstacle se dresse sur les cases libres, dont la case visée.
            if (skill.Ground is { IsTrap: true } && !CanRaiseOn(unit, position, cell))
                return (null, RejectionReason.InvalidDestination);
            if (skill.Raises is not null)
            {
                if (!CanRaiseOn(unit, position, cell))
                    return (null, RejectionReason.InvalidDestination);
                cells = cells.Where(c => CanRaiseOn(unit, position, c)).ToList();
            }
            return (new PlannedAction(skill, null, cells, Origin: zone.Shape == ZoneShape.Square ? cell : position), null);
        }

        if (skill.Target == SkillTargeting.Self)
        {
            // Pas de cible à donner (ou soi-même).
            if (use.Cell is not null || (use.Target is { } self && self != unit.Id))
                return (null, RejectionReason.InvalidTarget);
            return (new PlannedAction(skill, unit, null), null);
        }

        // Une unité, plus une case de destination pour une télékinésie (et seulement pour elle).
        var places = skill.ForcedMove?.Type == ForcedMoveType.Place;
        if (use.Target is not { } targetId || (use.Cell is not null) != places)
            return (null, RejectionReason.InvalidTarget);
        var target = FindUnit(targetId);
        if (target is null || !IsRightSide(unit, target, skill))
            return (null, RejectionReason.InvalidTarget);
        // Remplir une jauge déjà pleine, ou bloquée à 0 par une charge, ne servirait à rien.
        if (!skill.IsOffensive && !HasSupportEffect(skill, target))
            return (null, RejectionReason.NoEffect);
        if (IsImmune(skill, target))
            return (null, RejectionReason.NoEffect);
        // Un gadget ne se lance pas et ne change pas de place : le déplacement d'un allié vaut pour un équipier.
        if (skill.Target == SkillTargeting.Ally && skill.ForcedMove is not null && target.SummonedBy is not null)
            return (null, RejectionReason.InvalidTarget);
        // On ne domine pas un allié (une unité confuse pourrait le viser).
        if (target.Team == unit.Team && skill.Effects.Any(e => e.Status == StatusKind.Dominated))
            return (null, RejectionReason.InvalidTarget);
        if (CheckTarget(unit, position, target, skill) is { } reason)
            return (null, reason);
        if (places && !CanPlace(unit, position, target, use.Cell!.Value, skill.ForcedMove!.Distance))
            return (null, RejectionReason.InvalidDestination);
        return (new PlannedAction(skill, target, null, use.Cell), null);
    }

    /// <summary>
    /// Immunités : les robots aux pouvoirs mentaux, les boss à la domination. Une cible directe immunisée est refusée ;
    /// dans une zone, elle résiste. (Un civil ne peut pas être visé, et une domination ne se fait jamais en zone.)
    /// </summary>
    private static bool IsImmune(SkillDefinition skill, Unit target) =>
        (skill.IsMental && target.Definition.IsRobot)
        || (target.Definition.IsBoss && skill.Effects.Any(e => e.Status == StatusKind.Dominated));

    /// <summary>
    /// Une compétence de soutien aurait-elle un effet sur cette cible ? Un état ou un déplacement en a toujours ;
    /// des PV temporaires aussi ; un soin, si la cible est blessée ; un gain de jauge, si sa jauge n'est ni pleine ni bloquée par une charge.
    /// </summary>
    private static bool HasSupportEffect(SkillDefinition skill, Unit target) =>
        skill.Effects.Count > 0
        || skill.ForcedMove is not null
        || (skill.Heals && target.Hp < target.MaxHp)
        || skill.TempHp is not null
        || (skill.GaugeGainPercent > 0 && !target.IsGaugeFull && target.Charge is null);

    /// <summary>
    /// Une attaque vise un ennemi ; une compétence de soutien, un allié autre que le lanceur ; une télékinésie,
    /// n'importe quelle unité sauf le lanceur. Personne ne vise un civil directement (le sauvetage a sa propre règle).
    /// </summary>
    private static bool IsRightSide(Unit caster, Unit target, SkillDefinition skill) => !target.IsCivilian && skill.Target switch
    {
        // Une unité confuse frappe n'importe qui (sauf elle-même).
        SkillTargeting.Enemy => target.Team != caster.Team || (caster.HasStatus(StatusKind.Confused) && target != caster),
        SkillTargeting.Ally => target != caster && target.Team == caster.Team,
        SkillTargeting.Any => target != caster,
        _ => throw new InvalidOperationException($"Unknown target kind {skill.Target}."),
    };

    /// <summary>
    /// Télékinésie : case autre que celle de la cible, à <paramref name="distance"/> au plus d'elle, vue depuis elle
    /// (ce qui exclut aussi les murs), et libre une fois le lanceur arrivé en <paramref name="casterPosition"/>
    /// (il a pu quitter sa case, ou arriver sur celle visée, dans la même commande).
    /// </summary>
    private bool CanPlace(Unit caster, Cell casterPosition, Unit target, Cell destination, int distance) =>
        destination != target.Position
        && destination != casterPosition
        && target.Position.DistanceTo(destination) <= distance
        && IsFreeFor(caster, destination)
        && LineOfSight.IsClear(_grid, target.Position, destination);

    /// <summary>
    /// Une attaque ciblée de <paramref name="unit"/>, depuis <paramref name="from"/>, peut-elle toucher
    /// <paramref name="target"/> : debout, à portée et en vue ? Vérifié au lancement et, pour une attaque chargée,
    /// de nouveau au déclenchement.
    /// </summary>
    private RejectionReason? CheckTarget(Unit unit, Cell from, Unit target, SkillDefinition skill)
    {
        if (target.Status != UnitStatus.Active)
            return RejectionReason.InvalidTarget;
        if (!IsWithinRange(from, target.Position, skill.Range))
            return RejectionReason.OutOfRange;
        if (!HasLineOfSight(unit, from, target.Position, skill))
            return RejectionReason.NoLineOfSight;
        return null;
    }

    /// <summary>
    /// Sauvetage, depuis <paramref name="position"/> : un allié <b>à terre</b> (ou un civil debout ; pour un transport,
    /// un allié <b>debout</b>, pas un gadget) à <see cref="SkillDefinition.Range"/> pas au
    /// plus, à pied (chemin réel jusqu'à son contact, sans coupe d'angle) ; une case de dépôt libre, autre que celle du
    /// lanceur, au plus à <see cref="SkillDefinition.RescueDropRadius"/> et en vue ; et une case d'arrivée pour le lanceur
    /// (<see cref="RescuerEnd"/>).
    /// </summary>
    private (PlannedAction? Action, RejectionReason? Reason) PlanRescue(Unit unit, Cell position, SkillDefinition skill, SkillUse use)
    {
        // Un allié à terre, ou un civil (debout : à 0 PV, il est perdu) ; pour un transport, un allié debout.
        if (use.Target is not { } allyId || use.Cell is not { } drop || FindUnit(allyId) is not { } ally || ally == unit
            || !(skill.CarriesStandingAlly ? ally.IsStandingCombatant && ally.Team == unit.Team
                : ally.IsCivilian ? ally.Status == UnitStatus.Active
                : ally.Team == unit.Team && ally.Status == UnitStatus.Downed))
            return (null, RejectionReason.InvalidTarget);

        // Range pas jusqu'à l'allié : Range - 1 jusqu'à une case à son contact, puis le dernier pas sur lui.
        bool ReachesAlly(Cell cell) =>
            cell.IsAdjacentTo(ally.Position) && CanStepIgnoringDestination(unit, cell, ally.Position, downedBlockCorners: true);
        var reaches = ReachesAlly(position)
            || Pathfinder.ReachableCells(_grid, position, skill.Range - 1, cell => OccupantFor(unit, cell)).Keys.Any(ReachesAlly);
        if (!reaches)
            return (null, RejectionReason.OutOfRange);

        if (drop == ally.Position || drop == position || position.DistanceTo(drop) > skill.RescueDropRadius!.Value
            || !IsFreeForRescue(unit, ally, drop) || !HasLineOfSight(unit, position, drop))
            return (null, RejectionReason.InvalidDestination);
        if (RescuerEnd(unit, ally, position, drop, skill.RescueDropRadius.Value) is null)
            return (null, RejectionReason.InvalidDestination);
        return (new PlannedAction(skill, ally, null, drop), null);
    }

    /// <summary>Case libre une fois le sauvetage fait : le lanceur et l'allié la quittent.</summary>
    private bool IsFreeForRescue(Unit rescuer, Unit ally, Cell cell) =>
        _grid.IsWalkable(cell) && (UnitAt(cell) is not { } occupant || occupant == rescuer || occupant == ally);

    /// <summary>
    /// Où finit le lanceur d'un sauvetage : à côté du dépôt, sur sa case s'il y est déjà, sinon sur la première case
    /// voisine du dépôt (ordre fixe) qui est libre, dans le rayon et en vue depuis <paramref name="position"/>, et
    /// accessible depuis le dépôt sans coupe d'angle.
    /// </summary>
    private Cell? RescuerEnd(Unit rescuer, Unit ally, Cell position, Cell drop, int radius)
    {
        if (position.IsAdjacentTo(drop) && CanStepIgnoringDestination(rescuer, drop, position, downedBlockCorners: true))
            return position;
        return _grid.Neighbors(drop)
            .Where(c => c != drop && IsFreeForRescue(rescuer, ally, c) && position.DistanceTo(c) <= radius
                        && HasLineOfSight(rescuer, position, c)
                        && CanStepIgnoringDestination(rescuer, drop, c, downedBlockCorners: true))
            .Select(c => (Cell?)c)
            .FirstOrDefault();
    }

    /// <summary>Interaction validée : son type et l'unité au contact visée.</summary>
    internal sealed record PlannedInteraction(InteractionKind Kind, Unit Target);

    /// <summary>
    /// Validation d'une interaction depuis <paramref name="position"/> (case d'arrivée). Relever : un allié à terre,
    /// au contact (sans coupe d'angle par un mur, comme le corps à corps).
    /// </summary>
    private (PlannedInteraction? Interaction, RejectionReason? Reason) PlanInteraction(Unit unit, Cell position, InteractionUse use)
    {
        switch (use.Kind)
        {
            case InteractionKind.Revive:
                if (FindUnit(use.Target) is not { Status: UnitStatus.Downed } target || target.Team != unit.Team)
                    return (null, RejectionReason.InvalidTarget);
                if (!IsInMeleeReach(position, target.Position))
                    return (null, RejectionReason.OutOfRange);
                return (new PlannedInteraction(use.Kind, target), null);
            default: // valeur inconnue envoyée par un client : refusée, pas une erreur du serveur
                return (null, RejectionReason.InvalidTarget);
        }
    }
}
