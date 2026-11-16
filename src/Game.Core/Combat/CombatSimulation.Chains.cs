using Game.Core.Grids;

namespace Game.Core.Combat;

// Chaînes (Frappe en chaîne, Ricochet, Chaîne d'éclairs) : le coup rebondit d'une cible à la plus proche suivante.
public sealed partial class CombatSimulation
{
    /// <summary>Un maillon d'une chaîne : la cible, et la case d'où le lanceur la frappe.</summary>
    internal sealed record ChainLink(Unit Target, Cell From);

    /// <summary>
    /// Chaîne, depuis <paramref name="position"/> : une première cible (à pied, la case libre la plus proche à son
    /// contact à <see cref="SkillDefinition.Range"/> pas au plus ; sinon à portée et en vue), puis les rebonds, calculés
    /// tout de suite : la chaîne entière s'affiche avant de valider. <see cref="PlannedAction.Destination"/> : où finit le lanceur.
    /// </summary>
    private (PlannedAction? Action, RejectionReason? Reason) PlanChain(Unit unit, Cell position, SkillDefinition skill, SkillUse use)
    {
        if (StruckEnemy(unit, skill, use, wantsCell: false) is not { } first)
            return (null, RejectionReason.InvalidTarget);

        var onFoot = skill.SelfMove == SelfMoveType.Dash;
        Cell from;
        if (onFoot)
        {
            if (DashEnd(unit, position, first, skill.Range) is not { } end)
                return (null, RejectionReason.OutOfRange);
            from = end;
        }
        else
        {
            if (CheckTarget(unit, position, first, skill) is { } reason)
                return (null, reason);
            from = position;
        }

        var links = new List<ChainLink> { new(first, from) };
        while (links.Count < skill.Chain!.MaxTargets && NextLink(unit, skill, links, onFoot) is { } next)
            links.Add(next);

        var destination = onFoot && !skill.MoveOptions.ReturnsToStart ? links[^1].From : position;
        return (new PlannedAction(skill, first, null, destination, Chain: links), null);
    }

    /// <summary>
    /// Le rebond suivant : l'unité debout du camp de la première cible (des ennemis, sauf pour un lanceur confus), la plus
    /// proche de la dernière cible (à égalité, dans l'ordre des unités), pas encore touchée, à
    /// <see cref="ChainDefinition.BounceRange"/> cases au plus d'elle, et joignable : à pied, une case à son contact à
    /// autant de pas au plus ; sinon, une ligne depuis la dernière cible que ni mur ni zone au sol (fumée, pare-tirs) ne coupe.
    /// </summary>
    private ChainLink? NextLink(Unit unit, SkillDefinition skill, List<ChainLink> links, bool onFoot)
    {
        var last = links[^1];
        var side = links[0].Target.Team;
        var bounce = skill.Chain!.BounceRange;
        var candidates = _units
            .Where(u => u.Status == UnitStatus.Active && u != unit && u.Team == side && !u.IsCivilian
                        && links.All(l => l.Target != u) && u.Position.DistanceTo(last.Target.Position) <= bounce)
            .OrderBy(u => u.Position.DistanceTo(last.Target.Position))
            .ToList();
        if (candidates.Count == 0)
            return null;

        if (!onFoot)
        {
            var reached = candidates.FirstOrDefault(u => LineOfSight.IsClear(_grid, last.Target.Position, u.Position)
                                                         && !GroundBlocksLine(last.Target.Position, u.Position, skill));
            return reached is null ? null : new ChainLink(reached, last.From);
        }

        // Une seule recherche de chemins depuis la case où il se tient.
        var walkable = WalkableWithin(unit, last.From, bounce);
        foreach (var candidate in candidates)
        {
            if (DashEndIn(walkable, candidate) is { } end)
                return new ChainLink(candidate, end);
        }
        return null;
    }

    /// <summary>
    /// La chaîne prend effet, maillon par maillon (sans attaque d'opportunité) : à pied, le lanceur va au contact de la
    /// cible, puis frappe. Une cible tombée entre-temps n'est pas refrappée. À pied, tombé, étourdi ou immobilisé en
    /// chemin (un piège, un sol gelé), il s'arrête là. Ricochet : il revient ensuite à sa case de départ.
    /// </summary>
    private void ExecuteChain(Unit caster, SkillDefinition skill, IReadOnlyList<ChainLink> chain, int damageFactor)
    {
        var start = caster.Position;
        var onFoot = skill.SelfMove == SelfMoveType.Dash;
        _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, chain[0].Target.Id));
        Cell? origin = caster.Position;
        for (var i = 0; i < chain.Count; i++)
        {
            var link = chain[i];
            if (onFoot && caster.Position != link.From)
            {
                if (!caster.CanMove)
                    return;
                SelfDisplace(caster, link.From);
            }
            if (!CanStillAct(caster))
                return;
            if (i > 0)
                _events.Add(new ChainBounced(CurrentTick, caster.Id, chain[i - 1].Target.Id, link.Target.Id));
            // À distance, le coup part du lanceur, puis de la cible précédente (pour la couverture).
            ResolveHit(caster, skill, link.Target, damageFactor, origin: onFoot ? null : origin);
            origin = link.Target.Position;
        }
        if (onFoot && skill.MoveOptions.ReturnsToStart)
            ReturnToStart(caster, start);
    }
}
