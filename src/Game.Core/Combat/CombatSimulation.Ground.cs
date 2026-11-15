using Game.Core.Grids;

namespace Game.Core.Combat;

// Ce qu'une compétence laisse sur le terrain : zones au sol (fumée, pare-tirs, verglas, pièges) et obstacles dressés
// pour un temps (blocs de glace).
public sealed partial class CombatSimulation
{
    private readonly List<GroundZone> _groundZones = new();

    // Obstacles dressés par une compétence et encore debout, avec le tick où ils fondent, dans l'ordre de pose.
    private readonly List<(Cell Cell, int EndTick)> _raisedObstacles = new();
    private int _lastGroundZoneId;

    /// <summary>Zones au sol en cours, dans l'ordre de pose.</summary>
    public IReadOnlyList<GroundZone> GroundZones => _groundZones;

    /// <summary>
    /// La compétence laisse sa zone au sol sur <paramref name="cells"/>. Un piège remplace celui que le lanceur avait
    /// déjà posé ; les autres zones posent tout de suite leurs états à qui s'y trouve.
    /// </summary>
    private void LayGround(Unit caster, SkillDefinition skill, IReadOnlyList<Cell> cells)
    {
        var definition = skill.Ground!;
        if (definition.IsTrap)
        {
            foreach (var previous in _groundZones.Where(z => z.Owner == caster.Id && z.Definition.IsTrap).ToList())
                EndGround(previous);
        }

        int? endTick = definition.DurationMs == 0 ? null : CurrentTick + Rules.MillisecondsToTicks(definition.DurationMs);
        var zone = new GroundZone(++_lastGroundZoneId, caster, skill, cells, endTick);
        _groundZones.Add(zone);
        _events.Add(new GroundZoneCreated(CurrentTick, zone.Id, caster.Id, skill.Id, cells, endTick));
        if (definition.IsTrap)
            return;
        foreach (var unit in _units.Where(u => zone.Contains(u.Position) && Touches(zone, u)).ToList())
            ApplyGroundEffects(zone, unit);
    }

    private void EndGround(GroundZone zone)
    {
        if (_groundZones.Remove(zone))
            _events.Add(new GroundZoneEnded(CurrentTick, zone.Id));
    }

    /// <summary>La zone touche-t-elle cette unité : debout, et, pour une zone réservée aux ennemis, adversaire du camp qui l'a posée.</summary>
    private static bool Touches(GroundZone zone, Unit unit) =>
        unit.Status == UnitStatus.Active
        && (zone.Definition.Affects == ZoneTargets.All || (unit.Team != zone.Team && !unit.IsCivilian));

    /// <summary>
    /// La zone pose ses états à l'unité (seulement <paramref name="missingOnly"/> : ceux qu'elle n'a plus), sauf si un
    /// dôme de son camp la couvre : il les bloque comme un coup, et tombe à la fin de la source en cours.
    /// </summary>
    private void ApplyGroundEffects(GroundZone zone, Unit unit, bool missingOnly = false)
    {
        var effects = zone.Definition.Effects.Where(e => !missingOnly || !unit.HasStatus(e.Status)).ToList();
        if (effects.Count == 0 || IsBlockedByDome(zone.Skill, unit, harmful: true))
            return;
        ApplyUnblockedGroundEffects(zone, unit, effects);
    }

    private void ApplyUnblockedGroundEffects(GroundZone zone, Unit unit, IReadOnlyList<StatusEffect> effects)
    {
        // Le lanceur peut être tombé depuis : la zone garde ses effets.
        var owner = FindUnit(zone.Owner)!;
        foreach (var effect in effects)
        {
            if (unit.Status == UnitStatus.Active)
                ApplyStatus(unit, effect, owner);
        }
    }

    /// <summary>Début de tick : les zones au sol et les obstacles dressés arrivés à échéance disparaissent.</summary>
    private void ExpireGround()
    {
        foreach (var zone in _groundZones.Where(z => z.EndTick <= CurrentTick).ToList())
            EndGround(zone);
        foreach (var (cell, _) in _raisedObstacles.Where(o => o.EndTick <= CurrentTick).ToList())
        {
            ForgetRaisedObstacle(cell);
            _grid.SetTerrain(cell, Terrain.Floor);
            _events.Add(new ObstacleMelted(CurrentTick, cell));
        }
    }

