using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class GadgetTests
{
    private static readonly UnitId Tech = new(1);

    private static readonly UnitDefinition TurretUnit = new()
    {
        Id = "turret", Name = "Tourelle", MaxHp = 30, Initiative = 50, Mobility = 0, OnDefeat = DefeatOutcome.Removed,
        BasicAttack = null, OpportunityAttacksPerCycle = 0, Behavior = Behaviors.Basic,
        Skills = new[] { Strike with { Id = "turret-shot", Range = 5 } },
    };

    private static readonly UnitDefinition BeaconUnit = new()
    {
        Id = "beacon", Name = "Balise", MaxHp = 20, Initiative = 50, Mobility = 0, OnDefeat = DefeatOutcome.Removed,
        BasicAttack = null, OpportunityAttacksPerCycle = 0, Behavior = Behaviors.Beacon, MaxActivations = 3,
        Skills = new[]
        {
            new SkillDefinition
            {
                Id = "pulse", Weight = SkillWeight.Light, Range = 0, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
                MinHeal = 10, MaxHeal = 10,
                Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false, Affects = ZoneTargets.Allies },
            },
        },
    };

    private static SkillDefinition SummonSkill(string id, UnitDefinition unit) => new()
    {
        Id = id, Weight = SkillWeight.Light, Range = 3, MinDamage = 0, MaxDamage = 0, CooldownMs = 0, Summon = unit,
    };

    private static readonly SkillDefinition PlaceTurret = SummonSkill("place-turret", TurretUnit);
    private static readonly SkillDefinition PlaceBeacon = SummonSkill("place-beacon", BeaconUnit);

    private static UnitSetup TechAt(int x, int y, int initiative = 10) =>
        Unit(1, team: 0, x, y, gauge: Capacity, initiative: initiative, skills: new[] { PlaceTurret, PlaceBeacon });

    private static ActivateCommand Place(string skill, Cell cell) => new(Tech, null, new SkillUse(skill, null, cell));

    [Fact]
    public void Turret_IsSummonedOnTheChosenCell_AndShootsOnItsOwn()
    {
        var sim = Simulation(new Grid(8, 1), TechAt(0, 0), Unit(9, team: 1, 6, 0, initiative: 0));

        sim.Submit(Place("place-turret", new Cell(2, 0)));
        Assert.Contains(new UnitSummoned(1, new UnitId(10), "turret", Tech, 0, new Cell(2, 0)), sim.Tick());

        var turret = sim.Get(10);
        Assert.Equal((0, Tech), (turret.Team, turret.SummonedBy!.Value));

        var events = sim.TickTimes(3); // initiative 50 : prête au tick 3, tire au tick 4
        Assert.Contains(events, e => e is SkillUsed { Unit.Value: 10, SkillId: "turret-shot" });
        Assert.Equal(new Cell(2, 0), turret.Position);
    }

    [Fact]
    public void NewTurret_ReplacesTheOldOne()
    {
        var sim = Simulation(new Grid(8, 3), TechAt(0, 0, initiative: Capacity));

        sim.Submit(Place("place-turret", new Cell(2, 0)));
        sim.Tick();
        sim.Submit(Place("place-turret", new Cell(2, 2)));
        var events = sim.Tick();

        Assert.Contains(new UnitRemoved(2, new UnitId(2)), events);
        Assert.Contains(new UnitSummoned(2, new UnitId(3), "turret", Tech, 0, new Cell(2, 2)), events);
        Assert.Single(sim.Units, u => u.Definition.Id == "turret" && u.Status == UnitStatus.Active);
    }

    [Fact]
    public void Summon_NeedsAFreeCellInRangeAndSight()
    {
        var sim = Simulation(Grid.FromRows("..#....", "......."), TechAt(0, 0), Unit(2, team: 0, 1, 1));

        var occupied = Place("place-turret", new Cell(1, 1));
        var wall = Place("place-turret", new Cell(2, 0));
        var hidden = Place("place-turret", new Cell(3, 0));
        var far = Place("place-turret", new Cell(4, 1));
        foreach (var command in new[] { occupied, wall, hidden, far })
            sim.Submit(command);

        Assert.Equal(
            new CombatEvent[]
            {
                new CommandRejected(1, occupied, RejectionReason.InvalidDestination),
                new CommandRejected(1, wall, RejectionReason.InvalidDestination),
                new CommandRejected(1, hidden, RejectionReason.NoLineOfSight),
                new CommandRejected(1, far, RejectionReason.OutOfRange),
            },
            sim.Tick());
    }

    [Fact]
    public void Gadgets_BlockEnemies_LetAlliesThrough_AndMakeNoOpportunityAttack()
    {
        var grid = Grid.FromRows("#####", ".....", "#####");
        var sim = Simulation(grid, TechAt(0, 1), Unit(2, team: 0, 1, 1, gauge: Capacity, mobility: 4), Unit(9, team: 1, 4, 1, gauge: Capacity));
        sim.Submit(Place("place-turret", new Cell(2, 1)));
        sim.Tick();

        var allyThrough = new ActivateCommand(new UnitId(2), new Cell(3, 1));
        var enemyThrough = new ActivateCommand(new UnitId(9), new Cell(1, 1));
        sim.Submit(allyThrough);
        sim.Submit(enemyThrough);
        var events = sim.Tick();

        Assert.Contains(events, e => e is UnitMoved { Unit.Value: 2 });
        Assert.Contains(new CommandRejected(2, enemyThrough, RejectionReason.Unreachable), events);
        Assert.DoesNotContain(events, e => e is OpportunityAttack);
    }

    [Fact]
    public void Beacon_HealsOnlyWoundedStandingAllies_ThreeTimes_ThenDisappears()
    {
        var sim = Simulation(new Grid(6, 3), TechAt(0, 1), Unit(2, team: 0, 2, 0), Unit(3, team: 0, 2, 2, onDefeat: DefeatOutcome.Downed));
        sim.Submit(Place("place-beacon", new Cell(2, 1)));
        sim.Tick();
        var beacon = sim.Units.Single(u => u.Definition.Id == "beacon");

        // Personne n'est blessé : prête, elle attend.
        Assert.DoesNotContain(sim.TickTimes(5), e => e is SkillUsed { SkillId: "pulse" });

        sim.Get(2).Hp = 50;
        sim.Get(3).Hp = 0;
        sim.Get(3).Status = UnitStatus.Downed;
        var heals = new List<CombatEvent>();
        for (var i = 0; i < 20; i++)
        {
            heals.AddRange(sim.Tick().OfType<UnitHealed>());
            sim.Get(2).Hp = Math.Min(sim.Get(2).Hp, 50); // reste blessé pour la relancer
        }

        Assert.Equal(3, heals.Count);
        Assert.All(heals, h => Assert.Equal(2, ((UnitHealed)h).Unit.Value)); // pas l'allié à terre
        Assert.Equal(UnitStatus.Removed, beacon.Status);
    }

    [Fact]
    public void Heal_DoesNotExceedMaxHp()
    {
        var sim = Simulation(new Grid(6, 3), TechAt(0, 1), Unit(2, team: 0, 2, 0));
        sim.Submit(Place("place-beacon", new Cell(2, 1)));
        sim.Tick();
        sim.Get(2).Hp = 96;

        var healed = sim.TickTimes(5).OfType<UnitHealed>().First();

        Assert.Equal(new UnitHealed(healed.Tick, new UnitId(2), 4, 100), healed);
    }

    [Fact]
    public void Turret_CanBeRePlacedOnItsOwnCell()
    {
        var sim = Simulation(new Grid(8, 1), TechAt(0, 0, initiative: Capacity));
        sim.Submit(Place("place-turret", new Cell(2, 0)));
        sim.Tick();

        sim.Submit(Place("place-turret", new Cell(2, 0)));
        var events = sim.Tick();

        Assert.Contains(new UnitRemoved(2, new UnitId(2)), events);
        Assert.Contains(new UnitSummoned(2, new UnitId(3), "turret", Tech, 0, new Cell(2, 0)), events);
    }

    [Fact]
    public void OnlySkillUses_CountTowardsMaxActivations_AndTheLastChargedOneStillResolves()
    {
        var charged = Strike with { Id = "charged", Weight = SkillWeight.Heavy, ChargeMs = 500 };
        var limited = Unit(1, team: 0, 0, 0, initiative: Capacity, gauge: Capacity, mobility: 3, skills: new[] { charged });
        var sim = Simulation(new Grid(8, 1),
            limited with { Definition = limited.Definition with { MaxActivations = 1 } },
            Unit(9, team: 1, 3, 0, initiative: 0));

        sim.Submit(new ActivateCommand(new UnitId(1), new Cell(1, 0))); // déplacement seul : ne compte pas
        sim.Tick();
        Assert.Equal(UnitStatus.Active, sim.Get(1).Status);

        sim.Submit(new ActivateCommand(new UnitId(1), new Cell(2, 0), new SkillUse("charged", new UnitId(9))));
        var events = sim.TickTimes(11);

        Assert.Contains(events, e => e is UnitDamaged { Unit.Value: 9 }); // l'attaque chargée se déclenche
        Assert.Equal(UnitStatus.Removed, sim.Get(1).Status);             // puis l'unité, épuisée, disparaît
    }

    [Fact]
    public void HealOnAFullHpAlly_IsRefused()
    {
        var patch = new SkillDefinition
        {
            Id = "patch", Target = SkillTargeting.Ally, Weight = SkillWeight.Light, Range = 3,
            MinDamage = 0, MaxDamage = 0, MinHeal = 5, MaxHeal = 5, CooldownMs = 0,
        };
        var sim = Simulation(new Grid(5, 1), Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { patch }), Unit(2, team: 0, 2, 0));

        var command = new ActivateCommand(Tech, null, new SkillUse("patch", new UnitId(2)));
        sim.Submit(command);

        Assert.Equal(new CommandRejected(1, command, RejectionReason.NoEffect), sim.Tick().Single());
    }

    [Fact]
    public void SummonedUnits_AreCheckedAgainstTheRules()
    {
        var tech = new UnitDefinition
        {
            Id = "tech", Name = "Tech", MaxHp = 50, Initiative = 10, Mobility = 3, OnDefeat = DefeatOutcome.Downed,
            BasicAttack = null, OpportunityAttacksPerCycle = 1, Behavior = null,
            Skills = new[] { SummonSkill("place-turret", TurretUnit with { Initiative = Capacity + 1 }) },
        };

        Assert.Throws<ArgumentException>(() => tech.ValidateAgainst(Rules));
    }

    [Theory]
    [InlineData("""{ "id": "x", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "summon": { "id": "g", "name": "G", "maxHp": 10, "initiative": 5, "mobility": 0, "onDefeat": "downed", "basicAttack": null, "opportunityAttacksPerCycle": 0, "behavior": "basic", "skills": [] } }""")]
    [InlineData("""{ "id": "x", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "summon": { "id": "g", "name": "G", "maxHp": 10, "initiative": 5, "mobility": 2, "onDefeat": "removed", "basicAttack": null, "opportunityAttacksPerCycle": 0, "behavior": "basic", "skills": [] } }""")]
    [InlineData("""{ "id": "x", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "summon": { "id": "g", "name": "G", "maxHp": 10, "initiative": 5, "mobility": 0, "onDefeat": "removed", "basicAttack": null, "opportunityAttacksPerCycle": 0, "behavior": null, "skills": [] } }""")]
    [InlineData("""{ "id": "x", "weight": "light", "range": 3, "minDamage": 5, "maxDamage": 5, "cooldownMs": 0, "summon": { "id": "g", "name": "G", "maxHp": 10, "initiative": 5, "mobility": 0, "onDefeat": "removed", "basicAttack": null, "opportunityAttacksPerCycle": 0, "behavior": "basic", "skills": [] } }""")]
    [InlineData("""{ "id": "x", "weight": "light", "range": 1, "minDamage": 0, "maxDamage": 0, "minHeal": 5, "maxHeal": 5, "cooldownMs": 0 }""")]
    [InlineData("""{ "id": "x", "weight": "light", "range": 2, "minDamage": 0, "maxDamage": 0, "minHeal": 5, "maxHeal": 5, "cooldownMs": 0, "zone": { "shape": "square", "size": 1, "throughWalls": false } }""")]
    [InlineData("""{ "id": "x", "target": "ally", "weight": "light", "range": 2, "minDamage": 3, "maxDamage": 3, "minHeal": 5, "maxHeal": 5, "cooldownMs": 0 }""")]
    public void InvalidGadgetsAndHeals_AreRejectedAtLoad(string skill)
    {
        Assert.Throws<ArgumentException>(() => SkillDefinition.ListFromJson($"[{skill}]"));
    }

    [Fact]
    public void AllyHeal_IsAValidSupportSkill()
    {
        var skill = SkillDefinition.ListFromJson("""
            [{ "id": "patch", "target": "ally", "weight": "light", "range": 2, "minDamage": 0, "maxDamage": 0, "minHeal": 5, "maxHeal": 8, "cooldownMs": 0 }]
            """).Single();

        Assert.True(skill.Heals);
    }
}
