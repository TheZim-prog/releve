using Game.Core.Grids;

namespace Game.Core.Combat;

// Déplacements forcés d'une cible : projection, renversement, pose sur une case, permutation. Aucun ne déclenche
// d'attaque d'opportunité, et tous interrompent une charge en cours.
public sealed partial class CombatSimulation
{
    /// <summary>
    /// Applique le déplacement forcé d'une compétence à sa cible. <paramref name="destination"/> : la case choisie,
    /// pour une pose. Une projection part du lanceur ; celle d'une zone, de son centre (voir <see cref="PushFromZone"/>).
    /// </summary>
    private void ApplyForcedMove(Unit caster, SkillDefinition skill, Unit target, ForcedMoveDefinition move, int damageFactor,
        Cell? destination)
    {
        switch (move.Type)
        {
            case ForcedMoveType.Push or ForcedMoveType.Throw:
                Push(caster, skill, target, move, damageFactor, from: caster.Position);
                break;
            case ForcedMoveType.Place:
                Displace(target, destination!.Value);
                LandingImpact(caster, skill, target, move, damageFactor);
                break;
            case ForcedMoveType.Swap:
                Swap(caster, target);
                break;
            default:
                throw new InvalidOperationException($"Unknown forced move {move.Type}.");
        }
    }

    /// <summary>
    /// Projection d'une zone : chaque unité touchée encore debout est repoussée depuis <paramref name="center"/>, les
    /// plus éloignées d'abord (elles libèrent la place de celles qui suivent).
    /// </summary>
    private void PushFromZone(Unit caster, SkillDefinition skill, IEnumerable<Unit> hit, ForcedMoveDefinition move, int damageFactor,
        Cell center)
    {
        foreach (var unit in hit.OrderByDescending(u => u.Position.DistanceTo(center)).ToList())
        {
            if (unit.Status == UnitStatus.Active)
                Push(caster, skill, unit, move, damageFactor, center);
        }
    }

    /// <summary>
    /// Projection : pousse <paramref name="target"/> en ligne droite, loin de <paramref name="from"/> (la direction la
    /// plus proche parmi les 8), sur la distance prévue. Renversement : dans l'autre sens, par-dessus
    /// <paramref name="from"/>, la distance comptée depuis cette case. Elle s'arrête avant une unité, ou avant un mur, le
    /// bord ou une diagonale bloquée (même règle de coupe d'angle qu'un déplacement de la cible) ; une collision fait
    /// des dégâts à la cible et à l'unité percutée si elle est debout.
    /// </summary>
    private void Push(Unit caster, SkillDefinition skill, Unit target, ForcedMoveDefinition move, int damageFactor, Cell from)
    {
        if (from.NearestDirectionTo(target.Position) is not { } away)
            return;
        var thrown = move.Type == ForcedMoveType.Throw;
        var step = thrown ? (X: -away.X, Y: -away.Y) : away;
        var start = thrown ? from : target.Position;

        var position = start;
        var collided = false;
        Unit? obstacle = null;
        for (var i = 0; i < move.Distance; i++)
        {
            var next = new Cell(position.X + step.X, position.Y + step.Y);
            // Le décor (ou un coin bouché) arrête d'abord : on ne percute pas une unité à travers un coin de mur.
            if (!CanStepIgnoringDestination(target, position, next, downedBlockCorners: true))
            {
                collided = true;
                break;
            }
            if (UnitAt(next) is { } other && other != target)
            {
                collided = true;
                obstacle = other;
                break;
            }
            position = next;
        }

        if (position != start)
            Displace(target, position);
        if (!collided)
            return;

        _events.Add(new UnitCollided(CurrentTick, target.Id, obstacle?.Id));
        if (move.CollisionMaxDamage == 0)
            return;
        // La cible (projetée sous un dôme de son équipe) ou l'unité percutée peuvent être protégées : la collision
        // fait partie de la même source. Un civil percuté n'est pas blessé (seules les zones les touchent).
        foreach (var hit in new[] { target, obstacle })
        {
            if (hit is { Status: UnitStatus.Active, IsCivilian: false })
                SecondaryDamage(caster, skill, hit, move.CollisionMinDamage, move.CollisionMaxDamage, damageFactor);
        }
    }

    /// <summary>
    /// Atterrissage d'une unité posée (ex. Boulet) : les adversaires du lanceur debout au contact de sa case d'arrivée
    /// prennent les dégâts d'impact.
    /// </summary>
    private void LandingImpact(Unit caster, SkillDefinition skill, Unit landed, ForcedMoveDefinition move, int damageFactor)
    {
        if (move.ImpactMaxDamage == 0)
            return;
        var center = landed.Position;
        foreach (var hit in _units.Where(u => u != landed && u.Status == UnitStatus.Active && AreOpponents(u, caster)
                                              && IsInMeleeReach(center, u.Position)).ToList())
            SecondaryDamage(caster, skill, hit, move.ImpactMinDamage, move.ImpactMaxDamage, damageFactor);
    }

    /// <summary>
    /// Dégâts qu'une compétence fait en plus de son coup (collision, impact) : un tirage multiplié par la puissance du
    /// lanceur, que le dôme de la victime bloque comme le reste de la source ; les passifs du lanceur comptent.
    /// </summary>
    private void SecondaryDamage(Unit caster, SkillDefinition skill, Unit hit, int minDamage, int maxDamage, int damageFactor)
    {
        if (IsBlockedByDome(skill, hit, harmful: true))
            return;
        var wereOpponents = AreOpponents(hit, caster);
        var lost = Damage(hit, Scaled(_random.NextInt(minDamage, maxDamage), damageFactor, hit), fallbackCell: null,
            ignoresProtections: skill.IgnoresProtections);
        GaugePassivesOnHit(caster, hit, lost, wereOpponents);
        ChainReaction(caster, hit, wereOpponents);
    }

    /// <summary>Permutation : les deux unités échangent leurs cases.</summary>
    private void Swap(Unit caster, Unit target)
    {
        var (mine, theirs) = (caster.Position, target.Position);
        Displace(caster, theirs);
        Displace(target, mine);
    }

    /// <summary>Déplacement forcé (sans chemin ni attaque d'opportunité) ; il interrompt une charge en cours.</summary>
    private void Displace(Unit unit, Cell to)
    {
        var from = unit.Position;
        unit.Position = to;
        _events.Add(new UnitDisplaced(CurrentTick, unit.Id, from, to));
        InterruptCharge(unit);
    }
}