    /// <summary>
    /// Zones dont les états se renouvellent (ex. Verglas) : qui s'y trouve encore, et n'a plus l'état, le reçoit de
    /// nouveau. Après la fin des états du tick : l'état ne s'interrompt pas tant que l'unité reste dans la zone.
    /// </summary>
    private void RenewGroundEffects()
    {
        foreach (var zone in _groundZones.Where(z => z.Definition.Renews).ToList())
        {
            foreach (var unit in _units.Where(u => zone.Contains(u.Position) && Touches(zone, u)).ToList())
                ApplyGroundEffects(zone, unit, missingOnly: true);
            EndSource(); // chaque zone est une source : un dôme qui en a protégé quelqu'un tombe
        }
    }

    /// <summary>Une zone au sol réagirait-elle à l'entrée de l'unité sur <paramref name="to"/>, venue de <paramref name="from"/> ?</summary>
    private bool GroundAwaits(Unit unit, Cell from, Cell to) => _groundZones.Any(z => Awaits(z, unit, from, to));

    /// <summary>Entrer, c'est venir du dehors : passer d'une case à l'autre de la même zone ne compte pas.</summary>
    private static bool Awaits(GroundZone zone, Unit unit, Cell from, Cell to) =>
        (zone.Definition.IsTrap || zone.Definition.Effects.Count > 0)
        && zone.Contains(to) && !zone.Contains(from) && Touches(zone, unit);

    /// <summary>
    /// Le sol de <paramref name="to"/> arrêterait-il une unité lancée dessus (poussée, Charge) : un piège qui l'attend,
    /// ou une zone qui l'immobilise ou l'étourdit à l'entrée ?
    /// </summary>
    private bool GroundHalts(Unit unit, Cell from, Cell to) =>
        _groundZones.Any(z => Awaits(z, unit, from, to)
                              && (z.Definition.IsTrap
                                  || z.Definition.Effects.Any(e => e.Status is StatusKind.Immobilized or StatusKind.Stunned)));

    /// <summary>
    /// L'unité vient d'entrer sur <paramref name="to"/> (à pied ou de force) : les pièges s'y déclenchent, les autres
    /// zones lui posent leurs états. <paramref name="fallbackCell"/> : où elle tombe si un piège la met à terre sur une
    /// case qu'elle partage (en traversant un allié).
    /// </summary>
    private void EnterGround(Unit unit, Cell from, Cell to, Cell? fallbackCell)
    {
        foreach (var zone in _groundZones.Where(z => Awaits(z, unit, from, to)).ToList())
        {
            if (unit.Status != UnitStatus.Active)
                return;
            if (zone.Definition.IsTrap)
                TriggerTrap(zone, unit, fallbackCell);
            else
                ApplyGroundEffects(zone, unit);
        }
    }

    /// <summary>
    /// Le piège se déclenche et disparaît : ses dégâts (multipliés par la puissance de son lanceur) pour l'unité, ou
    /// pour tout ce qui est debout dans son explosion, puis ses états pour l'unité. Un dôme en protège comme d'un coup.
    /// </summary>
    private void TriggerTrap(GroundZone trap, Unit unit, Cell? fallbackCell)
    {
        var definition = trap.Definition;
        var owner = FindUnit(trap.Owner)!;
        var cell = trap.Cells[0];
        _events.Add(new TrapTriggered(CurrentTick, trap.Id, unit.Id, owner.Id, cell));
        EndGround(trap);

        // Sous un dôme de son camp, celui qui l'a déclenché ne prend ni ses dégâts ni ses états.
        var caught = !IsBlockedByDome(trap.Skill, unit, harmful: true);
        if (definition.DealsDamage)
        {
            var blast = definition.Blast?.Cells(_grid, cell, cell)?.ToHashSet();
            var victims = blast is null
                ? new List<Unit> { unit }
                : _units.Where(u => u.Status == UnitStatus.Active && blast.Contains(u.Position)).ToList();
            foreach (var victim in victims.Where(v => v.Status == UnitStatus.Active))
            {
                if (victim != unit)
                    SecondaryDamage(owner, trap.Skill, victim, definition.MinDamage, definition.MaxDamage, owner.DamageFactor);
                else if (caught)
                    UnblockedSecondaryDamage(owner, trap.Skill, unit, definition.MinDamage, definition.MaxDamage, owner.DamageFactor, fallbackCell);
            }
        }
        if (caught)
            ApplyUnblockedGroundEffects(trap, unit, definition.Effects);
    }

