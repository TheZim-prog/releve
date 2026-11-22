using Game.Core.Grids;

namespace Game.Core.Combat;

// Surveillances (Tir de couverture, Protection rapprochée) : un tir préparé, qui part sur le premier ennemi qui bouge ou
// agit dans la zone surveillée, ou qui attaque l'allié protégé.
public sealed partial class CombatSimulation
{
    private readonly List<WatchState> _watches = new();

    /// <summary>Surveillances en cours, dans l'ordre où elles ont été posées.</summary>
    public IReadOnlyList<WatchState> Watches => _watches;

    /// <summary>La compétence surveille ses cases, ou l'allié visé, jusqu'à la prochaine action du lanceur.</summary>
    private void SetWatch(Unit owner, SkillDefinition skill, IReadOnlyList<Cell>? cells, Unit? ally)
    {
        EndWatch(owner);
        _watches.Add(new WatchState(owner.Id, skill, cells, ally?.Id));
        _events.Add(new WatchSet(CurrentTick, owner.Id, skill.Id, cells, ally?.Id));
    }

    private void EndWatch(Unit owner)
    {
        if (_watches.RemoveAll(w => w.Owner == owner.Id) > 0)
            _events.Add(new WatchEnded(CurrentTick, owner.Id));
    }

    /// <summary>Y a-t-il une surveillance de zone d'un adversaire sur l'une de ces deux cases ?</summary>
    private bool HasZoneWatchOn(Unit unit, Cell from, Cell cell) => _watches.Count > 0 && ZoneWatchesOn(unit, from, cell).Any();

    private IEnumerable<WatchState> ZoneWatchesOn(Unit unit, Cell from, Cell cell) =>
        _watches.Where(w => (w.Covers(cell) || w.Covers(from)) && AreOpponents(FindUnit(w.Owner)!, unit));

    /// <summary>
    /// Un ennemi bouge (un pas qui part d'une case surveillée ou y arrive) ou va agir sur <paramref name="cell"/> : les
    /// surveillances de zone de ses adversaires peuvent tirer. Chacune est une source à part. <paramref name="fallbackCell"/> :
    /// où il tombe s'il est à terre sur une case qu'il partage (en traversant un allié).
    /// </summary>
    private void TriggerZoneWatches(Unit unit, Cell from, Cell cell, Cell? fallbackCell = null)
    {
        if (_reacting)
            return;
        foreach (var watch in ZoneWatchesOn(unit, from, cell).ToList())
        {
            if (unit.Status != UnitStatus.Active)
                return;
            Fire(watch, unit, fallbackCell);
            EndSource();
        }
    }

    /// <summary>
    /// Le lanceur d'un mouvement spécial vient de parcourir <paramref name="cells"/> depuis <paramref name="from"/> : chaque
    /// pas peut déclencher une surveillance de zone, comme à pied.
    /// </summary>
    private void TriggerZoneWatchesAlong(Unit unit, Cell from, IReadOnlyList<Cell> cells)
    {
        if (_watches.Count == 0)
            return;
        var previous = from;
        foreach (var cell in cells)
        {
            if (unit.Status != UnitStatus.Active)
                return;
            if (HasZoneWatchOn(unit, previous, cell))
                TriggerZoneWatches(unit, previous, cell);
            previous = cell;
        }
    }

    /// <summary>
    /// Protection rapprochée : un adversaire attaque l'allié protégé ; le tir attend la fin de la source. Pas contre le
    /// décor qui s'effondre (une structure), qui n'est pas un ennemi à abattre.
    /// </summary>
    private void QueueProtectionShot(Unit attacker, SkillDefinition skill, Unit target)
    {
        if (_watches.Count == 0 || !skill.IsOffensive || attacker.IsStructure)
            return;
        foreach (var watch in _watches.Where(w => w.Ally == target.Id && AreOpponents(FindUnit(w.Owner)!, attacker)).ToList())
            _pendingReactions.Enqueue(() => Fire(watch, attacker));
    }

    /// <summary>
    /// La surveillance tire sur l'ennemi, si son lanceur peut réagir et l'a à portée et en vue ; elle prend fin alors.
    /// Sinon elle continue de surveiller (elle a pu être levée entre-temps : rien ne se passe).
    /// </summary>
    private void Fire(WatchState watch, Unit enemy, Cell? fallbackCell = null)
    {
        var owner = FindUnit(watch.Owner)!;
        var shot = watch.Skill.Watch!.ShotOf(watch.Skill);
        if (!_watches.Contains(watch) || !CanReact(owner) || enemy.Status != UnitStatus.Active
            || CheckTarget(owner, owner.Position, enemy, shot) is not null)
            return;
        _watches.Remove(watch);
        _events.Add(new WatchFired(CurrentTick, owner.Id, enemy.Id));
        _events.Add(new WatchEnded(CurrentTick, owner.Id));
        AsReaction(() => ResolveHit(owner, shot, enemy, DamageFactorOf(owner), fallbackCell, origin: owner.Position));
    }
}
