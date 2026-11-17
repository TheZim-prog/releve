using Game.Core.Grids;

namespace Game.Core.Combat;

// Déplacements forcés d'une cible : projection, renversement, tourniquet, vortex, pose sur une case, permutation. Aucun ne
// déclenche d'attaque d'opportunité, et tous interrompent une charge en cours. Un adversaire inébranlable ne bouge pas.
public sealed partial class CombatSimulation
{
    /// <summary>
    /// Applique le déplacement forcé d'une compétence à sa cible (projection, renversement, tourniquet, vortex,
    /// télékinésie, permutation) : c'est là que joue l'Impact du lanceur. Un sauvetage ou un transport (des alliés) n'y
    /// passe pas ; une compétence qui déplacerait un ennemi autrement devrait y passer. <paramref name="point"/> : la case de référence du déplacement : pour une pose, la
    /// case choisie ; pour un tourniquet, celle vers laquelle il lance ; pour un vortex, le centre vers lequel il attire
    /// (sinon le lanceur) ; pour une projection, d'où elle part (le centre de sa zone, sinon le lanceur).
    /// </summary>
    private void ApplyForcedMove(Unit caster, SkillDefinition skill, Unit target, ForcedMoveDefinition move, int damageFactor,
        Cell? point)
    {
        var before = target.Position;
        MoveByForce(caster, skill, target, move, damageFactor, point);
        ImpactOnArrival(caster, skill, target, before, damageFactor);
    }

    private void MoveByForce(Unit caster, SkillDefinition skill, Unit target, ForcedMoveDefinition move, int damageFactor,
        Cell? point)
    {
        switch (move.Type)
        {
            case ForcedMoveType.Push:
                Push(caster, skill, target, move, damageFactor, from: point ?? caster.Position);
                break;
            case ForcedMoveType.Throw:
                Push(caster, skill, target, move, damageFactor, from: caster.Position);
                break;
            case ForcedMoveType.Spin:
                Push(caster, skill, target, move, damageFactor, from: caster.Position, toward: point);
                break;
            case ForcedMoveType.Pull:
                Pull(caster, target, move, center: point ?? caster.Position);
                break;
            case ForcedMoveType.Place:
                if (ResistsForcedMove(caster, target))
                    break;
                Displace(target, point!.Value);
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
                ApplyForcedMove(caster, skill, unit, move, damageFactor, point: center);
        }
    }

    /// <summary>
    /// Inébranlable : la cible a ce passif et le déplacement vient d'un adversaire. Une structure (objet de terrain) ne
    /// bouge jamais. Elle reste où elle est.
    /// </summary>
    private bool ResistsForcedMove(Unit caster, Unit target)
    {
        if (!target.IsStructure && (!target.HasPassive(PassiveKind.Unshakable) || !AreOpponents(caster, target)))
            return false;
        _events.Add(new UnitResisted(CurrentTick, target.Id));
        return true;
    }

