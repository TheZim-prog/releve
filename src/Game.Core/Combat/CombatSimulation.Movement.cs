using Game.Core.Grids;

namespace Game.Core.Combat;

// Déplacement et géométrie : chemins, attaques d'opportunité, contact, portée, ligne de vue, qui gêne qui.
public sealed partial class CombatSimulation
{
    /// <summary>Cases où l'unité peut aller par un déplacement (sa case comprise), avec leur distance à pied.</summary>
    public IReadOnlyDictionary<Cell, int> ReachableCells(UnitId unitId) =>
        FindUnit(unitId) is { } unit
            ? Pathfinder.ReachableCells(_grid, unit.Position, unit.Mobility, cell => OccupantFor(unit, cell))
            : new Dictionary<Cell, int>();

    /// <summary>Chemin qu'emprunterait un déplacement de l'unité vers <paramref name="destination"/>, ou <c>null</c>.</summary>
    public IReadOnlyList<Cell>? PathFor(UnitId unitId, Cell destination) =>
        FindUnit(unitId) is { } unit ? PathFor(unit, destination) : null;

    private IReadOnlyList<Cell>? PathFor(Unit unit, Cell destination) =>
        Pathfinder.FindPath(_grid, unit.Position, destination, unit.Mobility, cell => OccupantFor(unit, cell));

    /// <summary>
    /// Déplace l'unité pas à pas ; chaque pas qui sort de la portée d'un ennemi peut déclencher son attaque
    /// d'opportunité (dans l'ordre des unités), sauf si l'unité a le passif qui les ignore ; chaque case où elle entre
    /// peut cacher un piège ou un sol gelé.
    /// Renvoie faux si l'unité est tombée, étourdie ou immobilisée en chemin.
    /// </summary>
    private bool MoveAlong(Unit unit, IReadOnlyList<Cell> path)
    {
        var segmentStart = unit.Position;
        var segment = new List<Cell>();
        var lastFreeCell = unit.Position;
        var canBePunished = !unit.HasPassive(PassiveKind.IgnoresOpportunityAttacks);

        void FlushSegment()
        {
            if (segment.Count == 0)
                return;
            _events.Add(new UnitMoved(CurrentTick, unit.Id, segmentStart, segment));
            segmentStart = unit.Position;
            segment = new List<Cell>();
        }

        // Tombée, ou arrêtée net (étourdie, immobilisée) : elle recule sur sa dernière case libre si elle partageait la sienne.
        // Immobilisée en posant le pied sur sa destination (<paramref name="arrived"/>), elle est arrivée : son action suit.
        // Arrêtée par un Coup d'arrêt (<paramref name="stopped"/>), elle s'arrête là où elle est.
        bool IsHalted(bool arrived = false, bool stopped = false)
        {
            if (unit.Status != UnitStatus.Active)
                return true;
            if (!stopped && !unit.HasStatus(StatusKind.Stunned) && (unit.CanMove || arrived))
                return false;
            if (IsOccupiedByOther(unit, unit.Position))
                unit.Position = lastFreeCell;
            _events.Add(new UnitStopped(CurrentTick, unit.Id, unit.Position));
            return true;
        }

        foreach (var next in path)
        {
            foreach (var enemy in canBePunished ? _units : Enumerable.Empty<Unit>())
            {
                if (!CanMakeOpportunityAttack(enemy, unit, next))
                    continue;

                FlushSegment();
                var attack = enemy.BasicAttack!;
                enemy.OpportunityAttacksLeft--;
                _events.Add(new OpportunityAttack(CurrentTick, enemy.Id, unit.Id, attack.Id));
                var landed = ResolveHit(enemy, attack, unit, DamageFactorOf(enemy), lastFreeCell);
                EndSource(); // chaque attaque d'opportunité est une source à part
                FlushGauge(enemy);
                if (IsHalted(stopped: landed && enemy.HasPassive(PassiveKind.StoppingBlow)))
                    return false;
            }

            var from = unit.Position;
            unit.Position = next;
            segment.Add(next);
            if (!IsOccupiedByOther(unit, next))
                lastFreeCell = next;

            // Un piège ou un sol gelé sur cette case, puis une surveillance sur ce pas (qui entre, sort ou bouge dans sa
            // zone) : chacun est une source à part, comme une attaque d'opportunité.
            var ground = GroundAwaits(unit, from, next);
            var watched = HasZoneWatchOn(unit, from, next);
            if (ground || watched)
            {
                FlushSegment();
                if (ground)
                {
                    EnterGround(unit, from, next, lastFreeCell);
                    EndSource();
                }
                if (watched)
                    TriggerZoneWatches(unit, from, next, lastFreeCell);
                if (IsHalted(arrived: next == path[^1]))
                    return false;
            }
        }

        FlushSegment();
        return true;
    }

