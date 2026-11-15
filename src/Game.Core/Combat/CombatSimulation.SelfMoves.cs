using Game.Core.Grids;

namespace Game.Core.Combat;

// Mouvements spéciaux du lanceur (Percée, Saut, Charge, Ruée, Bond, Repli) : visée, puis effet. Aucun ne déclenche
// d'attaque d'opportunité.
public sealed partial class CombatSimulation
{
    /// <summary>
    /// Mouvements spéciaux, depuis <paramref name="position"/>. <see cref="PlannedAction.Destination"/> : où va le
    /// lanceur. Percée : <see cref="PlannedAction.Zone"/> = la course (arrivée comprise). Saut : la zone autour de
    /// l'atterrissage (ou du départ). Charge : <see cref="PlannedAction.Target"/> = l'unité percutée. Ruée, Bond,
    /// Repli : la cible frappée.
    /// </summary>
    private (PlannedAction? Action, RejectionReason? Reason) PlanSelfMove(
        Unit unit, Cell position, SkillDefinition skill, SkillUse use, SelfMoveType selfMove) => selfMove switch
    {
        SelfMoveType.Pierce => PlanPierce(unit, position, skill, use),
        SelfMoveType.Leap => PlanLeap(unit, position, skill, use),
        SelfMoveType.Charge => PlanCharge(unit, position, skill, use),
        SelfMoveType.Dash or SelfMoveType.Pounce => PlanStrikeMove(unit, position, skill, use, selfMove),
        SelfMoveType.Retreat => PlanRetreat(unit, position, skill, use),
        _ => throw new InvalidOperationException($"Unknown self move {selfMove}."),
    };

