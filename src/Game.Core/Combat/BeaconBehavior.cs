namespace Game.Core.Combat;

/// <summary>
/// Balise (gadget immobile) : jauge pleine, elle lance sa zone de soutien autour d'elle, seulement si un allié en
/// profiterait (blessé pour un soin ; un héros sans PV temporaires pour une balise bouclier, pas un autre gadget) ;
/// sinon elle attend.
/// </summary>
internal sealed class BeaconBehavior : IUnitBehavior
{
    /// <summary>Sa zone de soutien, visée sur elle-même.</summary>
    public Intention? ChooseIntention(CombatSimulation sim, Unit unit) =>
        SupportSkill(unit) is { } skill ? new Intention(skill.Id, unit.Id) : null;

    public ActivateCommand? Execute(CombatSimulation sim, Unit unit, Intention intention)
    {
        if (SupportSkill(unit) is not { } skill || unit.UnavailableAt(skill, sim.CurrentTick) is not null)
            return null;
        var zone = skill.Zone!.Cells(sim.Grid, unit.Position, unit.Position);
        var useful = zone is not null && sim.UnitsAffectedBy(unit, skill, zone).Any(ally => Helps(skill, ally));
        return useful ? new ActivateCommand(unit.Id, null, new SkillUse(skill.Id, null, unit.Position)) : null;
    }

    /// <summary>Immobile.</summary>
    public ActivateCommand? Approach(CombatSimulation sim, Unit unit, Intention intention) => null;

    private static SkillDefinition? SupportSkill(Unit unit) =>
        unit.Skills.FirstOrDefault(s => s.Zone is { Affects: ZoneTargets.Allies } && (s.Heals || s.TempHp is not null));

    private static bool Helps(SkillDefinition skill, Unit ally) =>
        (skill.Heals && ally.Hp < ally.MaxHp) || (skill.TempHp is not null && ally.TempHp == 0 && ally.SummonedBy is null);
}
