using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class ChargedAttackTests
{
    private static readonly UnitId Caster = new(1);

    /// <summary>Zone carrée de rayon 1 à 4 cases, chargée 10 ticks (500 ms), lourde, 20 dégâts, 2 s de recharge.</summary>
    private static readonly SkillDefinition Quake = new()
    {
        Id = "quake", Weight = SkillWeight.Heavy, Range = 4, MinDamage = 20, MaxDamage = 20, CooldownMs = 2000,
        ChargeMs = 500, Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
    };

    /// <summary>Tir chargé sur une unité, portée 4, 10 ticks de charge, 25 dégâts.</summary>
    private static readonly SkillDefinition AimedShot = new()
    {
        Id = "aimed", Weight = SkillWeight.Heavy, Range = 4, MinDamage = 25, MaxDamage = 25, CooldownMs = 0, ChargeMs = 500,
    };

    private static readonly IReadOnlyList<Cell> QuakeZoneAt31 = new[]
    {
        new Cell(2, 0), new Cell(3, 0), new Cell(4, 0),
        new Cell(2, 1), new Cell(3, 1), new Cell(4, 1),
        new Cell(2, 2), new Cell(3, 2), new Cell(4, 2),
    };

    private static UnitSetup CasterAt(int x, int y, params SkillDefinition[] skills) =>
        Unit(1, team: 0, x, y, gauge: Capacity, skills: skills);

    private static ActivateCommand Launch(string skill, Cell cell) => new(Caster, null, new SkillUse(skill, null, cell));

    private static ActivateCommand Aim(UnitId target) => new(Caster, null, new SkillUse("aimed", target));

    [Fact]
    public void Charge_ShowsTheZone_ImmobilizesTheCaster_ThenReleases()
    {
        var sim = Simulation(new Grid(7, 3), CasterAt(0, 1, Quake), Unit(2, team: 1, 3, 1));

        sim.Submit(Launch("quake", new Cell(3, 1)));
        Assert.Equal(
            new CombatEvent[] { new ChargeStarted(1, Caster, "quake", null, QuakeZoneAt31, 11) },
            sim.Tick());
        Assert.Equal(41, sim.Get(1).CooldownEndTick("quake")); // la recharge démarre au lancement

        var move = new ActivateCommand(Caster, new Cell(0, 0));
        sim.Submit(move);
        Assert.Contains(new CommandRejected(2, move, RejectionReason.Charging), sim.Tick());

        sim.TickTimes(8); // ticks 3 à 10
        Assert.Equal(0, sim.Get(1).Gauge);

        Assert.Equal(
            new CombatEvent[]
            {
                new SkillUsed(11, Caster, "quake", null, QuakeZoneAt31),
                new UnitDamaged(11, new UnitId(2), 20, 80),
            },
            sim.Tick().Where(e => e is not UnitReady));
        Assert.Null(sim.Get(1).Charge);
        Assert.Equal(10, sim.Get(1).Gauge); // lourde : repart de 0, puis +10 au même tick
    }

    [Fact]
    public void Zone_HitsWhoeverStandsInItAtRelease_AlliesIncluded_ByDefault()
    {
        var sim = Simulation(new Grid(7, 3),
            CasterAt(0, 1, Quake),
            Unit(2, team: 1, 3, 1),                                         // reste dans la zone
            Unit(3, team: 1, 4, 2, gauge: Capacity, initiative: Capacity),  // sort de la zone
            Unit(4, team: 0, 2, 0));                                        // allié dans la zone

        sim.Submit(Launch("quake", new Cell(3, 1)));
        sim.Tick();
        sim.Submit(new ActivateCommand(new UnitId(3), new Cell(6, 2)));
        sim.TickTimes(10);

        Assert.Equal(80, sim.Get(2).Hp);
        Assert.Equal(100, sim.Get(3).Hp);
        Assert.Equal(80, sim.Get(4).Hp);
        Assert.Equal(100, sim.Get(1).Hp);
    }

    [Fact]
    public void Zone_ForEnemiesOnly_SparesAllies()
    {
        var enemiesOnly = Quake with { Zone = Quake.Zone! with { Affects = ZoneTargets.Enemies } };
        var sim = Simulation(new Grid(7, 3), CasterAt(0, 1, enemiesOnly), Unit(2, team: 1, 3, 1), Unit(4, team: 0, 2, 0));

        sim.Submit(Launch("quake", new Cell(3, 1)));
        sim.TickTimes(11);

        Assert.Equal(80, sim.Get(2).Hp);
        Assert.Equal(100, sim.Get(4).Hp);
    }

    [Fact]
    public void StunningTheCaster_InterruptsTheCharge()
    {
        var stunBlow = Strike with { Id = "stun-blow", Effects = new[] { new StatusEffect { Status = StatusKind.Stunned, DurationMs = 100 } } };
        var sim = Simulation(new Grid(7, 3),
            CasterAt(0, 1, Quake),
            Unit(2, team: 1, 3, 1),
            Unit(3, team: 1, 0, 0, gauge: Capacity, skills: new[] { stunBlow })); // au contact, hors de la ligne de vue

        sim.Submit(Launch("quake", new Cell(3, 1)));
        sim.Tick();
        sim.Submit(new ActivateCommand(new UnitId(3), null, new SkillUse("stun-blow", Caster)));
        var events = sim.Tick();

        Assert.Equal(new ChargeInterrupted(2, Caster, "quake"), events.OfType<ChargeInterrupted>().Single());
        Assert.DoesNotContain(sim.TickTimes(20), e => e is SkillUsed);
        Assert.Equal(100, sim.Get(2).Hp);
    }

    [Fact]
    public void DefeatingTheCaster_InterruptsTheCharge_BeforeItFalls()
    {
        var sim = Simulation(new Grid(7, 3),
            Unit(1, team: 0, 0, 1, gauge: Capacity, hp: 10, skills: new[] { Quake }),
            Unit(3, team: 1, 0, 0, gauge: Capacity));

        sim.Submit(Launch("quake", new Cell(3, 1)));
        sim.Tick();
        sim.Submit(new ActivateCommand(new UnitId(3), null, new SkillUse("strike", Caster)));

        Assert.Equal(
            new CombatEvent[]
            {
                new UnitDamaged(2, Caster, 10, 0),
                new ChargeInterrupted(2, Caster, "quake"),
                new UnitRemoved(2, Caster),
            },
            sim.Tick().SkipWhile(e => e is not UnitDamaged).Take(3));
    }

    [Fact]
    public void AimedCharge_HitsATargetThatStayedInRangeAndSight()
    {
        var sim = Simulation(new Grid(7, 1), CasterAt(0, 0, AimedShot), Unit(2, team: 1, 3, 0));

        sim.Submit(Aim(new UnitId(2)));
        Assert.Equal(
            new CombatEvent[] { new ChargeStarted(1, Caster, "aimed", new UnitId(2), null, 11) },
            sim.Tick());

        sim.TickTimes(9);
        Assert.Contains(new UnitDamaged(11, new UnitId(2), 25, 75), sim.Tick());
    }

    [Fact]
    public void AimedCharge_MissesATargetThatLeftTheRange()
    {
        var sim = Simulation(new Grid(7, 1), CasterAt(0, 0, AimedShot),
            Unit(2, team: 1, 3, 0, gauge: Capacity, initiative: Capacity));

        sim.Submit(Aim(new UnitId(2)));
        sim.Tick();
        sim.Submit(new ActivateCommand(new UnitId(2), new Cell(6, 0)));
        sim.TickTimes(9);

        Assert.Contains(new ChargeMissed(11, Caster, "aimed", new UnitId(2)), sim.Tick());
        Assert.Equal(100, sim.Get(2).Hp);
    }

    [Fact]
    public void AimedCharge_MissesATargetThatHidBehindAWall()
    {
        // La cible passe de (3,1) à (4,0), derrière le mur (3,0) vu de (0,1).
        var grid = Grid.FromRows(
            "...#...",
            ".......");
        var sim = Simulation(grid, CasterAt(0, 1, AimedShot), Unit(2, team: 1, 3, 1, gauge: Capacity, initiative: Capacity));

        sim.Submit(Aim(new UnitId(2)));
        sim.Tick();
        sim.Submit(new ActivateCommand(new UnitId(2), new Cell(4, 0)));
        sim.TickTimes(9);

        Assert.Contains(new ChargeMissed(11, Caster, "aimed", new UnitId(2)), sim.Tick());
    }

    [Fact]
    public void InstantZone_CenteredOnTheCaster_HitsAroundButNotTheCaster()
    {
        var nova = new SkillDefinition
        {
            Id = "nova", Weight = SkillWeight.Light, Range = 0, MinDamage = 15, MaxDamage = 15, CooldownMs = 0,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        };
        var sim = Simulation(new Grid(5, 3), CasterAt(1, 1, nova), Unit(2, team: 1, 2, 2), Unit(3, team: 1, 4, 1));

        sim.Submit(Launch("nova", new Cell(1, 1)));
        var events = sim.Tick();

        Assert.IsType<SkillUsed>(events[0]);
        Assert.Equal(new UnitDamaged(1, new UnitId(2), 15, 85), events[1]);
        Assert.Equal(100, sim.Get(1).Hp);
        Assert.Equal(100, sim.Get(3).Hp);
        Assert.Equal(40 + 10, sim.Get(1).Gauge); // instantanée et légère
    }

    [Fact]
    public void LineZone_HitsAlongTheLine_UpToItsLength()
    {
        var wave = new SkillDefinition
        {
            Id = "wave", Weight = SkillWeight.Heavy, Range = 1, MinDamage = 15, MaxDamage = 15, CooldownMs = 0,
            Zone = new ZoneDefinition { Shape = ZoneShape.Line, Size = 3, ThroughWalls = false },
        };
        var sim = Simulation(new Grid(6, 1), CasterAt(0, 0, wave), Unit(2, team: 1, 2, 0), Unit(3, team: 1, 4, 0));

        sim.Submit(Launch("wave", new Cell(1, 0)));
        var used = sim.Tick().OfType<SkillUsed>().Single();

        Assert.Equal(new[] { new Cell(1, 0), new Cell(2, 0), new Cell(3, 0) }, used.Zone);
        Assert.Equal(85, sim.Get(2).Hp);
        Assert.Equal(100, sim.Get(3).Hp);
    }

    [Fact]
    public void SquareZone_NeedsSightOfItsCenter()
    {
        var sim = Simulation(Grid.FromRows("..#...."), CasterAt(0, 0, Quake));

        var hidden = Launch("quake", new Cell(4, 0));
        sim.Submit(hidden);

        Assert.Equal(new CommandRejected(1, hidden, RejectionReason.NoLineOfSight), sim.Tick().Single());
    }

    [Fact]
    public void WrongKindOfTarget_IsRejected()
    {
        var sim = Simulation(new Grid(7, 1), CasterAt(0, 0, Quake, AimedShot), Unit(2, team: 1, 3, 0));

        var zoneOnUnit = new ActivateCommand(Caster, null, new SkillUse("quake", new UnitId(2)));
        var aimOnCell = new ActivateCommand(Caster, null, new SkillUse("aimed", null, new Cell(3, 0)));
        var tooFar = Launch("quake", new Cell(5, 0));
        sim.Submit(zoneOnUnit);
        sim.Submit(aimOnCell);
        sim.Submit(tooFar);

        Assert.Equal(
            new CombatEvent[]
            {
                new CommandRejected(1, zoneOnUnit, RejectionReason.InvalidTarget),
                new CommandRejected(1, aimOnCell, RejectionReason.InvalidTarget),
                new CommandRejected(1, tooFar, RejectionReason.OutOfRange),
            },
            sim.Tick());
    }

    [Fact]
    public void ChargingUnit_MakesNoOpportunityAttack()
    {
        // L'ennemi en (0,0), au contact du lanceur, s'éloigne vers (3,0) : le pas (1,0) → (2,0) sort de sa portée.
        var caster = Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { Strike, Quake }, basicAttack: "strike");
        var sim = Simulation(new Grid(7, 3), caster, Unit(2, team: 1, 0, 0, gauge: Capacity, initiative: Capacity));

        sim.Submit(Launch("quake", new Cell(3, 1)));
        Assert.Contains(sim.Tick(), e => e is ChargeStarted);

        sim.Submit(new ActivateCommand(new UnitId(2), new Cell(3, 0)));
        var events = sim.Tick();
        Assert.Contains(events, e => e is UnitMoved);
        Assert.DoesNotContain(events, e => e is OpportunityAttack);
    }

    [Fact]
    public void LineBlockedFromItsFirstStep_IsRejected()
    {
        var wave = new SkillDefinition
        {
            Id = "wave", Weight = SkillWeight.Heavy, Range = 1, MinDamage = 15, MaxDamage = 15, CooldownMs = 0,
            Zone = new ZoneDefinition { Shape = ZoneShape.Line, Size = 3, ThroughWalls = false },
        };
        var sim = Simulation(Grid.FromRows(".#", ".."), CasterAt(0, 0, wave));

        var blocked = Launch("wave", new Cell(1, 1)); // diagonale qui frôle le mur (1,0)
        sim.Submit(blocked);

        Assert.Equal(new CommandRejected(1, blocked, RejectionReason.InvalidTarget), sim.Tick().Single());
        Assert.True(sim.Get(1).IsReady);
    }

    [Fact]
    public void BasicAttack_CannotBeChargedOrAZone()
    {
        Assert.Throws<ArgumentException>(() =>
            Simulation(new Grid(2, 1), Unit(1, team: 0, 0, 0, skills: new[] { AimedShot }, basicAttack: "aimed")));
        Assert.Throws<ArgumentException>(() =>
            Simulation(new Grid(2, 1), Unit(1, team: 0, 0, 0, skills: new[] { Quake with { ChargeMs = 0 } }, basicAttack: "quake")));
    }
}
