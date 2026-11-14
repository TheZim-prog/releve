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
    /// Compétence à zone : une case, à portée (0 = sur soi), libre et en vue pour un carré, alignée pour une ligne,
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
            if (cells is null || cells.Count == 0)
                return (null, RejectionReason.InvalidTarget);
            if (zone.Shape == ZoneShape.Square && cell != position && !HasLineOfSight(unit, position, cell))
                return (null, RejectionReason.NoLineOfSight);
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
        && (UnitAt(destination) is null || UnitAt(destination) == caster)
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
        if (!HasLineOfSight(unit, from, target.Position))
            return RejectionReason.NoLineOfSight;
        return null;
    }

    /// <summary>
    /// Mouvements spéciaux, depuis <paramref name="position"/>. <see cref="PlannedAction.Destination"/> : où finit le
    /// lanceur. Percée : <see cref="PlannedAction.Zone"/> = la course (arrivée comprise). Saut : la zone autour de
    /// l'atterrissage. Charge : <see cref="PlannedAction.Target"/> = l'unité percutée.
    /// </summary>
    private (PlannedAction? Action, RejectionReason? Reason) PlanSelfMove(
        Unit unit, Cell position, SkillDefinition skill, SkillUse use, SelfMoveType selfMove)
    {
        switch (selfMove)
        {
            case SelfMoveType.Pierce:
            {
                if (use.Cell is not { } aim || use.Target is not null || position.DirectionTo(aim) is not { } step)
                    return (null, RejectionReason.InvalidTarget);
                // Course à travers les unités (les civils traversés ne sont pas touchés), arrêtée par les murs et les
                // obstacles bas (sans coupe d'angle par le décor).
                var course = new List<Cell>();
                var previous = position;
                for (var i = 0; i < skill.Range; i++)
                {
                    var next = new Cell(previous.X + step.X, previous.Y + step.Y);
                    if (!_grid.CanStep(previous, next))
                        break;
                    course.Add(next);
                    previous = next;
                }
                // Arrivée : la dernière case libre de la course.
                var end = course.FindLastIndex(c => UnitAt(c) is not { } occupant || occupant == unit);
                if (end < 0)
                    return (null, RejectionReason.Unreachable);
                return (new PlannedAction(skill, null, course.GetRange(0, end + 1), course[end]), null);
            }

            case SelfMoveType.Leap:
            {
                if (use.Cell is not { } landing || use.Target is not null)
                    return (null, RejectionReason.InvalidTarget);
                if (!IsWithinRange(position, landing, skill.Range))
                    return (null, RejectionReason.OutOfRange);
                if (!_grid.IsWalkable(landing) || (UnitAt(landing) is { } occupant && occupant != unit))
                    return (null, RejectionReason.InvalidDestination);
                // Par-dessus les unités et les obstacles bas, pas les murs.
                if (!LineOfSight.IsClear(_grid, position, landing))
                    return (null, RejectionReason.NoLineOfSight);
                var zone = skill.Zone!.Cells(_grid, landing, landing);
                return zone is null
                    ? (null, RejectionReason.InvalidTarget)
                    : (new PlannedAction(skill, null, zone, landing), null);
            }

            case SelfMoveType.Rush:
            {
                if (use.Target is not { } targetId || use.Cell is not null || FindUnit(targetId) is not { } target
                    || target.Status != UnitStatus.Active || !IsRightSide(unit, target, skill)
                    || position.DirectionTo(target.Position) is not { } step)
                    return (null, RejectionReason.InvalidTarget);
                if (!IsWithinRange(position, target.Position, skill.Range))
                    return (null, RejectionReason.OutOfRange);

                // Fonce jusqu'à la première unité debout de la ligne, par-dessus les unités à terre et les civils ; le
                // décor (ou un coin bouché) l'arrête sans qu'il touche personne. Il finit sur la dernière case libre de
                // sa course ; si elle n'est pas au contact de l'unité (une case occupée juste devant), il ne la frappe pas.
                Unit? struck = null;
                var lastFree = position;
                var previous = position;
                for (var i = 0; i < skill.Range; i++)
                {
                    var next = new Cell(previous.X + step.X, previous.Y + step.Y);
                    if (!CanStepIgnoringDestination(unit, previous, next, downedBlockCorners: false))
                        break;
                    if (UnitAt(next) is { } other && other != unit)
                    {
                        if (other.Status == UnitStatus.Active && !other.IsCivilian)
                        {
                            if (lastFree == previous)
                                struck = other;
                            break;
                        }
                    }
                    else
                    {
                        lastFree = next;
                    }
                    previous = next;
                }
                return (new PlannedAction(skill, struck, null, lastFree), null);
            }

            default:
                throw new InvalidOperationException($"Unknown self move {selfMove}.");
        }
    }

    /// <summary>
    /// Sauvetage, depuis <paramref name="position"/> : un allié <b>à terre</b> (ou un civil debout) à <see cref="SkillDefinition.Range"/> pas au
    /// plus, à pied (chemin réel jusqu'à son contact, sans coupe d'angle) ; une case de dépôt libre, autre que celle du
    /// lanceur, au plus à <see cref="SkillDefinition.RescueDropRadius"/> et en vue ; et une case d'arrivée pour le lanceur
    /// (<see cref="RescuerEnd"/>).
    /// </summary>
    private (PlannedAction? Action, RejectionReason? Reason) PlanRescue(Unit unit, Cell position, SkillDefinition skill, SkillUse use)
    {
        // Un allié à terre, ou un civil (debout : à 0 PV, il est perdu).
        if (use.Target is not { } allyId || use.Cell is not { } drop || FindUnit(allyId) is not { } ally || ally == unit
            || !(ally.IsCivilian ? ally.Status == UnitStatus.Active : ally.Team == unit.Team && ally.Status == UnitStatus.Downed))
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
