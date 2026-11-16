using Game.Core.Grids;

namespace Game.Core.Combat;

// Réactions à une attaque : Garde du corps, Réflexes, gardes (Parade, Déviation, Maître d'armes), Riposte. Les
// réactions ne déclenchent pas de réactions (une riposte ne se pare pas, ne s'esquive pas et n'appelle pas de riposte).
public sealed partial class CombatSimulation
{
    // Vrai pendant qu'une réaction se résout (riposte, tir de surveillance, attaque interceptée).
    private bool _reacting;

    // Réactions qui attendent la fin de la source en cours (ripostes, tirs de Protection rapprochée), dans l'ordre.
    private readonly Queue<Action> _pendingReactions = new();

    /// <summary>
    /// Une attaque ciblée : offensive, sur une unité, pas en zone (une Percée ou un Saut, qui frappent ce qu'ils
    /// traversent ou ce qui les entoure, comptent comme des zones).
    /// </summary>
    private static bool IsTargetedAttack(SkillDefinition skill) =>
        skill.IsOffensive && skill.Zone is null && skill.SelfMove is not (SelfMoveType.Pierce or SelfMoveType.Leap);

    /// <summary>Une unité peut réagir : debout et pas étourdie.</summary>
    private static bool CanReact(Unit unit) => unit.Status == UnitStatus.Active && !unit.HasStatus(StatusKind.Stunned);

    /// <summary>
    /// Avant le coup de <paramref name="attacker"/> sur <paramref name="target"/> : la cible l'esquive (Réflexes) ou le
    /// pare (garde), sinon un allié Garde du corps le prend à sa place (le coup est résolu sur lui). Renvoie vrai si la
    /// cible ne le reçoit pas. Une attaque au contact évitée appelle quand même une riposte.
    /// </summary>
    private bool AvertedBeforeHit(Unit attacker, SkillDefinition skill, Unit target, int damageFactor, Cell? origin)
    {
        if (_reacting || attacker == target || !AreOpponents(attacker, target))
            return false;
        QueueProtectionShot(attacker, skill, target);
        if (!IsTargetedAttack(skill))
            return false;

        if (target.HasPassive(PassiveKind.Reflexes) && CanReact(target) && !target.SpentReactions.HasFlag(CycleReactions.Reflexes))
        {
            target.SpentReactions |= CycleReactions.Reflexes;
            _events.Add(new AttackDodged(CurrentTick, target.Id, attacker.Id));
            QueueRiposte(attacker, skill, target);
            return true;
        }
        if (target.Guard is { } held && CanReact(target)
            && held.Against == (IsRangedHit(attacker, skill, target) ? GuardAgainst.Ranged : GuardAgainst.Melee))
        {
            _events.Add(new AttackParried(CurrentTick, target.Id, attacker.Id));
            if (!held.BlocksAll)
                EndGuard(target);
            QueueRiposte(attacker, skill, target, byGuard: held.Ripostes);
            return true;
        }
        // Le garde du corps ne s'interpose que si le coup allait toucher : pas devant un dôme qui le bloquerait.
        if (DomeBlocking(skill, target) is null && Interceptor(target) is { } guard)
        {
            guard.SpentReactions |= CycleReactions.Bodyguard;
            _events.Add(new AttackIntercepted(CurrentTick, guard.Id, target.Id, attacker.Id));
            AsReaction(() => ResolveHit(attacker, skill, guard, damageFactor, origin: origin));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Garde du corps : l'allié debout, pas étourdi, au contact de la cible, qui a ce passif et ne l'a pas encore utilisé
    /// ce cycle (le premier dans l'ordre des unités). Il protège un héros, pas un gadget.
    /// </summary>
    private Unit? Interceptor(Unit target) =>
        target.SummonedBy is not null ? null
        : _units.FirstOrDefault(u => u != target && u.Team == target.Team && u.HasPassive(PassiveKind.Bodyguard) && CanReact(u)
                                     && !u.SpentReactions.HasFlag(CycleReactions.Bodyguard)
                                     && IsInMeleeReach(u, target.Position));

    /// <summary>
    /// Après une attaque ciblée au contact sur une unité qui riposte (passif Riposte, une fois par cycle, ou une garde qui
    /// riposte à chaque coup), la riposte attend la fin de la source.
    /// </summary>
    private void QueueRiposte(Unit attacker, SkillDefinition skill, Unit target, bool byGuard = false)
    {
        if (_reacting || IsRangedHit(attacker, skill, target) || !IsTargetedAttack(skill))
            return;
        if (!byGuard)
        {
            if (!target.HasPassive(PassiveKind.Riposte) || target.SpentReactions.HasFlag(CycleReactions.Riposte))
                return;
            target.SpentReactions |= CycleReactions.Riposte;
        }
        _pendingReactions.Enqueue(() => CounterAttack(target, attacker));
    }

    /// <summary>La riposte : l'attaque de base de l'unité, si elle peut encore réagir et que l'attaquant est debout à son contact.</summary>
    private void CounterAttack(Unit unit, Unit attacker)
    {
        if (!CanReact(unit) || attacker.Status != UnitStatus.Active || unit.BasicAttack is not { } basic
            || !IsInMeleeReach(unit, attacker.Position))
            return;
        _events.Add(new CounterAttack(CurrentTick, unit.Id, attacker.Id));
        ResolveHit(unit, basic, attacker, DamageFactorOf(unit), origin: unit.Position);
    }

    /// <summary>Résout une réaction, sans qu'elle en déclenche d'autres.</summary>
    private void AsReaction(Action reaction)
    {
        var was = _reacting;
        _reacting = true;
        reaction();
        _reacting = was;
    }

    /// <summary>Les réactions en attente partent, dans l'ordre, chacune comme une source à part.</summary>
    private void FlushReactions()
    {
        while (_pendingReactions.TryDequeue(out var reaction))
        {
            AsReaction(reaction);
            EndDomes();
        }
    }

    /// <summary>La compétence met son lanceur en garde.</summary>
    private void RaiseGuard(Unit unit, GuardDefinition guard)
    {
        unit.Guard = guard;
        unit.GuardEndTick = guard.DurationMs == 0 ? null : CurrentTick + Rules.MillisecondsToTicks(guard.DurationMs);
        _events.Add(new GuardRaised(CurrentTick, unit.Id, guard.Against, unit.GuardEndTick));
    }

    private void EndGuard(Unit unit)
    {
        if (unit.Guard is null)
            return;
        unit.Guard = null;
        unit.GuardEndTick = null;
        _events.Add(new GuardEnded(CurrentTick, unit.Id));
    }

    /// <summary>Début de tick : les gardes à durée arrivées à échéance tombent.</summary>
    private void ExpireGuards()
    {
        foreach (var unit in _units.Where(u => u.GuardEndTick <= CurrentTick))
            EndGuard(unit);
    }

    /// <summary>
    /// L'unité est mise hors de combat, ou change de camp : sa garde et sa surveillance tombent, ses réactions limitées
    /// sont rechargées (rien de ce qu'elle a préparé ne sert après).
    /// </summary>
    private void ClearReactions(Unit unit)
    {
        unit.SpentReactions = CycleReactions.None;
        EndGuard(unit);
        EndWatch(unit);
    }

    /// <summary>
    /// L'unité agit de nouveau : ses réactions limitées sont rechargées, sa garde sans durée tombe, sa surveillance aussi.
    /// </summary>
    private void ResetReactions(Unit unit)
    {
        unit.SpentReactions = CycleReactions.None;
        if (unit.Guard is { DurationMs: 0 })
            EndGuard(unit);
        EndWatch(unit);
    }
}