    /// <summary>
    /// <paramref name="enemy"/> peut punir <paramref name="mover"/> qui va en <paramref name="next"/> : le pas sort
    /// de sa portée, et c'est un ennemi debout, non étourdi, pas en train de charger, avec une attaque de base et
    /// une attaque d'opportunité restante.
    /// </summary>
    private bool CanMakeOpportunityAttack(Unit enemy, Unit mover, Cell next) =>
        enemy != mover
        && AreOpponents(enemy, mover)
        && enemy.Status == UnitStatus.Active
        && !enemy.HasStatus(StatusKind.Stunned)
        && enemy.Charge is null
        && enemy.BasicAttack is not null
        && enemy.OpportunityAttacksLeft > 0
        && ControllerOf(enemy) != Behaviors.FleeInstance // en fuite, elle ne fait que courir
        && IsInMeleeReach(enemy, mover.Position)
        && !IsInMeleeReach(enemy, next);

    /// <summary>
    /// Case que l'unité peut frapper au contact : adjacente, libre, sans coupe d'angle par un mur (règle de la vue :
    /// un obstacle bas sur un coin ne gêne pas).
    /// </summary>
    private bool IsInMeleeReach(Unit unit, Cell cell) => IsInMeleeReach(unit.Position, cell);

    private bool IsInMeleeReach(Cell from, Cell cell) =>
        from.IsAdjacentTo(cell) && LineOfSight.IsClear(_grid, from, cell);

    private bool IsOccupiedByOther(Unit unit, Cell cell) =>
        _units.Any(u => u != unit && u.Status != UnitStatus.Removed && u.Position == cell);

    /// <summary>
    /// Pas de <paramref name="from"/> à <paramref name="to"/> pour <paramref name="unit"/>, sans tenir compte de qui
    /// occupe <paramref name="to"/> (on peut s'y écraser) : le décor (coupe d'angle comprise) et, en diagonale, les
    /// ennemis de l'unité sur les coins (à terre compris, sauf si <paramref name="downedBlockCorners"/> est faux).
    /// </summary>
    private bool CanStepIgnoringDestination(Unit unit, Cell from, Cell to, bool downedBlockCorners)
    {
        if (!_grid.CanStep(from, to))
            return false;
        if (!from.IsDiagonalTo(to))
            return true;
        bool Blocks(Cell corner) =>
            UnitAt(corner) is { } other && AreOpponents(other, unit) && (downedBlockCorners || other.Status == UnitStatus.Active);
        return !Blocks(new Cell(from.X, to.Y)) && !Blocks(new Cell(to.X, from.Y));
    }

    /// <summary>Distance en cases (diagonale = 1), sans tenir compte des obstacles.</summary>
    private static bool IsWithinRange(Cell from, Cell to, int range)
    {
        var distance = from.DistanceTo(to);
        return distance > 0 && distance <= range;
    }

    /// <summary>
    /// Vue de <paramref name="from"/> (case d'arrivée de l'attaquant) vers <paramref name="to"/>.
    /// Au contact : pas de coupe d'angle par un mur, mais seul le décor compte (règle du corps à corps).
    /// Au-delà : ligne de vue, bloquée par les murs, les ennemis debout de l'attaquant et les zones au sol qui la
    /// coupent (fumée ; pare-tirs pour le tir <paramref name="skill"/>). Les obstacles bas ne gênent jamais la vue.
    /// </summary>
    private bool HasLineOfSight(Unit attacker, Cell from, Cell to, SkillDefinition? skill = null)
    {
        if (from.IsAdjacentTo(to))
            return LineOfSight.IsClear(_grid, from, to);
        if (GroundBlocksLine(from, to, skill))
            return false;
        return LineOfSight.IsClear(_grid, from, to, cell => BlocksSightOf(attacker, cell));
    }

    // Qui fait obstacle à qui. Les deux règles diffèrent volontairement (docs/design.md, section 3) :
    // un héros à terre occupe toujours sa case pour le déplacement, mais on tire par-dessus lui.

    /// <summary>
    /// Pour le déplacement : toute unité présente (debout ou à terre), alliée ou ennemie, vue par
    /// <paramref name="mover"/>. Public pour l'aperçu des cases atteignables (<see cref="Pathfinder"/>).
    /// </summary>
    public Occupant OccupantFor(Unit mover, Cell cell)
    {
        var other = UnitAt(cell);
        if (other is null || other == mover)
            return Occupant.None;
        // On traverse un civil comme un allié, et un civil traverse tout le monde.
        return AreOpponents(other, mover) ? Occupant.Enemy : Occupant.Ally;
    }

    /// <summary>Adversaires : de camps différents, et aucun des deux n'est un civil (neutre).</summary>
    internal static bool AreOpponents(Unit a, Unit b) => a.Team != b.Team && !a.IsCivilian && !b.IsCivilian;

    /// <summary>Pour la ligne de vue : seuls les ennemis debout bloquent.</summary>
    private bool BlocksSightOf(Unit viewer, Cell cell) =>
        UnitAt(cell) is { Status: UnitStatus.Active } other && AreOpponents(other, viewer);
}
