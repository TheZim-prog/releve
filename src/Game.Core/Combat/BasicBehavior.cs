using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>
/// Comportement de base. En mode confus, les « ennemis » sont toutes les autres unités debout, et la cible est
/// toujours la plus proche (la plus forte compétence qui l'atteint). En mode tireur, il utilise aussi ses zones en
/// ligne et tire depuis la case la plus éloignée de l'ennemi le plus proche.
/// </summary>
internal sealed class BasicBehavior(BasicBehavior.Mode mode) : IUnitBehavior
{
    internal enum Mode
    {
        Basic,
        Confused,
        Gunner,
    }

    private bool Confused => mode == Mode.Confused;

    public bool ObeysTaunts => !Confused;

    public Intention? ChooseIntention(CombatSimulation sim, Unit unit)
    {
        var enemies = sim.Units
            .Where(e => e != unit && e.Status == UnitStatus.Active && (Confused ? !e.IsCivilian : CombatSimulation.AreOpponents(e, unit)))
            .ToList();
        if (enemies.Count == 0)
            return null;
        // Provoquée, elle ne voit plus que celui qui l'a provoquée (sauf confuse : elle frappe au plus près).
        if (ObeysTaunts && sim.TaunterOf(unit) is { } taunter)
            enemies = new List<Unit> { taunter };

        var skills = UsableSkills(sim, unit);
        var reachable = ReachableCells(sim, unit);

        if (Confused)
        {
            var nearest = Nearest(sim, unit, enemies);
            var reaching = skills.FirstOrDefault(s => CanAttack(sim, unit, s, nearest, reachable));
            return new Intention((reaching ?? skills.FirstOrDefault())?.Id, nearest.Id);
        }

        // L'ennemi le plus fragile qu'au moins une compétence atteint (OrderBy est stable : à égalité, l'ordre
        // des unités), puis la plus forte compétence qui l'atteint.
        foreach (var target in enemies.OrderBy(e => e.Hp))
        {
            var skill = skills.FirstOrDefault(s => CanAttack(sim, unit, s, target, reachable));
            if (skill is not null)
                return new Intention(skill.Id, target.Id);
        }

        return new Intention(skills.FirstOrDefault()?.Id, Nearest(sim, unit, enemies).Id);
    }

    public ActivateCommand? Execute(CombatSimulation sim, Unit unit, Intention intention)
    {
        if (intention.SkillId is null || unit.FindSkill(intention.SkillId) is not { } skill)
            return null;
        if (unit.UnavailableAt(skill, sim.CurrentTick) is not null)
            return null;
        if (sim.FindUnit(intention.Target) is not { Status: UnitStatus.Active } target)
            return null;
        return FindAttack(sim, unit, skill, target, ReachableCells(sim, unit));
    }

    public ActivateCommand? Approach(CombatSimulation sim, Unit unit, Intention intention)
    {
        if (unit.Reach == 0 || sim.FindUnit(intention.Target) is not { Status: UnitStatus.Active } target)
            return null;

        // Distance à pied jusqu'à la cible (contourne les murs) ; la case atteignable qui la réduit le plus.
        var walking = WalkingDistances(sim, unit, target);
        if (!walking.TryGetValue(unit.Position, out var current))
            return null;
        var best = ReachableCells(sim, unit)
            .Where(c => c != unit.Position && walking.ContainsKey(c))
            .Select(c => (Cell: c, Distance: walking[c]))
            .DefaultIfEmpty((Cell: unit.Position, Distance: current))
            .MinBy(c => c.Distance);
        return best.Distance < current ? new ActivateCommand(unit.Id, best.Cell) : null;
    }

    /// <summary>
    /// Attaques hors recharge, de la plus forte à la plus faible (dégâts moyens ; à égalité, ordre des données).
    /// Ce comportement n'utilise ni les compétences de soutien (sur un allié ou sur soi, zones de soin), ni les dômes,
    /// ni les invocations, ni la Percée ou le Saut (la Charge, oui) ; les zones en ligne, seulement en mode tireur.
    /// </summary>
    private List<SkillDefinition> UsableSkills(CombatSimulation sim, Unit unit) =>
        unit.Skills
            .Where(s => unit.UnavailableAt(s, sim.CurrentTick) is null)
            .Where(s => s.IsOffensive && (mode == Mode.Gunner || s.Zone?.Shape != ZoneShape.Line) && !s.CreatesDome
                        && s.Summon is null && s.SelfMove is null or SelfMoveType.Charge)
            .OrderByDescending(s => s.MinDamage + s.MaxDamage)
            .ToList();

