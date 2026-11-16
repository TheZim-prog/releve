using Game.Core.Grids;

namespace Game.Core.Combat;

// Passifs du télépathe : Contagion, Impact, Présence, Ange gardien. (Emprise est dans CombatSimulation.Statuses.cs,
// Écran cinétique dans CombatSimulation.Passives.cs.)
public sealed partial class CombatSimulation
{
    /// <summary>États qu'une Contagion fait sauter : la paralysie (étourdi) et la confusion.</summary>
    private static readonly StatusKind[] ContagiousKinds = { StatusKind.Stunned, StatusKind.Confused };

    /// <summary>
    /// Contagion, avant la chute de <paramref name="fallen"/> : ses états contagieux posés par un adversaire debout qui a
    /// ce passif, avec leur fin et cet adversaire.
    /// </summary>
    private List<(StatusKind Status, int EndTick, Unit Source)> ContagiousStatuses(Unit fallen)
    {
        var contagious = new List<(StatusKind, int, Unit)>();
        foreach (var status in ContagiousKinds)
        {
            if (fallen.StatusEndTicks.TryGetValue(status, out var endTick) && fallen.StatusSource(status) is { } sourceId
                && FindUnit(sourceId) is { Status: UnitStatus.Active } source && source.HasPassive(PassiveKind.Contagion)
                && AreOpponents(fallen, source))
                contagious.Add((status, endTick, source));
        }
        return contagious;
    }

    /// <summary>
    /// Contagion, après la chute : chaque état saute, pour sa durée restante, sur l'adversaire du télépathe debout le plus
    /// proche de la case où elle est tombée (à égalité, dans l'ordre des unités) : pas un gadget, ni un robot (un pouvoir
    /// mental). Un ennemi serein y résiste ; un ennemi qui l'a déjà pour plus longtemps le garde tel quel : dans les deux
    /// cas, l'état se perd.
    /// </summary>
    private void SpreadContagion(Unit fallen, List<(StatusKind Status, int EndTick, Unit Source)> contagious)
    {
        foreach (var (status, endTick, source) in contagious)
        {
            var next = _units
                .Where(u => u != fallen && u.IsStandingCombatant && AreOpponents(u, source) && !u.Definition.IsRobot)
                .OrderBy(u => u.Position.DistanceTo(fallen.Position))
                .FirstOrDefault();
            if (next is null || (next.StatusEndTicks.TryGetValue(status, out var current) && current > endTick))
                continue;
            if (ResistsStatus(next, status))
            {
                _events.Add(new UnitResisted(CurrentTick, next.Id));
                continue;
            }
            _events.Add(new StatusSpread(CurrentTick, fallen.Id, next.Id, status));
            PutStatus(next, status, endTick, source);
        }
    }

    /// <summary>
    /// Impact : un adversaire que le lanceur (qui a ce passif) vient de déplacer de force, encore debout et parti de
    /// <paramref name="before"/>, prend des dégâts à l'arrivée (collision comprise, ils s'ajoutent).
    /// </summary>
    private void ImpactOnArrival(Unit caster, SkillDefinition skill, Unit target, Cell before, int damageFactor)
    {
        if (caster.HasPassive(PassiveKind.Impact) && target.Status == UnitStatus.Active && target.Position != before
            && AreOpponents(caster, target))
            SecondaryDamage(caster, skill, target, Rules.ImpactMinDamage, Rules.ImpactMaxDamage, damageFactor);
    }

    /// <summary>
    /// Présence : après chacune de ses actions, ses alliés blessés à portée récupèrent un peu de PV (multipliés par sa
    /// puissance, comme un soin) : des héros debout, pas lui-même, ni un gadget, ni une unité dominée.
    /// </summary>
    private void Presence(Unit unit)
    {
        if (unit.Status != UnitStatus.Active || !unit.HasPassive(PassiveKind.Presence))
            return;
        foreach (var ally in _units.Where(u => IsHeroAllyOf(unit, u) && u.Hp < u.MaxHp
                                              && IsNear(unit, u, PassiveKind.Presence, Rules.PresenceRange)).ToList())
            Heal(ally, unit.HealWith(_random.NextInt(Rules.PresenceMinHeal, Rules.PresenceMaxHeal)));
    }

    /// <summary>
    /// Ange gardien : le télépathe debout, de l'équipe de <paramref name="falling"/> et à portée, qui n'a pas encore servi
    /// dans ce combat et la retiendrait à 1 PV ; <c>null</c> s'il n'y en a pas.
    /// </summary>
    private Unit? GuardianAngelFor(Unit falling) =>
        _units.FirstOrDefault(u => u.Status == UnitStatus.Active && !u.GuardianAngelUsed && IsHeroAllyOf(u, falling)
                                   && IsNear(u, falling, PassiveKind.GuardianAngel, Rules.GuardianAngelRange));

    private void SavedByGuardianAngel(Unit unit, Unit angel)
    {
        angel.GuardianAngelUsed = true;
        _events.Add(new GuardianAngelSaved(CurrentTick, unit.Id, angel.Id));
    }

    /// <summary>
    /// Un allié que soutient le télépathe (Siphon, Présence, Ange gardien) : un héros de son équipe, debout, autre que
    /// lui, ni gadget ni unité dominée (un ennemi passé de son côté pour un temps). Dominé lui-même, il ne soutient
    /// personne (ni ses anciens alliés, ni ses maîtres du moment).
    /// </summary>
    private static bool IsHeroAllyOf(Unit bearer, Unit unit) =>
        bearer.DominatedBy is null && unit != bearer && unit.IsStandingCombatant && unit.Team == bearer.Team && unit.DominatedBy is null;
}
