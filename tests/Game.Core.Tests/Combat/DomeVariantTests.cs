using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Variantes du dôme : la Forteresse (tenue pendant une charge) et le Bouclier personnel (qui suit un allié).</summary>
public class DomeVariantTests
{
    private static readonly UnitId Colossus = new(1);
    private static readonly UnitId Ally = new(2);
    private static readonly UnitId Foe = new(9);

    /// <summary>Carré de rayon 2 autour du lanceur, tenu 1 s (20 ticks).</summary>
    private static readonly SkillDefinition Fortress = new()
    {
        Id = "fortress", Weight = SkillWeight.Heavy, Range = 0, MinDamage = 0, MaxDamage = 0, CooldownMs = 0, ChargeMs = 1000,
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 2, ThroughWalls = false },
        CreatesDome = true, HoldsDome = true,
    };

    private static readonly SkillDefinition Shield = new()
    {
        Id = "shield", Target = SkillTargeting.Ally, Weight = SkillWeight.Light, Range = 3, MinDamage = 0, MaxDamage = 0,
        CooldownMs = 0, CreatesDome = true,
    };

    /// <summary>Zone 3x3 autour de soi, 10 dégâts, étourdit.</summary>
    private static readonly SkillDefinition Quake = Strike with
    {
        Id = "quake", Range = 0,
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        Effects = new[] { new StatusEffect { Status = StatusKind.Stunned, DurationMs = 500 } },
    };

    private static UnitSetup Holder(SkillDefinition skill, int x = 4, int y = 2) =>
        Unit(1, team: 0, x, y, gauge: Capacity, skills: new[] { skill });

    private static UnitSetup Attacker(int id, int x, int y, params SkillDefinition[] skills) =>
        Unit(id, team: 1, x, y, gauge: Capacity, initiative: Capacity, skills: skills.Length > 0 ? skills : null);

    private static CombatSimulation WithFortressRaised(params UnitSetup[] others)
    {
        var sim = Simulation(new Grid(9, 5), new[] { Holder(Fortress) }.Concat(others).ToArray());
        sim.Submit(Use(Colossus, "fortress", cell: new Cell(4, 2)));
        Assert.Contains(sim.Tick(), e => e is DomeRaised { Owner.Value: 1, Cells.Count: 25 });
        return sim;
    }

    [Fact]
    public void Fortress_BlocksEveryAttack_OnTheAlliesUnderIt_WithoutFalling()
    {
        var sim = WithFortressRaised(Unit(2, team: 0, 3, 2), Attacker(9, 2, 2));

        for (var tick = 2; tick <= 4; tick++)
        {
            sim.Submit(Use(Foe, "strike", Ally));
            var events = sim.Tick();
            Assert.Contains(new HitBlocked(tick, Ally, Colossus), events);
            Assert.DoesNotContain(events, e => e is DomeEnded);
        }
        Assert.Equal(100, sim.Get(2).Hp);
    }

    [Fact]
    public void Fortress_DoesNotProtectItsHolder_WhoStaysStillWithAnEmptyGauge()
    {
        var sim = WithFortressRaised(Attacker(9, 5, 2));

        sim.Submit(Use(Foe, "strike", Colossus));
        sim.Tick();

        Assert.Equal(90, sim.Get(1).Hp);
        Assert.Equal(0, sim.Get(1).Gauge);
        Assert.NotNull(sim.Get(1).Charge);
        Assert.Equal(RejectionReason.Charging, sim.Check(new ActivateCommand(Colossus, new Cell(4, 3), null)));
    }

    [Fact]
    public void Fortress_FallsAtTheEndOfItsCharge_WithoutAnythingElse()
    {
        var sim = WithFortressRaised(Unit(2, team: 0, 3, 2));

        var events = sim.TickTimes(20);

        Assert.Contains(new DomeEnded(21, Colossus), events);
        Assert.DoesNotContain(events, e => e is SkillUsed);
        Assert.Null(sim.Get(1).Charge);
        Assert.Empty(sim.DomeCells);
    }

    [Fact]
    public void Fortress_StunnedHolder_LetsGo_AfterTheSourceThatStunnedHim()
    {
        // Le séisme touche le colosse (étourdi : la forteresse est lâchée) puis l'allié, encore protégé pour cette source.
        var sim = WithFortressRaised(Unit(2, team: 0, 4, 3), Attacker(9, 5, 2, Quake, Strike));

        sim.Submit(Use(Foe, "quake", cell: new Cell(5, 2)));
        var events = sim.Tick();

        Assert.Contains(events, e => e is StatusApplied { Unit.Value: 1, Status: StatusKind.Stunned });
        Assert.Contains(new HitBlocked(2, Ally, Colossus), events);
        Assert.Contains(new DomeEnded(2, Colossus), events);
        Assert.Equal(100, sim.Get(2).Hp);

        sim.Submit(Use(Foe, "strike", Ally));
        Assert.Contains(new UnitDamaged(3, Ally, 10, 90), sim.Tick());
    }

    [Fact]
    public void Fortress_FallsWhenItsHolderIsPushed()
    {
        var shove = Strike with { Id = "shove", ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Push, Distance = 1 } };
        var sim = WithFortressRaised(Attacker(9, 5, 2, shove));

        sim.Submit(Use(Foe, "shove", Colossus));
        var events = sim.Tick();

        Assert.Contains(new UnitDisplaced(2, Colossus, new Cell(4, 2), new Cell(3, 2)), events);
        Assert.Contains(new DomeEnded(2, Colossus), events);
    }

    [Fact]
    public void PersonalShield_FollowsItsAlly_AndBlocksOneAttack()
    {
        var sim = Simulation(new Grid(8, 1),
            Holder(Shield, 0, 0), Unit(2, team: 0, 2, 0, gauge: Capacity), Attacker(9, 6, 0));

        sim.Submit(Use(Colossus, "shield", Ally));
        Assert.Contains(new DomeRaised(1, Colossus, new[] { new Cell(2, 0) }, Ally), sim.Tick());

        sim.Submit(new ActivateCommand(Ally, new Cell(5, 0), null));
        sim.Tick();
        Assert.Equal(new[] { new Cell(5, 0) }, sim.DomeCells);

        sim.Submit(Use(Foe, "strike", Ally));
        var events = sim.Tick();
        Assert.Contains(new HitBlocked(3, Ally, Colossus), events);
        Assert.Contains(new DomeEnded(3, Colossus), events);

        sim.Submit(Use(Foe, "strike", Ally));
        Assert.Contains(new UnitDamaged(4, Ally, 10, 90), sim.Tick());
    }

    [Fact]
    public void PersonalShield_GoesOnAnAlly_NotOnItsCasterNorOnAnEnemy()
    {
        var sim = Simulation(new Grid(8, 1), Holder(Shield, 0, 0), Unit(2, team: 0, 2, 0), Unit(9, team: 1, 3, 0));

        Assert.Null(sim.Check(Use(Colossus, "shield", Ally)));
        Assert.Equal(RejectionReason.InvalidTarget, sim.Check(Use(Colossus, "shield", Colossus)));
        Assert.Equal(RejectionReason.InvalidTarget, sim.Check(Use(Colossus, "shield", Foe)));
    }

    [Fact]
    public void Fortress_DoesNotShowItsHoldersCellAsProtected()
    {
        var sim = WithFortressRaised();

        Assert.Equal(24, sim.DomeCells.Count());
        Assert.DoesNotContain(new Cell(4, 2), sim.DomeCells);
    }

    [Fact]
    public void ABodyguard_DoesNotStepInFrontOfAnAttackHisShieldBlocks()
    {
        var sim = Simulation(new Grid(8, 1),
            Unit(1, team: 0, 1, 0, gauge: Capacity, skills: new[] { Shield }, passive: PassiveKind.Bodyguard),
            Unit(2, team: 0, 2, 0), Attacker(9, 3, 0));
        sim.Submit(Use(Colossus, "shield", Ally));
        sim.Tick();

        sim.Submit(Use(Foe, "strike", Ally));
        var events = sim.Tick();

        Assert.Contains(new HitBlocked(2, Ally, Colossus), events);
        Assert.DoesNotContain(events, e => e is AttackIntercepted);
        Assert.Equal((100, 100), (sim.Get(1).Hp, sim.Get(2).Hp));
    }

    [Fact]
    public void ADominatedColossus_LosesHisShield()
    {
        var dominate = new SkillDefinition
        {
            Id = "dominate", Weight = SkillWeight.Light, Range = 5, MinDamage = 0, MaxDamage = 0, CooldownMs = 0, IsMental = true,
            Effects = new[] { new StatusEffect { Status = StatusKind.Dominated, DurationMs = 1000 } },
        };
        var sim = Simulation(new Grid(8, 2), Holder(Shield, 0, 0), Unit(2, team: 0, 2, 1), Attacker(9, 5, 0, dominate));
        sim.Submit(Use(Colossus, "shield", Ally));
        sim.Tick();

        sim.Submit(Use(Foe, "dominate", Colossus));

        Assert.Contains(new DomeEnded(2, Colossus), sim.Tick());
        Assert.Empty(sim.DomeCells);
    }

    [Fact]
    public void ADomeDefinition_IsAnInstantZone_OrAShieldOnAnAlly_OrAHeldFortress()
    {
        Fortress.Validate();
        Shield.Validate();
        Assert.Throws<ArgumentException>(() => (Fortress with { HoldsDome = false }).Validate());
        Assert.Throws<ArgumentException>(() => (Fortress with { ChargeMs = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (Fortress with { Range = 2 }).Validate());
        Assert.Throws<ArgumentException>(() => (Shield with { Target = SkillTargeting.Enemy }).Validate());
    }
}
