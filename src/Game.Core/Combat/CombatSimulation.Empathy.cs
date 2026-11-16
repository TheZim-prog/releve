using Game.Core.Grids;

namespace Game.Core.Combat;

// Soutien du télépathe : soins pris à l'ennemi (Siphon) ou sur soi (Transfusion), états retirés (Clarté), alliés
// relevés en zone (Éveil).
public sealed partial class CombatSimulation
{
    /// <summary>
    /// Le soin d'une compétence : un tirage multiplié par la puissance du lanceur. Transfusion : il est pris sur les PV
    /// du lanceur, qui perd ce que la cible gagne et garde au moins 1 PV.
    /// </summary>
    private void HealFrom(Unit healer, SkillDefinition skill, Unit target)
    {
        var amount = healer.HealWith(_random.NextInt(skill.MinHeal, skill.MaxHeal));
        if (skill.GivesOwnHp)
            amount = Math.Min(amount, healer.Hp - 1);
        var healed = Heal(target, amount);
        if (skill.GivesOwnHp && healed > 0)
            LoseHp(healer, healed, fallbackCell: null);
    }

    /// <summary>
    /// Les états qu'une Clarté retirerait : les néfastes, sauf une domination (une unité dominée est passée du côté du
    /// lanceur : l'apaiser ne la rend pas à son camp).
    /// </summary>
    private static IEnumerable<StatusKind> ClearableStatuses(Unit unit) =>
        unit.StatusEndTicks.Keys.Where(s => s.IsHarmful() && s != StatusKind.Dominated);

    /// <summary>Clarté : ces états prennent fin (un étourdissement fini la rend prête si sa jauge l'était).</summary>
    private void ClearHarmfulStatuses(Unit unit)
    {
        foreach (var status in ClearableStatuses(unit).ToList())
            EndStatus(unit, status);
    }

    /// <summary>
    /// Siphon : <paramref name="drained"/> PV rendus à l'allié du lanceur le plus blessé (le plus de PV manquants ; à
    /// égalité, dans l'ordre des unités) à <paramref name="range"/> cases de lui au plus : un héros debout, pas le
    /// lanceur, ni un gadget, ni une unité dominée (un ennemi passé de son côté pour un temps).
    /// </summary>
    private void Siphon(Unit caster, int range, int drained)
    {
        if (drained <= 0)
            return;
        var ally = _units
            .Where(u => u != caster && u.IsStandingCombatant && u.Team == caster.Team && u.DominatedBy is null && u.Hp < u.MaxHp
                        && u.Position.DistanceTo(caster.Position) <= range)
            .OrderByDescending(u => u.MaxHp - u.Hp)
            .FirstOrDefault();
        if (ally is not null)
            Heal(ally, drained);
    }

    /// <summary>Éveil : les alliés du lanceur à terre sur les cases de la zone se relèvent, comme avec l'action « relever ».</summary>
    private void ReviveDownedIn(Unit caster, IReadOnlyList<Cell> zone)
    {
        var cells = zone.ToHashSet();
        foreach (var ally in _units.Where(u => u.Status == UnitStatus.Downed && u.Team == caster.Team && cells.Contains(u.Position)).ToList())
            Revive(caster, ally);
    }
}