    private (PlannedAction? Action, RejectionReason? Reason) PlanPierce(Unit unit, Cell position, SkillDefinition skill, SkillUse use)
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
        var end = course.FindLastIndex(c => IsFreeFor(unit, c));
        if (end < 0)
            return (null, RejectionReason.Unreachable);
        return (new PlannedAction(skill, null, course.GetRange(0, end + 1), course[end]), null);
    }

    private (PlannedAction? Action, RejectionReason? Reason) PlanLeap(Unit unit, Cell position, SkillDefinition skill, SkillUse use)
    {
        if (use.Cell is not { } landing || use.Target is not null)
            return (null, RejectionReason.InvalidTarget);
        if (!IsWithinRange(position, landing, skill.Range))
            return (null, RejectionReason.OutOfRange);
        if (!CanLandOn(unit, position, landing))
            return (null, _grid.IsWalkable(landing) && IsFreeFor(unit, landing) ? RejectionReason.NoLineOfSight : RejectionReason.InvalidDestination);
        var center = skill.MoveOptions.ZoneAtStart ? position : landing;
        var zone = skill.Zone!.Cells(_grid, center, center);
        return zone is null
            ? (null, RejectionReason.InvalidTarget)
            : (new PlannedAction(skill, null, zone, landing), null);
    }

    /// <summary>Atterrissage d'un saut : une case libre, vue depuis le départ par-dessus les unités et les obstacles bas (pas les murs).</summary>
    private bool CanLandOn(Unit unit, Cell from, Cell landing) =>
        _grid.IsWalkable(landing) && IsFreeFor(unit, landing) && LineOfSight.IsClear(_grid, from, landing);

    /// <summary>Case sans autre unité que <paramref name="unit"/> (qui a pu la quitter dans la même commande).</summary>
    private bool IsFreeFor(Unit unit, Cell cell) => UnitAt(cell) is not { } occupant || occupant == unit;

    private (PlannedAction? Action, RejectionReason? Reason) PlanCharge(Unit unit, Cell position, SkillDefinition skill, SkillUse use)
    {
        if (StruckEnemy(unit, skill, use, wantsCell: false) is not { } target || position.DirectionTo(target.Position) is not { } step)
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

    /// <summary>
    /// L'ennemi debout que vise un mouvement spécial ciblé, ou <c>null</c> si la désignation ne convient pas
    /// (<paramref name="wantsCell"/> : une case doit l'accompagner, ou au contraire ne pas être donnée).
    /// </summary>
    private Unit? StruckEnemy(Unit unit, SkillDefinition skill, SkillUse use, bool wantsCell) =>
        use.Target is { } targetId && (use.Cell is not null) == wantsCell && FindUnit(targetId) is { } target
        && target.Status == UnitStatus.Active && IsRightSide(unit, target, skill)
            ? target
            : null;

    /// <summary>Ruée ou Bond : le lanceur rejoint la case libre la plus proche au contact de sa cible, puis la frappe.</summary>
    private (PlannedAction? Action, RejectionReason? Reason) PlanStrikeMove(
        Unit unit, Cell position, SkillDefinition skill, SkillUse use, SelfMoveType selfMove)
    {
        if (StruckEnemy(unit, skill, use, wantsCell: false) is not { } target)
            return (null, RejectionReason.InvalidTarget);
        var end = selfMove == SelfMoveType.Dash
            ? DashEnd(unit, position, target, skill.Range)
            : PounceEnd(unit, position, target, skill.Range);
        return end is null
            ? (null, RejectionReason.OutOfRange)
            : (new PlannedAction(skill, target, null, end), null);
    }

    /// <summary>
    /// Fin d'une ruée : la case où l'unité peut s'arrêter, à <paramref name="steps"/> pas au plus à pied, qui touche la
    /// cible au contact ; la plus proche à pied (à égalité, la première dans l'ordre d'exploration des chemins). Sa
    /// propre case si elle y est déjà.
    /// </summary>
    private Cell? DashEnd(Unit unit, Cell position, Unit target, int steps)
    {
        // Les cases atteignables sont énumérées par distance croissante.
        foreach (var cell in WalkableWithin(unit, position, steps).Keys)
        {
            if (IsInMeleeReach(cell, target.Position))
                return cell;
        }
        return null;
    }

    /// <summary>Cases où l'unité peut s'arrêter à pied depuis <paramref name="from"/>, avec leur distance (règles d'un déplacement).</summary>
    private IReadOnlyDictionary<Cell, int> WalkableWithin(Unit unit, Cell from, int steps) =>
        Pathfinder.ReachableCells(_grid, from, steps, cell => OccupantFor(unit, cell));

    /// <summary>
    /// Cases où l'unité pourrait s'arrêter à pied en <paramref name="steps"/> pas au plus depuis <paramref name="from"/>
    /// (sa case, ou celle du fantôme), pour l'aperçu d'un repli. Vide si l'unité est inconnue.
    /// </summary>
    public IReadOnlyDictionary<Cell, int> WalkableCells(UnitId unitId, Cell from, int steps) =>
        FindUnit(unitId) is { } unit ? WalkableWithin(unit, from, steps) : new Dictionary<Cell, int>();

    /// <summary>
    /// Fin d'un bond : la case libre au contact de la cible où l'unité peut atterrir (à portée, comme un Saut), la plus
    /// proche du départ (en cases, puis en ligne droite ; à égalité : de haut en bas, puis de gauche à droite). Sa propre
    /// case si elle y est déjà.
    /// </summary>
    private Cell? PounceEnd(Unit unit, Cell position, Unit target, int range)
    {
        if (IsInMeleeReach(position, target.Position))
            return position;
        // Distance en cases d'abord, puis à vol d'oiseau (carré de la distance, en entiers).
        (int Cells, int Straight) Far(Cell cell) =>
            (position.DistanceTo(cell), (cell.X - position.X) * (cell.X - position.X) + (cell.Y - position.Y) * (cell.Y - position.Y));
        Cell? best = null;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var cell = new Cell(target.Position.X + dx, target.Position.Y + dy);
                if (IsInMeleeReach(cell, target.Position) && IsWithinRange(position, cell, range) && CanLandOn(unit, position, cell)
                    && (best is not { } current || Far(cell).CompareTo(Far(current)) < 0))
                    best = cell;
            }
        }
        return best;
    }

    /// <summary>
    /// Repli : un ennemi au contact (frappé d'abord), puis une case libre où le lanceur peut aller à pied en
    /// <see cref="SelfMoveOptions.RetreatDistance"/> pas au plus.
    /// </summary>
    private (PlannedAction? Action, RejectionReason? Reason) PlanRetreat(Unit unit, Cell position, SkillDefinition skill, SkillUse use)
    {
        if (StruckEnemy(unit, skill, use, wantsCell: true) is not { } target)
            return (null, RejectionReason.InvalidTarget);
        if (CheckTarget(unit, position, target, skill) is { } reason)
            return (null, reason);
        var end = use.Cell!.Value;
        if (end == position
            || Pathfinder.FindPath(_grid, position, end, skill.MoveOptions.RetreatDistance, cell => OccupantFor(unit, cell)) is null)
            return (null, RejectionReason.InvalidDestination);
        return (new PlannedAction(skill, target, null, end), null);
    }

    /// <summary>Après son mouvement, le lanceur peut-il encore frapper : debout, et pas étourdi par le sol où il arrive ?</summary>
    private static bool CanStillAct(Unit caster) => caster.Status == UnitStatus.Active && !caster.HasStatus(StatusKind.Stunned);

    /// <summary>
    /// Les cases d'une Charge, en ligne droite jusqu'à <paramref name="destination"/>, coupées à la première case libre
    /// dont le sol arrête le lanceur (piège, sol qui fige).
    /// </summary>
    private List<Cell> ChargeCourse(Unit caster, Cell start, Cell destination)
    {
        var step = start.DirectionTo(destination)!.Value;
        var course = new List<Cell>();
        for (var cell = start; cell != destination;)
        {
            var next = new Cell(cell.X + step.X, cell.Y + step.Y);
            course.Add(next);
            // Par-dessus une unité à terre ou un civil, il ne touche pas le sol.
            if (IsFreeFor(caster, next) && GroundHalts(caster, cell, next))
                break;
            cell = next;
        }
        return course;
    }

    /// <summary>
    /// Mouvement spécial planifié par <see cref="PlanSelfMove"/> (sans attaque d'opportunité) :
    /// Percée = course puis coups sur tout ce qui a été traversé ; Saut = atterrissage puis zone ;
    /// Charge = course jusqu'à l'unité percutée, puis le coup (aucun si le décor l'a arrêtée) ;
    /// Ruée et Bond = trajet, coup, puis poussée ou retour éventuels ; Repli = coup, puis trajet.
    /// </summary>
    private void ExecuteSelfMove(Unit caster, SkillDefinition skill, SelfMoveType selfMove, Unit? struck,
        IReadOnlyList<Cell>? cells, Cell destination, int damageFactor)
    {
        var options = skill.MoveOptions;
        var start = caster.Position;
        switch (selfMove)
        {
            case SelfMoveType.Pierce:
            {
                var course = cells!;
                _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, null, course));
                var crossed = course.Take(course.Count - 1).ToHashSet();
                var hit = _units.Where(u => u != caster && u.Status == UnitStatus.Active && !u.IsCivilian
                                            && crossed.Contains(u.Position)
                                            && (!options.EnemiesOnly || AreOpponents(u, caster))).ToList();
                Displace(caster, destination);
                if (!CanStillAct(caster))
                    break; // tombé ou figé par le sol à l'arrivée
                foreach (var unit in hit)
                    ResolveHit(caster, skill, unit, damageFactor);
                break;
            }
            case SelfMoveType.Leap:
                _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, null, cells));
                Displace(caster, destination);
                if (!CanStillAct(caster))
                    break;
                foreach (var unit in UnitsAffectedBy(caster, skill, cells!))
                    ResolveHit(caster, skill, unit, damageFactor, origin: options.ZoneAtStart ? start : destination); // d'où part l'onde
                break;
            case SelfMoveType.Charge:
                // Même ordre que les autres : la compétence, la course, puis le coup (s'il a percuté quelqu'un).
                _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, struck?.Id));
                if (destination != start)
                {
                    // Il court au sol : un piège ou un sol qui fige sur sa ligne l'arrête, et il ne percute plus personne.
                    var course = ChargeCourse(caster, start, destination);
                    if (course[^1] != destination)
                        struck = null;
                    Displace(caster, course[^1], course);
                }
                if (struck is not null && CanStillAct(caster))
                    ResolveHit(caster, skill, struck, damageFactor);
                break;
            case SelfMoveType.Dash or SelfMoveType.Pounce:
            {
                _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, struck!.Id));
                // L'élan : compté avant de partir, sur le trajet à pied. Il ne vaut que pour le coup, pas pour une collision.
                var steps = options.DamagePercentPerCell == 0 ? 0
                    : WalkableWithin(caster, start, skill.Range).GetValueOrDefault(destination, start.DistanceTo(destination));
                if (destination != start)
                    Displace(caster, destination);
                if (!CanStillAct(caster))
                    break;
                var factor = (int)((long)damageFactor * (100 + options.DamagePercentPerCell * steps) / 100);
                var landed = ResolveHit(caster, skill, struck, factor);
                if (landed && struck.Status == UnitStatus.Active && skill.ForcedMove is { } move)
                    ApplyForcedMove(caster, skill, struck, move, damageFactor, null);
                if (options.ReturnsToStart && caster.Status == UnitStatus.Active && caster.Position != start)
                    Displace(caster, start);
                break;
            }
            case SelfMoveType.Retreat:
                _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, struck!.Id));
                ResolveHit(caster, skill, struck, damageFactor);
                if (caster.Status == UnitStatus.Active)
                    Displace(caster, destination);
                break;
        }
    }
}
