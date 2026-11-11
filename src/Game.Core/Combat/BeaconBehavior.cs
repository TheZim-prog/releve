namespace Game.Core.Combat;

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