    /// <summary>
    /// Projection : pousse <paramref name="target"/> en ligne droite, loin de <paramref name="from"/> (la direction la
    /// plus proche parmi les 8), sur la distance prévue. Renversement : dans l'autre sens, par-dessus
    /// <paramref name="from"/>, la distance comptée depuis cette case. Tourniquet : de même, mais vers
    /// <paramref name="toward"/>. Elle s'arrête avant une unité, ou avant un mur, le bord ou une diagonale bloquée (même
    /// règle de coupe d'angle qu'un déplacement de la cible) ; une collision fait des dégâts à la cible et à l'unité
    /// percutée si elle est debout.
    /// </summary>
    private void Push(Unit caster, SkillDefinition skill, Unit target, ForcedMoveDefinition move, int damageFactor, Cell from,
        Cell? toward = null)
    {
        if (ResistsForcedMove(caster, target))
            return;
        var direction = move.Type switch
        {
            ForcedMoveType.Spin => from.NearestDirectionTo(toward!.Value),
            ForcedMoveType.Throw => from.NearestDirectionTo(target.Position) is { } away ? (-away.X, -away.Y) : null,
            _ => from.NearestDirectionTo(target.Position),
        };
        if (direction is not { } step)
            return;
        var start = move.Type == ForcedMoveType.Push ? target.Position : from;

        var (collided, obstacle) = Slide(target, start, move.Distance, _ => step);
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
    /// Vortex : chaque unité touchée encore debout est attirée vers <paramref name="center"/>, les plus proches d'abord
    /// (celles qui suivent s'arrêtent derrière elles).
    /// </summary>
    private void PullToCenter(Unit caster, SkillDefinition skill, IEnumerable<Unit> hit, ForcedMoveDefinition move, int damageFactor,
        Cell center)
    {
        foreach (var unit in hit.OrderBy(u => u.Position.DistanceTo(center)).ToList())
        {
            if (unit.Status == UnitStatus.Active)
                ApplyForcedMove(caster, skill, unit, move, damageFactor, center);
        }
    }

    /// <summary>
    /// Attire <paramref name="target"/> vers <paramref name="center"/>, sans collision. Un adversaire inébranlable ne
    /// bouge pas (il ne « résiste » que s'il avait à bouger).
    /// </summary>
    private void Pull(Unit caster, Unit target, ForcedMoveDefinition move, Cell center)
    {
        if (target.Position != center && !ResistsForcedMove(caster, target))
            Slide(target, target.Position, move.Distance, position => position.NearestDirectionTo(center));
    }

    /// <summary>
    /// Fait glisser <paramref name="target"/> (posée sur <paramref name="start"/> ou partie de là) sur
    /// <paramref name="distance"/> cases au plus, chaque pas dans la direction que donne <paramref name="direction"/>
    /// depuis la case atteinte (<c>null</c> : elle s'arrête là). Elle s'arrête avant une unité, ou avant un mur, le bord
    /// ou une diagonale bloquée (même règle de coupe d'angle qu'un déplacement de la cible), et sur un piège ou un sol
    /// qui fige. Renvoie si elle a heurté quelque chose, et l'unité heurtée.
    /// </summary>
    private (bool Collided, Unit? Obstacle) Slide(Unit target, Cell start, int distance, Func<Cell, (int X, int Y)?> direction)
    {
        var position = start;
        var collided = false;
        Unit? obstacle = null;
        var crossed = new List<Cell>();
        for (var i = 0; i < distance && direction(position) is { } step; i++)
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
            crossed.Add(next);
            position = next;
            // Un piège ou un sol qui fige sur cette case : la course s'arrête là (sans collision).
            if (GroundHalts(target, crossed.Count > 1 ? crossed[^2] : start, next))
                break;
        }

        if (position != start)
            Displace(target, position, crossed, departure: start);
        return (collided, obstacle);
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
        if (!IsBlockedByDome(skill, hit, harmful: true))
            UnblockedSecondaryDamage(caster, skill, hit, minDamage, maxDamage, damageFactor, fallbackCell: null);
    }

    /// <summary>
    /// Les mêmes dégâts, une fois le dôme passé. <paramref name="fallbackCell"/> : où tombe la victime si elle partage sa case.
    /// </summary>
    private void UnblockedSecondaryDamage(Unit caster, SkillDefinition skill, Unit hit, int minDamage, int maxDamage,
        int damageFactor, Cell? fallbackCell)
    {
        var wereOpponents = AreOpponents(hit, caster);
        var lost = Damage(hit, Scaled(_random.NextInt(minDamage, maxDamage), damageFactor, hit), fallbackCell,
            ignoresProtections: skill.IgnoresProtections);
        GaugePassivesOnHit(caster, hit, lost, wereOpponents);
        ChainReaction(caster, hit, wereOpponents);
    }

    /// <summary>
    /// Le lanceur d'un mouvement spécial se déplace (sans attaque d'opportunité) : comme un déplacement forcé, plus les
    /// surveillances de zone qu'il croise en chemin (<paramref name="course"/>, ou seulement l'arrivée).
    /// </summary>
    private void SelfDisplace(Unit unit, Cell to, IReadOnlyList<Cell>? course = null)
    {
        var from = unit.Position;
        Displace(unit, to, course);
        TriggerZoneWatchesAlong(unit, from, course ?? new[] { to });
    }

    /// <summary>Permutation : les deux unités échangent leurs cases.</summary>
    private void Swap(Unit caster, Unit target)
    {
        var (mine, theirs) = (caster.Position, target.Position);
        Displace(caster, theirs);
        Displace(target, mine);
    }

    /// <summary>
    /// Déplacement forcé (sans chemin ni attaque d'opportunité) ; il interrompt une charge en cours, et une unité debout
    /// qui arrive ainsi sur un piège ou un sol gelé y entre comme à pied. <paramref name="course"/> : les cases qu'elle
    /// parcourt au sol pour y arriver, arrivée comprise (poussée, Charge) ; sans elle, seule l'arrivée compte (elle est
    /// posée, elle saute ou elle file). <paramref name="departure"/> : d'où part cette course, si ce n'est pas sa case
    /// (lancée par-dessus le lanceur, elle part de la case de celui-ci).
    /// </summary>
    private void Displace(Unit unit, Cell to, IReadOnlyList<Cell>? course = null, Cell? departure = null)
    {
        var from = unit.Position;
        unit.Position = to;
        _events.Add(new UnitDisplaced(CurrentTick, unit.Id, from, to));
        InterruptCharge(unit);
        var previous = departure ?? from;
        foreach (var cell in course ?? new[] { to })
        {
            EnterGround(unit, previous, cell, fallbackCell: null);
            previous = cell;
        }
    }
}
