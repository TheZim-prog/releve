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
    /// Évite de prendre ses alliés dans ses zones. N'utilise pas encore les zones en ligne ni le soutien.
    /// </summary>
    public const string Basic = "basic";

    /// <summary>
    /// Balise de soin (gadget immobile) : jauge pleine, elle lance sa zone de soin autour d'elle, seulement si un allié
    /// blessé s'y trouve ; sinon elle attend.
    /// </summary>
    public const string Beacon = "beacon";

    /// <summary>
    /// Tireur : comme le comportement de base, mais tire depuis la case atteignable la plus éloignée de l'ennemi le
    /// plus proche (il recule si on l'approche), et utilise aussi ses zones en ligne (Tir appuyé).
    /// </summary>
    public const string Gunner = "gunner";

    internal static readonly IUnitBehavior BasicInstance = new BasicBehavior(BasicBehavior.Mode.Basic);
    private static readonly IUnitBehavior BeaconInstance = new BeaconBehavior();
    private static readonly IUnitBehavior GunnerInstance = new BasicBehavior(BasicBehavior.Mode.Gunner);

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
        Flee => FleeInstance,
        _ => null,
    };
}

/// <summary>
/// Fuite : son intention est de fuir (aucune compétence, elle-même pour cible) ; elle n'attaque pas et se rapproche à
/// pied du bord de carte le plus proche. Quitter le combat en l'atteignant est géré par la simulation.
/// </summary>
internal sealed class FleeBehavior : IUnitBehavior
{
    public Intention? ChooseIntention(CombatSimulation sim, Unit unit) => new(null, unit.Id);

    public ActivateCommand? Execute(CombatSimulation sim, Unit unit, Intention intention) => null;

    public ActivateCommand? Approach(CombatSimulation sim, Unit unit, Intention intention)
    {
        if (unit.Mobility == 0)
            return null;
        Occupant OccupantFor(Cell cell) => sim.OccupantFor(unit, cell);

        // Le bord le plus proche à pied, puis la case atteignable qui s'en rapproche le plus.
        var fromUnit = Pathfinder.ReachableCells(sim.Grid, unit.Position, int.MaxValue, OccupantFor);
        var exits = fromUnit.Where(c => sim.Grid.IsEdge(c.Key)).ToList();
        if (exits.Count == 0)
            return null;
        var exit = exits.MinBy(c => c.Value).Key;
        var toExit = Pathfinder.ReachableCells(sim.Grid, exit, int.MaxValue, OccupantFor);

        var best = Pathfinder.ReachableCells(sim.Grid, unit.Position, unit.Mobility, OccupantFor).Keys
            .Where(c => c != unit.Position && toExit.ContainsKey(c))
            .Select(c => (Cell: c, Distance: toExit[c]))
            .DefaultIfEmpty((Cell: unit.Position, Distance: int.MaxValue))
            .MinBy(c => c.Distance);
        return best.Distance < toExit.GetValueOrDefault(unit.Position, int.MaxValue)
            ? new ActivateCommand(unit.Id, best.Cell)
            : null;
    }
}

internal sealed class BeaconBehavior : IUnitBehavior
{
    /// <summary>Sa zone de soin, visée sur elle-même.</summary>
    public Intention? ChooseIntention(CombatSimulation sim, Unit unit) =>
        HealSkill(unit) is { } skill ? new Intention(skill.Id, unit.Id) : null;

    public ActivateCommand? Execute(CombatSimulation sim, Unit unit, Intention intention)
    {
        if (HealSkill(unit) is not { } skill || sim.CurrentTick < unit.CooldownEndTick(skill.Id))
            return null;
        var zone = skill.Zone!.Cells(sim.Grid, unit.Position, unit.Position);
        var someoneWounded = zone is not null && sim.UnitsAffectedBy(unit, skill, zone).Any(u => u.Hp < u.MaxHp);
        return someoneWounded ? new ActivateCommand(unit.Id, null, new SkillUse(skill.Id, null, unit.Position)) : null;
    }

    /// <summary>Immobile.</summary>
    public ActivateCommand? Approach(CombatSimulation sim, Unit unit, Intention intention) => null;

    private static SkillDefinition? HealSkill(Unit unit) =>
        unit.Skills.FirstOrDefault(s => s.Heals && s.Zone is { Affects: ZoneTargets.Allies });
}

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

    public Intention? ChooseIntention(CombatSimulation sim, Unit unit)
    {
        var enemies = sim.Units
            .Where(e => e != unit && e.Status == UnitStatus.Active && (Confused ? !e.IsCivilian : CombatSimulation.AreOpponents(e, unit)))
            .ToList();
        if (enemies.Count == 0)
            return null;

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
        if (sim.CurrentTick < unit.CooldownEndTick(skill.Id))
            return null;
        if (sim.FindUnit(intention.Target) is not { Status: UnitStatus.Active } target)
            return null;
        return FindAttack(sim, unit, skill, target, ReachableCells(sim, unit));
    }

    public ActivateCommand? Approach(CombatSimulation sim, Unit unit, Intention intention)
    {
        if (unit.Mobility == 0 || sim.FindUnit(intention.Target) is not { Status: UnitStatus.Active } target)
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
            .Where(s => sim.CurrentTick >= unit.CooldownEndTick(s.Id))
            .Where(s => s.IsOffensive && (mode == Mode.Gunner || s.Zone?.Shape != ZoneShape.Line) && !s.CreatesDome
                        && s.Summon is null && s.SelfMove is null or SelfMoveType.Rush)
            .OrderByDescending(s => s.MinDamage + s.MaxDamage)
            .ToList();

    /// <summary>
    /// L'ennemi le plus proche à pied ; s'il n'y a de chemin vers aucun, ou si l'unité est immobile (tourelle),
    /// le plus proche à vol d'oiseau.
    /// </summary>
    private static Unit Nearest(CombatSimulation sim, Unit unit, List<Unit> enemies)
    {
        if (unit.Mobility == 0)
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
        if (action is null || (skill.SelfMove == SelfMoveType.Rush && action.Target != target))
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
        Pathfinder.ReachableCells(sim.Grid, unit.Position, unit.Mobility, cell => sim.OccupantFor(unit, cell)).Keys.ToList();

    /// <summary>Distance à pied de chaque case jusqu'à <paramref name="target"/>, selon les règles de déplacement de l'unité.</summary>
    private static IReadOnlyDictionary<Cell, int> WalkingDistances(CombatSimulation sim, Unit unit, Unit target) =>
        Pathfinder.ReachableCells(sim.Grid, target.Position, int.MaxValue, cell => sim.OccupantFor(unit, cell));
}