    /// <summary>Les écrans de fumée qui touchent ces cases sont dissipés (ex. par le Tourbillon).</summary>
    private void ClearSmoke(IReadOnlyList<Cell> cells)
    {
        foreach (var smoke in _groundZones.Where(z => z.Definition.BlocksSight && cells.Any(z.Contains)).ToList())
            EndGround(smoke);
    }

    /// <summary>
    /// Une zone au sol coupe-t-elle la ligne entre ces deux cases, extrémités comprises ? La fumée coupe toute ligne de
    /// vue, sauf celle d'une compétence qui la dissipe (on vise la fumée elle-même) ; un pare-tirs, seulement celle
    /// d'un tir (voir <see cref="IsShot"/>).
    /// </summary>
    private bool GroundBlocksLine(Cell from, Cell to, SkillDefinition? skill)
    {
        var blockers = LineBlockers(skill);
        if (blockers.Count == 0)
            return false;
        bool Blocked(Cell cell) => blockers.Any(z => z.Contains(cell));
        return Blocked(from) || Blocked(to) || LineOfSight.CrossedCells(from, to).Any(Blocked);
    }

    /// <summary>Les zones au sol qui arrêtent la ligne de cette compétence : fumée (sauf si elle la dissipe), pare-tirs si c'est un tir.</summary>
    private List<GroundZone> LineBlockers(SkillDefinition? skill) =>
        _groundZones
            .Where(z => (z.Definition.BlocksSight && skill is not { ClearsSmoke: true }) || (z.Definition.BlocksShots && IsShot(skill)))
            .ToList();

    /// <summary>
    /// Un « tir » : une attaque qui fait quelque chose à ceux qu'elle touche, pas un pouvoir mental ni du soutien, ni
    /// une compétence qui n'agit que sur le terrain (fumigène, mur de glace). Seul un pare-tirs l'arrête.
    /// </summary>
    private static bool IsShot(SkillDefinition? skill) => skill is { IsOffensive: true, IsMental: false, HasHitEffect: true };

    /// <summary>
    /// Une zone en ligne tirée depuis <paramref name="from"/> s'arrête à la première case d'une fumée ou d'un pare-tirs
    /// (qui n'est pas touchée) ; tirée de l'intérieur, elle ne part pas. Les lignes qui traversent les murs passent.
    /// </summary>
    private IReadOnlyList<Cell> StoppedByGround(SkillDefinition skill, Cell from, IReadOnlyList<Cell> cells)
    {
        if (skill.Zone is not { Shape: ZoneShape.Line, ThroughWalls: false })
            return cells;
        var blockers = LineBlockers(skill);
        if (blockers.Count == 0)
            return cells;
        bool Blocked(Cell cell) => blockers.Any(z => z.Contains(cell));
        return Blocked(from) ? Array.Empty<Cell>() : cells.TakeWhile(c => !Blocked(c)).ToList();
    }

    /// <summary>Case où un obstacle peut être dressé : du sol, sans unité (le lanceur a pu la quitter, mais pas y arriver).</summary>
    private bool CanRaiseOn(Unit caster, Cell casterPosition, Cell cell) =>
        _grid.IsWalkable(cell) && cell != casterPosition && IsFreeFor(caster, cell);

    /// <summary>Dresse l'obstacle de la compétence sur chacune de ces cases, pour sa durée.</summary>
    private void RaiseObstacles(RaisedObstacleDefinition obstacle, IReadOnlyList<Cell> cells)
    {
        var endTick = CurrentTick + Rules.MillisecondsToTicks(obstacle.DurationMs);
        foreach (var cell in cells)
        {
            // Une unité y est arrivée entre-temps (attaque d'opportunité qui arrête le lanceur en chemin, par exemple).
            if (!_grid.IsWalkable(cell) || UnitAt(cell) is not null)
                continue;
            _grid.SetTerrain(cell, obstacle.Terrain);
            if (obstacle.Terrain == Terrain.LowObstacle)
                _coverHp[cell] = obstacle.Hp;
            _raisedObstacles.Add((cell, endTick));
            _events.Add(new ObstacleRaised(CurrentTick, cell, obstacle.Terrain, endTick));
        }
    }

    /// <summary>L'obstacle de cette case n'est plus là (fondu ou détruit) : il n'a plus à fondre, ni de PV.</summary>
    private void ForgetRaisedObstacle(Cell cell)
    {
        _raisedObstacles.RemoveAll(o => o.Cell == cell);
        _coverHp.Remove(cell);
    }
}
