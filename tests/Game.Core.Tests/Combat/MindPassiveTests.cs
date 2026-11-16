using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Passifs du télépathe : Contagion, Impact, Présence, Ange gardien.</summary>
public class MindPassiveTests
{
    private static readonly UnitId Telepath = new(1);
    private static readonly UnitId Ally = new(2);
    private static readonly UnitId Foe = new(9);

    /// <summary>Étourdit 2 s (40 ticks), sans dégâts.</summary>
    private static readonly SkillDefinition Paralysis = new()
    {
        Id = "paralysis", Weight = SkillWeight.Light, Range = 5, MinDamage = 0, MaxDamage = 0, CooldownMs = 0, IsMental = true,
        Effects = new[] { new StatusEffect { Status = StatusKind.Stunned, DurationMs = 2000 } },
    };

    /// <summary>Pousse un ennemi de 2 cases, sans dégâts de collision.</summary>
    private static readonly SkillDefinition Shove = new()
    {
        Id = "shove", Weight = SkillWeight.Light, Range = 5, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
        ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Push, Distance = 2 },
    };

    private static readonly SkillDefinition Lethal = Smash with { MinDamage = 50, MaxDamage = 50, CooldownMs = 0 };

    /// <summary>
    /// Le télépathe (Contagion ou non) paralyse l'ennemi 8, que l'allié achève dans le même tick ; l'ennemi 9 est plus
    /// proche de lui que le 7, et le robot 6 plus proche encore.
    /// </summary>
    private static (CombatSimulation Sim, IReadOnlyList<CombatEvent> Events) StunThenKill(PassiveKind? passive)
    {
        var sim = Simulation(new Grid(9, 2),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { Paralysis }, passive: passive),
            Unit(2, team: 0, 3, 1, gauge: Capacity, skills: new[] { Lethal }),
            Unit(8, team: 1, 3, 0, hp: 30), Unit(6, team: 1, 4, 1, isRobot: true), Unit(9, team: 1, 5, 0), Unit(7, team: 1, 8, 1));
        sim.Submit(Use(Telepath, "paralysis", new UnitId(8)));
        sim.Submit(Use(Ally, "smash", new UnitId(8)));
        return (sim, sim.Tick());
    }

    [Fact]
    public void Contagion_TheParalysisOfAFallenEnemy_JumpsToTheNearestOne_ForWhatIsLeft()
    {
        var (sim, events) = StunThenKill(PassiveKind.Contagion);

        var end = events.OfType<StatusApplied>().First(a => a.Unit.Value == 8).EndTick;
        Assert.Contains(new StatusSpread(1, new UnitId(8), Foe, StatusKind.Stunned), events);
        Assert.Equal(end, sim.Get(9).StatusEndTicks[StatusKind.Stunned]);
        Assert.False(sim.Get(6).HasStatus(StatusKind.Stunned)); // un robot : insensible aux pouvoirs mentaux
        Assert.Equal(Telepath, sim.Get(9).StatusSource(StatusKind.Stunned));
    }

    [Fact]
    public void WithoutContagion_TheParalysisEndsWithTheFallenEnemy()
    {
        var (sim, events) = StunThenKill(passive: null);

        Assert.DoesNotContain(events, e => e is StatusSpread);
        Assert.False(sim.Get(9).HasStatus(StatusKind.Stunned));
    }

    [Fact]
    public void Impact_AnEnemyMovedByForce_TakesDamageOnArrival()
    {
        var sim = Simulation(new Grid(8, 1), Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { Shove }, passive: PassiveKind.Impact),
            Unit(9, team: 1, 2, 0));

        sim.Submit(Use(Telepath, "shove", Foe));
        sim.Tick();

        Assert.Equal((new Cell(4, 0), 95), (sim.Get(9).Position, sim.Get(9).Hp));
    }

    [Fact]
    public void Contagion_AConfusionJumps_AndRelaysWhenItsNewBearerFalls_ButNotOnceTheTelepathIsDown()
    {
        var confusion = Paralysis with
        {
            Id = "confusion", Effects = new[] { new StatusEffect { Status = StatusKind.Confused, DurationMs = 5000 } },
        };
        var sim = Simulation(new Grid(9, 2),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { confusion }, passive: PassiveKind.Contagion, onDefeat: DefeatOutcome.Downed),
            Unit(2, team: 0, 3, 1, gauge: Capacity, initiative: 100, skills: new[] { Lethal }),
            Unit(7, team: 1, 3, 0, hp: 30), Unit(8, team: 1, 4, 0, hp: 30), Unit(9, team: 1, 6, 0));
        sim.Submit(Use(Telepath, "confusion", new UnitId(7)));
        sim.Submit(Use(Ally, "smash", new UnitId(7)));
        sim.Tick();
        Assert.True(sim.Get(8).HasStatus(StatusKind.Confused));

        sim.Submit(Use(Ally, "smash", new UnitId(8))); // l'allié rejoue au tick suivant (jauge rapide)
        var events = sim.TickTimes(2);

        Assert.Contains(new StatusSpread(events.OfType<StatusSpread>().Single().Tick, new UnitId(8), Foe, StatusKind.Confused), events);
        Assert.True(sim.Get(9).HasStatus(StatusKind.Confused));
    }

    [Fact]
    public void Contagion_NeedsTheTelepathStanding()
    {
        var sim = Simulation(new Grid(9, 2),
            Unit(1, team: 0, 0, 0, gauge: Capacity, hp: 10, skills: new[] { Paralysis }, passive: PassiveKind.Contagion,
                onDefeat: DefeatOutcome.Downed),
            Unit(2, team: 0, 3, 1, gauge: Capacity, initiative: 100, skills: new[] { Lethal }),
            Unit(6, team: 1, 0, 1, gauge: Capacity, skills: new[] { Lethal }),
            Unit(8, team: 1, 3, 0, hp: 30), Unit(9, team: 1, 5, 0));
        sim.Submit(Use(Telepath, "paralysis", new UnitId(8)));
        sim.Tick();
        sim.Submit(Use(new UnitId(6), "smash", Telepath)); // le télépathe tombe
        sim.Submit(Use(Ally, "smash", new UnitId(8)));
        var events = sim.Tick();

        Assert.Equal(UnitStatus.Downed, sim.Get(1).Status);
        Assert.DoesNotContain(events, e => e is StatusSpread);
    }

    [Fact]
    public void Impact_AlsoHitsEnemiesPushedByAZone_NotTheAllies()
    {
        var wave = Shove with
        {
            Id = "wave", Range = 0, Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        };
        var sim = Simulation(new Grid(8, 1), Unit(1, team: 0, 3, 0, gauge: Capacity, skills: new[] { wave }, passive: PassiveKind.Impact),
            Unit(2, team: 0, 2, 0), Unit(9, team: 1, 4, 0));

        sim.Submit(Use(Telepath, "wave", cell: new Cell(3, 0)));
        sim.Tick();

        Assert.Equal((new Cell(6, 0), 95), (sim.Get(9).Position, sim.Get(9).Hp));
        Assert.Equal((new Cell(0, 0), 100), (sim.Get(2).Position, sim.Get(2).Hp));
    }

    [Fact]
    public void Impact_AnEnemyThatDoesNotMove_TakesNothing()
    {
        var sim = Simulation(Grid.FromRows("...#...."), Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { Shove }, passive: PassiveKind.Impact),
            Unit(9, team: 1, 2, 0));

        sim.Submit(Use(Telepath, "shove", Foe));
        sim.Tick();

        Assert.Equal((new Cell(2, 0), 100), (sim.Get(9).Position, sim.Get(9).Hp));
    }

    [Fact]
    public void Presence_AfterEachAction_NearbyWoundedAlliesRecoverALittle()
    {
        var sim = Simulation(new Grid(8, 1), Unit(1, team: 0, 0, 0, gauge: Capacity, passive: PassiveKind.Presence),
            Unit(2, team: 0, 2, 0), Unit(3, team: 0, 3, 0), Unit(9, team: 1, 1, 0));
        sim.Get(1).Hp = 50;
        sim.Get(2).Hp = 50;
        sim.Get(3).Hp = 50; // trop loin

        sim.Submit(Use(Telepath, "strike", Foe));
        sim.Tick();

        Assert.Equal((50, 54, 50), (sim.Get(1).Hp, sim.Get(2).Hp, sim.Get(3).Hp));
    }

    [Fact]
    public void Presence_ADisplacementAlone_HealsNobody()
    {
        var sim = Simulation(new Grid(8, 1), Unit(1, team: 0, 0, 0, gauge: Capacity, passive: PassiveKind.Presence),
            Unit(2, team: 0, 2, 0), Unit(9, team: 1, 7, 0));
        sim.Get(2).Hp = 50;

        sim.Submit(new ActivateCommand(Telepath, new Cell(1, 0), null));
        sim.Tick();

        Assert.Equal(50, sim.Get(2).Hp);
    }

    [Fact]
    public void GuardianAngel_OncePerFight_ANearbyAllyWhoShouldFall_StaysAtOneHp()
    {
        var sim = Simulation(new Grid(8, 2), Unit(1, team: 0, 0, 0, passive: PassiveKind.GuardianAngel),
            Unit(2, team: 0, 3, 0, onDefeat: DefeatOutcome.Downed),
            Unit(9, team: 1, 4, 0, gauge: Capacity, initiative: 100, skills: new[] { Lethal }));
        sim.Get(2).Hp = 40;

        sim.Submit(Use(Foe, "smash", Ally));
        var events = sim.Tick();
        Assert.Equal((UnitStatus.Active, 1), (sim.Get(2).Status, sim.Get(2).Hp));
        Assert.Contains(new GuardianAngelSaved(1, Ally, Telepath), events);

        sim.Submit(Use(Foe, "smash", Ally)); // part dès que la jauge est de nouveau pleine
        sim.TickTimes(2);
        Assert.Equal(UnitStatus.Downed, sim.Get(2).Status);
    }

    [Fact]
    public void GuardianAngel_DoesNotSaveTheTelepathHimself_NorAFarAlly()
    {
        var sim = Simulation(new Grid(8, 2), Unit(1, team: 0, 0, 0, passive: PassiveKind.GuardianAngel, onDefeat: DefeatOutcome.Downed),
            Unit(2, team: 0, 5, 0, onDefeat: DefeatOutcome.Downed),
            Unit(8, team: 1, 1, 0, gauge: Capacity, skills: new[] { Lethal }), Unit(9, team: 1, 6, 0, gauge: Capacity, skills: new[] { Lethal }));
        sim.Get(1).Hp = 40;
        sim.Get(2).Hp = 40;

        sim.Submit(Use(new UnitId(8), "smash", Telepath));
        sim.Submit(Use(Foe, "smash", Ally));
        sim.Tick();

        Assert.Equal((UnitStatus.Downed, UnitStatus.Downed), (sim.Get(1).Status, sim.Get(2).Status));
    }
}