    /// <summary>
    /// L'ennemi le plus proche à pied ; s'il n'y a de chemin vers aucun, ou si l'unité est immobile (tourelle),
    /// le plus proche à vol d'oiseau.
    /// </summary>
    private static Unit Nearest(CombatSimulation sim, Unit unit, List<Unit> enemies)
    {
        if (unit.Reach == 0)
            return enemies.MinBy(e => e.Position.DistanceTo(unit.Position))!;
        var byWalking = enemies
            .Select(e => (Enemy: e, Distance: WalkingDistances(sim, unit, e).GetValueOrDefault(unit.Position, int.MaxValue)))
            .MinBy(e => e.Distance);
        return byWalking.Distance < int.MaxValue
            ? byWalking.Enemy
            : enemies.MinBy(e => e.Position.DistanceTo(unit.Position))!;
    }

    /// <summary>
    /// Activation qui frappe <paramref name="target"/> avec <paramref name="skill"/>, ou <c>null</c>. D'ordinaire en
    /// bougeant le moins possible (cases atteignables par distance croissante, la case actuelle d'abord) ; en mode
    /// tireur, depuis la case la plus éloignée de l'ennemi le plus proche. Une zone carrée est centrée sur la cible,
    /// ou sur l'unité si sa portée est 0 ; une ligne part vers la cible alignée ; la zone doit toucher la cible et,
    /// hors confusion, aucun allié.
    /// </summary>
    private ActivateCommand? FindAttack(CombatSimulation sim, Unit unit, SkillDefinition skill, Unit target,
        IReadOnlyList<Cell> reachable)
    {
        List<Cell>? threats = null;
        ActivateCommand? best = null;
        var bestDistance = -1;
        foreach (var cell in reachable)
        {
            if (AttackFrom(sim, unit, skill, target, cell) is not { } use)
                continue;
            var command = new ActivateCommand(unit.Id, cell == unit.Position ? null : cell, use);
            if (mode != Mode.Gunner)
                return command;

            // Tireur : la case la plus loin de l'ennemi le plus proche (à égalité, la première dans l'ordre).
            threats ??= sim.Units
                .Where(e => CombatSimulation.AreOpponents(e, unit) && e.Status == UnitStatus.Active)
                .Select(e => e.Position)
                .ToList();
            var distance = threats.Count == 0 ? int.MaxValue : threats.Min(p => p.DistanceTo(cell));
            if (distance > bestDistance)
            {
                best = command;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>Au moins une case atteignable d'où <paramref name="skill"/> frappe <paramref name="target"/> (sans chercher la meilleure).</summary>
    private bool CanAttack(CombatSimulation sim, Unit unit, SkillDefinition skill, Unit target, IReadOnlyList<Cell> reachable) =>
        reachable.Any(cell => AttackFrom(sim, unit, skill, target, cell) is not null);

    /// <summary>Visée de <paramref name="skill"/> depuis <paramref name="cell"/> qui touche bien <paramref name="target"/>, ou <c>null</c>.</summary>
    private SkillUse? AttackFrom(CombatSimulation sim, Unit unit, SkillDefinition skill, Unit target, Cell cell)
    {
        SkillUse use;
        if (skill.Zone is null)
            use = new SkillUse(skill.Id, target.Id);
        else if (skill.Zone.Shape == ZoneShape.Line)
        {
            if (cell.DirectionTo(target.Position) is not { } step)
                return null;
            use = new SkillUse(skill.Id, null, new Cell(cell.X + step.X, cell.Y + step.Y));
        }
        else
            use = new SkillUse(skill.Id, null, skill.Range == 0 ? cell : target.Position);

        var action = sim.PlanAction(unit, cell, skill, use).Action;
        // Une charge peut percuter quelqu'un d'autre en chemin (ou personne) : on ne garde que celles qui
        // atteignent la cible.
        if (action is null || (skill.SelfMove == SelfMoveType.Charge && action.Target != target))
            return null;
        // Une zone doit vraiment toucher la cible (selon qui elle touche) ; hors confusion, sans prendre d'allié
        // (ni ses propres gadgets, pour une IEM).
        if (action.Zone is { } zone)
        {
            var affected = sim.UnitsAffectedBy(unit, skill, zone);
            if (!affected.Contains(target) || (!Confused && affected.Any(u => !u.IsCivilian && u.Team == unit.Team)))
                return null;
        }
        return use;
    }

    private static IReadOnlyList<Cell> ReachableCells(CombatSimulation sim, Unit unit) =>
        Pathfinder.ReachableCells(sim.Grid, unit.Position, unit.Reach, cell => sim.OccupantFor(unit, cell)).Keys.ToList();

    /// <summary>Distance à pied de chaque case jusqu'à <paramref name="target"/>, selon les règles de déplacement de l'unité.</summary>
    private static IReadOnlyDictionary<Cell, int> WalkingDistances(CombatSimulation sim, Unit unit, Unit target) =>
        Pathfinder.ReachableCells(sim.Grid, target.Position, int.MaxValue, cell => sim.OccupantFor(unit, cell));
}
