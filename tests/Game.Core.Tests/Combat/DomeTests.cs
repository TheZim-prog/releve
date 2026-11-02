using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class DomeTests
{
    private static readonly UnitId Colossus = new(1);
    private static readonly UnitId Ally = new(2);
    private static readonly UnitId Foe = new(9);

    /// <summary>Dôme 3x3 à portée 3.</summary>
    private static readonly SkillDefinition Dome = new()
    {
        Id = "dome", Weight = SkillWeight.Light, Range = 3, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        CreatesDome = true,
    };

    /// <summary>Zone 3x3 autour de soi, 20 dégâts, étourdit.</summary>
    private static readonly SkillDefinition Quake = new()
    {
        Id = "quake", Weight = SkillWeight.Heavy, Range = 0, MinDamage = 20, MaxDamage = 20, CooldownMs = 0,
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        Effects = new[] { new StatusEffect { Status = StatusKind.Stunned, DurationMs = 500 } },
    };

    private static ActivateCommand RaiseDome(Cell center) => new(Colossus, null, new SkillUse("dome", null, center));

    private static ActivateCommand Hit(UnitId attacker, UnitId target) => new(attacker, null, new SkillUse("strike", target));

    [Fact]
    public void Dome_IsRaisedOnTheZone()
    {
        var sim = Simulation(new Grid(7, 3), Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { Dome }));

        sim.Submit(RaiseDome(new Cell(2, 1)));
        var events = sim.Tick();

        Assert.Contains(events, e => e is DomeRaised { Owner.Value: 1, Cells.Count: 9 });
    }

    [Fact]
    public void Dome_BlocksAWholeZoneAttack_ForEveryAllyInside_ThenFalls()
    {
        var sim = Simulation(new Grid(7, 3),
            Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { Dome }),
            Unit(2, team: 0, 2, 0),
            Unit(3, team: 0, 2, 2),
            Unit(9, team: 1, 3, 1, gauge: Capacity, skills: new[] { Quake }));
        sim.Submit(RaiseDome(new Cell(2, 1)));
        sim.Tick();

        sim.Submit(new ActivateCommand(Foe, null, new SkillUse("quake", null, new Cell(3, 1))));
        var events = sim.Tick();

        Assert.Contains(new HitBlocked(2, Ally, Colossus), events);
        Assert.Contains(new HitBlocked(2, new UnitId(3), Colossus), events);
        Assert.Contains(new DomeEnded(2, Colossus), events);
        Assert.DoesNotContain(events, e => e is UnitDamaged or StatusApplied);
        Assert.Equal(100, sim.Get(2).Hp);
    }

    [Fact]
    public void Dome_BlocksOnlyOneSource_TheNextOneLands()
    {
        var sim = Simulation(new Grid(7, 3),
            Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { Dome }),
            Unit(2, team: 0, 2, 1),
            Unit(9, team: 1, 3, 1, gauge: Capacity, initiative: Capacity));
        sim.Submit(RaiseDome(new Cell(2, 1)));
        sim.Tick();

        sim.Submit(Hit(Foe, Ally));
        Assert.Contains(new HitBlocked(2, Ally, Colossus), sim.Tick());

        sim.Submit(Hit(Foe, Ally));
        Assert.Contains(new UnitDamaged(3, Ally, 10, 90), sim.Tick());
    }

    [Fact]
    public void Dome_DoesNotProtectEnemiesStandingInIt()
    {
        var sim = Simulation(new Grid(7, 3),
            Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { Dome }),
            Unit(2, team: 0, 4, 1, gauge: Capacity),
            Unit(9, team: 1, 3, 1));
        sim.Submit(RaiseDome(new Cell(2, 1))); // l'ennemi en (3,1) est sous le dôme
        sim.Tick();

        sim.Submit(Hit(Ally, Foe));
        var events = sim.Tick();

        Assert.Contains(new UnitDamaged(2, Foe, 10, 90), events);
        Assert.DoesNotContain(events, e => e is DomeEnded);
    }

    [Fact]
    public void Dome_Falls_AtTheColossusNextAction()
    {
        var sim = Simulation(new Grid(7, 3), Unit(1, team: 0, 0, 1, gauge: Capacity, initiative: Capacity, skills: new[] { Dome }));
        sim.Submit(RaiseDome(new Cell(2, 1)));
        sim.Tick();

        sim.Submit(new ActivateCommand(Colossus, new Cell(0, 0)));

        Assert.Contains(new DomeEnded(2, Colossus), sim.Tick());
    }

    [Fact]
    public void Dome_Falls_IfTheColossusIsDowned_NotIfStunned()
    {
        var stunBlow = Strike with { Id = "stun-blow", MinDamage = 0, MaxDamage = 0, Effects = new[] { new StatusEffect { Status = StatusKind.Stunned, DurationMs = 500 } } };
        var sim = Simulation(new Grid(7, 3),
            Unit(1, team: 0, 0, 1, hp: 10, gauge: Capacity, onDefeat: DefeatOutcome.Downed, skills: new[] { Dome }),
            Unit(9, team: 1, 1, 0, gauge: Capacity, initiative: Capacity, skills: new[] { stunBlow, Strike }));
        sim.Submit(RaiseDome(new Cell(3, 1))); // cases (2..4, 0..2) : le colosse n'est pas sous son dôme
        Assert.Contains(sim.Tick(), e => e is DomeRaised);

        sim.Submit(new ActivateCommand(Foe, null, new SkillUse("stun-blow", Colossus)));
        Assert.DoesNotContain(sim.Tick(), e => e is DomeEnded);

        sim.Submit(Hit(Foe, Colossus));
        Assert.Contains(new DomeEnded(3, Colossus), sim.Tick());
    }

    [Fact]
    public void Dome_BlocksAProjectionEntirely()
    {
        var projection = Strike with
        {
            Id = "projection",
            ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Push, Distance = 2 },
        };
        var sim = Simulation(new Grid(7, 3),
            Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { Dome }),
            Unit(2, team: 0, 2, 1),
            Unit(9, team: 1, 3, 1, gauge: Capacity, skills: new[] { projection }));
        sim.Submit(RaiseDome(new Cell(2, 1)));
        sim.Tick();

        sim.Submit(new ActivateCommand(Foe, null, new SkillUse("projection", Ally)));
        var events = sim.Tick();

        Assert.Contains(new HitBlocked(2, Ally, Colossus), events);
        Assert.DoesNotContain(events, e => e is UnitDamaged or UnitDisplaced);
    }

    [Fact]
    public void OpportunityAttack_IsASource_TheDomeBlocksItAndFalls()
    {
        var sim = Simulation(new Grid(7, 3),
            Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { Dome }),
            Unit(2, team: 0, 2, 1, gauge: Capacity, initiative: Capacity),
            Unit(9, team: 1, 3, 1, basicAttack: "strike"));
        sim.Submit(RaiseDome(new Cell(2, 1)));
        sim.Tick();

        // L'allié quitte le contact de l'ennemi depuis une case sous le dôme : (2,1) → (1,0) → (0,0).
        sim.Submit(new ActivateCommand(Ally, new Cell(0, 0)));
        var events = sim.Tick();

        Assert.Contains(events, e => e is OpportunityAttack { Attacker.Value: 9 });
        Assert.Contains(new HitBlocked(2, Ally, Colossus), events);
        Assert.Contains(new DomeEnded(2, Colossus), events);
        Assert.Equal(100, sim.Get(2).Hp);
    }

    [Fact]
    public void Dome_DoesNotBlockSupportSkills()
    {
        var order = new SkillDefinition
        {
            Id = "order", Target = SkillTargeting.Ally, Weight = SkillWeight.Light, Range = 5,
            MinDamage = 0, MaxDamage = 0, CooldownMs = 0, GaugeGainPercent = 50,
        };
        // Un autre allié (le tech) donne l'ordre pendant que le dôme est levé.
        var sim = Simulation(new Grid(7, 3),
            Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { Dome }),
            Unit(2, team: 0, 2, 1, initiative: 0),
            Unit(3, team: 0, 0, 0, gauge: Capacity, skills: new[] { order }));
        sim.Submit(RaiseDome(new Cell(2, 1)));
        sim.Tick();

        sim.Submit(new ActivateCommand(new UnitId(3), null, new SkillUse("order", Ally)));
        var events = sim.Tick();
        Assert.Contains(events, e => e is GaugeGained { Unit.Value: 2 });
        Assert.DoesNotContain(events, e => e is HitBlocked or DomeEnded);
    }

    [Fact]
    public void AttackThatAlsoDownsTheColossus_StaysBlockedForTheAlliesUnderTheDome()
    {
        // Le colosse (10 PV) est hors de son dôme mais dans la zone ennemie ; l'allié listé après lui est dessous.
        var sim = Simulation(new Grid(7, 3),
            Unit(1, team: 0, 3, 0, hp: 10, gauge: Capacity, onDefeat: DefeatOutcome.Downed, skills: new[] { Dome }),
            Unit(9, team: 1, 4, 1, gauge: Capacity, skills: new[] { Quake }),
            Unit(2, team: 0, 3, 2));
        sim.Submit(RaiseDome(new Cell(2, 2))); // cases (1..3, 1..2) : l'allié dessous, pas le colosse
        Assert.Contains(sim.Tick(), e => e is DomeRaised);

        sim.Submit(new ActivateCommand(Foe, null, new SkillUse("quake", null, new Cell(4, 1))));
        var events = sim.Tick();

        Assert.Contains(new UnitDowned(2, Colossus, new Cell(3, 0)), events);
        Assert.Contains(new HitBlocked(2, Ally, Colossus), events);
        Assert.Equal(100, sim.Get(2).Hp);
        Assert.Contains(new DomeEnded(2, Colossus), events);
    }

    [Fact]
    public void CollisionWithAUnitUnderTheDome_IsBlocked()
    {
        var projection = Strike with
        {
            Id = "projection",
            ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Push, Distance = 3, CollisionMinDamage = 8, CollisionMaxDamage = 8 },
        };
        // Un héros (5) projette l'ennemi (8) sur l'allié protégé (2) : l'ennemi prend la collision, pas l'allié.
        var sim = Simulation(new Grid(8, 3),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { Dome }),
            Unit(2, team: 0, 2, 1),
            Unit(8, team: 1, 3, 1),
            Unit(5, team: 0, 4, 1, gauge: Capacity, skills: new[] { projection }));
        sim.Submit(RaiseDome(new Cell(2, 1)));
        sim.Tick();

        sim.Submit(new ActivateCommand(new UnitId(5), null, new SkillUse("projection", new UnitId(8))));
        var events = sim.Tick();

        Assert.Contains(new UnitCollided(2, new UnitId(8), Ally), events);
        Assert.Contains(new HitBlocked(2, Ally, Colossus), events);
        Assert.Equal(100, sim.Get(2).Hp);
        Assert.Equal(100 - 10 - 8, sim.Get(8).Hp);
    }

    [Fact]
    public void ChargedAttackReleasedOnTheDome_IsBlocked()
    {
        var chargedQuake = Quake with { Id = "charged-quake", ChargeMs = 500 };
        var sim = Simulation(new Grid(7, 3),
            Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { Dome }),
            Unit(2, team: 0, 2, 1),
            Unit(9, team: 1, 3, 1, gauge: Capacity, skills: new[] { chargedQuake }));

        sim.Submit(new ActivateCommand(Foe, null, new SkillUse("charged-quake", null, new Cell(3, 1))));
        sim.Tick();
        sim.Submit(RaiseDome(new Cell(2, 1))); // le dôme est levé pendant la charge
        var events = sim.TickTimes(10);

        Assert.Contains(new HitBlocked(11, Ally, Colossus), events);
        Assert.Equal(100, sim.Get(2).Hp);
    }

    [Fact]
    public void FriendlyFire_IsBlockedToo_AndUsesTheDome()
    {
        var nova = Quake with { Id = "nova", Weight = SkillWeight.Light, Effects = Array.Empty<StatusEffect>() };
        var sim = Simulation(new Grid(7, 3),
            Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { Dome }),
            Unit(2, team: 0, 2, 1),
            Unit(3, team: 0, 3, 1, gauge: Capacity, skills: new[] { nova }));
        sim.Submit(RaiseDome(new Cell(2, 1)));
        sim.Tick();

        sim.Submit(new ActivateCommand(new UnitId(3), null, new SkillUse("nova", null, new Cell(3, 1))));
        var events = sim.Tick();

        Assert.Contains(new HitBlocked(2, Ally, Colossus), events);
        Assert.Contains(new DomeEnded(2, Colossus), events);
    }

    [Theory]
    [InlineData("""{ "id": "x", "weight": "light", "range": 3, "minDamage": 5, "maxDamage": 5, "cooldownMs": 0, "zone": { "shape": "square", "size": 1, "throughWalls": false }, "createsDome": true }""")]
    [InlineData("""{ "id": "x", "weight": "light", "range": 1, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "zone": { "shape": "line", "size": 3, "throughWalls": false }, "createsDome": true }""")]
    [InlineData("""{ "id": "x", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "createsDome": true }""")]
    [InlineData("""{ "id": "x", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "chargeMs": 500, "zone": { "shape": "square", "size": 1, "throughWalls": false }, "createsDome": true }""")]
    public void InvalidDomes_AreRejectedAtLoad(string skill)
    {
        Assert.Throws<ArgumentException>(() => SkillDefinition.ListFromJson($"[{skill}]"));
    }
}
