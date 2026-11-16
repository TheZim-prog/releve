namespace Game.Core.Combat;

/// <summary>Inerte (un leurre) : n'agit jamais et ne bouge pas ; il est seulement là.</summary>
internal sealed class InertBehavior : IUnitBehavior
{
    public bool ObeysTaunts => false;

    public Intention? ChooseIntention(CombatSimulation sim, Unit unit) => null;

    public ActivateCommand? Execute(CombatSimulation sim, Unit unit, Intention intention) => null;

    public ActivateCommand? Approach(CombatSimulation sim, Unit unit, Intention intention) => null;
}
