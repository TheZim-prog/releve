using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>
/// Points de stats (docs/design.md 2.6). Avec les règles de test, un point vaut +10 % de la stat de base
/// (PV, initiative) ou +10 points de puissance.
/// </summary>
public class StatPointsTests
{
    private static readonly UnitId Hero = new(1);
    private static readonly UnitId Enemy = new(2);

    private static StatPoints Points(int hp = 0, int power = 0, int initiative = 0) =>
        new() { Hp = hp, Power = power, Initiative = initiative };

    private static ActivateCommand Use(string skill, UnitId? target = null, Cell? cell = null) =>
        new(Hero, null, new SkillUse(skill, target, cell));

    [Fact]
    public void WithoutPoints_AUnitHasTheStatsOfItsType()
    {
        var sim = Simulation(new Grid(3, 1), Unit(1, team: 0, 0, 0, hp: 100, initiative: 10, mobility: 3));

        var unit = sim.Get(1);

        Assert.Equal((100, 100, 10, 3, 100), (unit.MaxHp, unit.Hp, unit.Initiative, unit.Mobility, unit.PowerPercent));
    }

    [Fact]
    public void Points_RaiseHpPowerAndInitiative_ButNotMobility()
    {
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, hp: 100, initiative: 10, mobility: 3, points: Points(hp: 3, power: 2, initiative: 5)));

        var unit = sim.Get(1);

        Assert.Equal((130, 130, 15, 3, 120), (unit.MaxHp, unit.Hp, unit.Initiative, unit.Mobility, unit.PowerPercent));
        sim.Tick();
        Assert.Equal(15, unit.Gauge); // la jauge se remplit bien à l'initiative augmentée
    }

    [Theory]
    // Les héros établis du prologue retrouvent leurs valeurs : +3 % de PV et +2 % d'initiative par point.
    [InlineData(118, 12, 160)] // colosse : 160,48
    [InlineData(93, 6, 110)]   // combattant : 109,74, arrondi au plus proche
    [InlineData(65, 5, 75)]    // télépathe : 74,75
    [InlineData(76, 0, 76)]
    public void MaxHp_IsRoundedToTheNearest(int baseHp, int points, int expected)
    {
        var rules = Rules with { HpPercentPerStatPoint = 3 };

        Assert.Equal(expected, rules.MaxHpWith(baseHp, Points(hp: points)));
    }

    [Theory]
    [InlineData(129, 12, 160)] // speedster : 159,96
    [InlineData(67, 2, 70)]    // colosse : 69,68
    [InlineData(88, 7, 100)]   // tech : 100,32
    public void Initiative_IsRoundedToTheNearest(int baseInitiative, int points, int expected)
    {
        var rules = Rules with { InitiativePercentPerStatPoint = 2 };

        Assert.Equal(expected, rules.InitiativeWith(baseInitiative, Points(initiative: points)));
    }

    [Fact]
    public void Power_MultipliesDamage()
    {
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity, points: Points(power: 5)),
            Unit(2, team: 1, 1, 0, hp: 100));

        sim.Submit(Use("strike", Enemy));

        Assert.Contains(new UnitDamaged(1, Enemy, 15, 85), sim.Tick()); // 10 × 150 %
        Assert.Equal((15, 15), sim.Get(1).DamageRange(Strike));
    }

    [Fact]
    public void Power_AndTheEmpoweredStatus_CombineWithoutIntermediateRounding()
    {
        var energyForm = new SkillDefinition
        {
            Id = "energy-form", Target = SkillTargeting.Self, Weight = SkillWeight.Heavy, Range = 0,
            MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
            Effects = new[] { new StatusEffect { Status = StatusKind.Empowered, DurationMs = 5000 } },
        };
        var bigStrike = Strike with { Id = "big-strike", MinDamage = 200, MaxDamage = 200 };
        // Puissance 103 % × surpuissant 150 % = 154,5 % : 200 → 309 (et non 308, si 154,5 était d'abord arrondi à 154).
        var setup = Setup(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, initiative: Capacity, gauge: Capacity, skills: new[] { energyForm, bigStrike }, points: Points(power: 1)),
            Unit(2, team: 1, 1, 0, hp: 1000)) with { Rules = Rules with { PowerPercentPerStatPoint = 3 } };
        var sim = new CombatSimulation(setup);

        Assert.Equal((206, 206), sim.Get(1).DamageRange(bigStrike));
        sim.Submit(Use("energy-form"));
        sim.Tick();
        Assert.Equal((309, 309), sim.Get(1).DamageRange(bigStrike)); // l'aperçu suit l'état surpuissant
        sim.Submit(Use("big-strike", Enemy));

        Assert.Contains(new UnitDamaged(2, Enemy, 309, 691), sim.Tick());
    }

    [Fact]
    public void Power_MultipliesHeals_ButNotStatusDurations()
    {
        var patch = new SkillDefinition
        {
            Id = "patch", Target = SkillTargeting.Ally, Weight = SkillWeight.Light, Range = 3,
            MinDamage = 0, MaxDamage = 0, MinHeal = 10, MaxHeal = 10, CooldownMs = 0,
        };
        var stun = Strike with { Id = "stun", Effects = new[] { new StatusEffect { Status = StatusKind.Stunned, DurationMs = 1000 } } };
        var sim = Simulation(new Grid(3, 2),
            Unit(1, team: 0, 0, 0, initiative: Capacity, gauge: Capacity, skills: new[] { patch, stun }, points: Points(power: 5)),
            Unit(2, team: 1, 1, 0, hp: 100),
            Unit(3, team: 0, 0, 1, hp: 100));
        sim.Submit(Use("stun", Enemy));
        var first = sim.Tick();

        Assert.Contains(new StatusApplied(1, Enemy, StatusKind.Stunned, 21), first); // 1 s = 20 ticks, puissance ou non
        Assert.Contains(new UnitDamaged(1, Enemy, 15, 85), first);

        sim.Get(3).Hp = 50; // un allié blessé
        sim.Submit(Use("patch", new UnitId(3)));

        Assert.Contains(new UnitHealed(2, new UnitId(3), 15, 65), sim.Tick()); // 10 × 150 %
    }

    [Fact]
    public void AGadget_TakesThePowerOfItsOwner_NotItsOtherPoints()
    {
        var turret = new UnitDefinition
        {
            Id = "turret", Name = "Tourelle", MaxHp = 30, Initiative = 50, Mobility = 0, OnDefeat = DefeatOutcome.Removed,
            BasicAttack = null, OpportunityAttacksPerCycle = 0, Behavior = Behaviors.Basic,
            Skills = new[] { Strike with { Id = "turret-shot", Range = 5 } },
        };
        var place = new SkillDefinition
        {
            Id = "place-turret", Weight = SkillWeight.Light, Range = 3, MinDamage = 0, MaxDamage = 0, CooldownMs = 0, Summon = turret,
        };
        var sim = Simulation(new Grid(8, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { place }, points: Points(hp: 4, power: 5, initiative: 4)),
            Unit(9, team: 1, 6, 0, initiative: 0, hp: 100));

        sim.Submit(Use("place-turret", cell: new Cell(2, 0)));
        sim.Tick();
        var gadget = sim.Get(10);

        Assert.Equal((30, 50, 150), (gadget.MaxHp, gadget.Initiative, gadget.PowerPercent));
        Assert.Contains(sim.TickTimes(3), e => e is UnitDamaged { Unit.Value: 9, Amount: 15 }); // 10 × 150 %
    }

    [Fact]
    public void AChargedAttack_UsesThePowerOfItsCaster()
    {
        var charged = Strike with { Id = "charged", ChargeMs = 100 }; // 2 ticks
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { charged }, points: Points(power: 2)),
            Unit(2, team: 1, 1, 0, hp: 100));

        sim.Submit(Use("charged", Enemy));

        Assert.Contains(sim.TickTimes(3), e => e is UnitDamaged { Unit.Value: 2, Amount: 12 }); // 10 × 120 %
    }

    [Fact]
    public void PointsOutOfBounds_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => Simulation(new Grid(3, 1), Unit(1, team: 0, 0, 0, points: Points(power: -1))));
        Assert.Throws<ArgumentException>(() =>
            Simulation(new Grid(3, 1), Unit(1, team: 0, 0, 0, points: Points(hp: StatPoints.Max + 1))));
        Simulation(new Grid(3, 1), Unit(1, team: 0, 0, 0, initiative: 0, points: Points(StatPoints.Max, StatPoints.Max, StatPoints.Max)));
    }

    [Fact]
    public void AnInitiativeAboveTheGaugeCapacity_IsRejected_PointsIncluded()
    {
        // 90 d'initiative de base passe (capacité 100) ; avec 2 points (+20 %), 108 dépasse.
        Simulation(new Grid(3, 1), Unit(1, team: 0, 0, 0, initiative: 90, points: Points(initiative: 1)));

        Assert.Throws<ArgumentException>(() =>
            Simulation(new Grid(3, 1), Unit(1, team: 0, 0, 0, initiative: 90, points: Points(initiative: 2))));
    }
}
