using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>
/// Comportement d'une unité IA. Il ne décide que quoi faire : ses commandes passent par la même validation
/// que celles des joueurs. Les règles communes (quand fixer l'intention, quand la changer) sont dans la simulation.
/// </summary>
internal interface IUnitBehavior
{
    /// <summary>Nouvelle intention, ou <c>null</c> s'il n'y a plus d'ennemi.</summary>
    Intention? ChooseIntention(CombatSimulation sim, Unit unit);

    /// <summary>Activation qui exécute l'intention (déplacement compris), ou <c>null</c> si elle est devenue impossible.</summary>
    ActivateCommand? Execute(CombatSimulation sim, Unit unit, Intention intention);

    /// <summary>Déplacement seul vers la cible de l'intention, ou <c>null</c> si rien ne rapproche.</summary>
    ActivateCommand? Approach(CombatSimulation sim, Unit unit, Intention intention);
}

/// <summary>Comportements d'IA connus, désignés par leur identifiant dans les données.</summary>
public static class Behaviors
{
    /// <summary>
    /// Comportement de base (docs/design.md) : vise l'ennemi le plus fragile qu'il peut frapper à son prochain tour,
    /// avec la compétence la plus forte qui l'atteint ; sinon se rapproche de l'ennemi le plus proche à pied.
    /// Évite de prendre ses alliés dans ses zones. Les zones en ligne ne sont pas encore gérées.
    /// </summary>
    public const string Basic = "basic";

    private static readonly IUnitBehavior BasicInstance = new BasicBehavior();

    public static bool IsKnown(string id) => Find(id) is not null;

    internal static IUnitBehavior? Find(string id) => id switch
    {
        Basic => BasicInstance,
        _ => null,
    };
}

internal sealed class BasicBehavior : IUnitBehavior
{
    public Intention? ChooseIntention(CombatSimulation sim, Unit unit)
    {
        var enemies = sim.Units.Where(e => e.Team != unit.Team && e.Status == UnitStatus.Active).ToList();
        if (enemies.Count == 0)
            return null;

        var skills = UsableSkills(sim, unit);
        var reachable = ReachableCells(sim, unit);

        // L'ennemi le plus fragile qu'au moins une compétence atteint (OrderBy est stable : à égalité, l'ordre
        // des unités), puis la plus forte compétence qui l'atteint.
        foreach (var target in enemies.OrderBy(e => e.Hp))
        {
            var skill = skills.FirstOrDefault(s => FindAttack(sim, unit, s, target, reachable) is not null);
            if (skill is not null)
                return new Intention(skill.Id, target.Id);
        }

        return new Intention(skills.FirstOrDefault()?.Id, Nearest(sim, unit, enemies).Id);
    }

    public ActivateCommand? Execute(CombatSimulation sim, Unit unit, Intention intention)
    {
        if (intention.SkillId is null || unit.FindSkill(intention.SkillId) is not { } skill)
            return null;
        if (sim.CurrentTick < unit.CooldownEndTick(skill.Id))
            return null;
        if (sim.FindUnit(intention.Target) is not { Status: UnitStatus.Active } target)
            return null;
        return FindAttack(sim, unit, skill, target, ReachableCells(sim, unit));
    }

    public ActivateCommand? Approach(CombatSimulation sim, Unit unit, Intention intention)
    {
        if (sim.FindUnit(intention.Target) is not { Status: UnitStatus.Active } target)
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

    /// <summary>Compétences hors recharge, de la plus forte à la plus faible (dégâts moyens ; à égalité, ordre des données).</summary>
    private static List<SkillDefinition> UsableSkills(CombatSimulation sim, Unit unit) =>
        unit.Skills
            .Where(s => sim.CurrentTick >= unit.CooldownEndTick(s.Id) && s.Zone?.Shape != ZoneShape.Line)
            .OrderByDescending(s => s.MinDamage + s.MaxDamage)
            .ToList();

    /// <summary>L'ennemi le plus proche à pied ; s'il n'y a de chemin vers aucun, le plus proche à vol d'oiseau.</summary>
    private static Unit Nearest(CombatSimulation sim, Unit unit, List<Unit> enemies)
    {
        var byWalking = enemies
            .Select(e => (Enemy: e, Distance: WalkingDistances(sim, unit, e).GetValueOrDefault(unit.Position, int.MaxValue)))
            .MinBy(e => e.Distance);
        return byWalking.Distance < int.MaxValue
            ? byWalking.Enemy
            : enemies.MinBy(e => e.Position.DistanceTo(unit.Position))!;
    }

    /// <summary>
    /// Activation qui frappe <paramref name="target"/> avec <paramref name="skill"/> en bougeant le moins possible
    /// (cases atteignables par distance croissante, la case actuelle d'abord), ou <c>null</c>. Une zone carrée est
    /// centrée sur la cible, ou sur l'unité si sa portée est 0 ; elle doit toucher la cible et aucun allié.
    /// </summary>
    private static ActivateCommand? FindAttack(CombatSimulation sim, Unit unit, SkillDefinition skill, Unit target,
        IReadOnlyList<Cell> reachable)
    {
        foreach (var cell in reachable)
        {
            var use = skill.Zone is null
                ? new SkillUse(skill.Id, target.Id)
                : new SkillUse(skill.Id, null, skill.Range == 0 ? cell : target.Position);
            var action = sim.PlanAction(unit, cell, skill, use).Action;
            if (action is null)
                continue;
            if (action.Zone is { } zone
                && (!zone.Contains(target.Position) || (skill.Zone!.Affects == ZoneTargets.All && HitsAnAlly(sim, unit, zone))))
                continue;
            return new ActivateCommand(unit.Id, cell == unit.Position ? null : cell, use);
        }
        return null;
    }

    private static bool HitsAnAlly(CombatSimulation sim, Unit unit, IReadOnlyList<Cell> zone) =>
        sim.Units.Any(u => u != unit && u.Team == unit.Team && u.Status == UnitStatus.Active && zone.Contains(u.Position));

    private static IReadOnlyList<Cell> ReachableCells(CombatSimulation sim, Unit unit) =>
        Pathfinder.ReachableCells(sim.Grid, unit.Position, unit.Mobility, cell => sim.OccupantFor(unit, cell)).Keys.ToList();

    /// <summary>Distance à pied de chaque case jusqu'à <paramref name="target"/>, selon les règles de déplacement de l'unité.</summary>
    private static IReadOnlyDictionary<Cell, int> WalkingDistances(CombatSimulation sim, Unit unit, Unit target) =>
        Pathfinder.ReachableCells(sim.Grid, target.Position, int.MaxValue, cell => sim.OccupantFor(unit, cell));
}
