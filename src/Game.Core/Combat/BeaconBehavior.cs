namespace Game.Core.Combat;

/// <summary>
/// Balise (gadget immobile) : jauge pleine, elle lance sa zone autour d'elle, seulement si elle sert : une zone de soutien
/// si un allié en profiterait (blessé pour un soin ; un héros sans PV temporaires pour une balise bouclier, pas un autre
/// gadget), une zone offensive (ex. l'Astre) si un adversaire s'y trouve ; sinon elle attend.
/// </summary>
internal sealed class BeaconBehavior : IUnitBehavior
{
    public bool ObeysTaunts => false;

    /// <summary>Sa zone, visée sur elle-même.</summary>
    public Intention? ChooseIntention(CombatSimulation sim, Unit unit) =>
        PulseSkill(unit) is { } skill ? new Intention(skill.Id, unit.Id) : null;

    public ActivateCommand? Execute(CombatSimulation sim, Unit unit, Intention intention)
    {
        if (PulseSkill(unit) is not { } skill || unit.UnavailableAt(skill, sim.CurrentTick) is not null)
            return null;
        var zone = skill.Zone!.Cells(sim.Grid, unit.Position, unit.Position);
        var useful = zone is not null && sim.UnitsAffectedBy(unit, skill, zone).Any(other => Helps(skill, unit, other));
        return useful ? new ActivateCommand(unit.Id, null, new SkillUse(skill.Id, null, unit.Position)) : null;
    }

    /// <summary>Immobile.</summary>
    public ActivateCommand? Approach(CombatSimulation sim, Unit unit, Intention intention) => null;

    /// <summary>Sa zone autour d'elle : de soutien (soin, PV temporaires) ou offensive.</summary>
    private static SkillDefinition? PulseSkill(Unit unit) =>
        unit.Skills.FirstOrDefault(s => s.Zone is not null && s.Range == 0
                                        && (s.Zone.Affects == ZoneTargets.Allies ? s.Heals || s.TempHp is not null : s.HasHitEffect));

    private static bool Helps(SkillDefinition skill, Unit unit, Unit other) =>
        skill.IsOffensive
            ? CombatSimulation.AreOpponents(unit, other)
            : (skill.Heals && other.Hp < other.MaxHp) || (skill.TempHp is not null && other.TempHp == 0 && other.SummonedBy is null);
}
