using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Zones laissées au sol : fumée, pare-tirs, sols qui posent un état. (Pièges : <see cref="TrapTests"/>.)</summary>
public class GroundZoneTests
{
    private static readonly UnitId Caster = new(1);
    private static readonly UnitId Enemy = new(2);

    /// <summary>Tir à 6 cases, 10 dégâts.</summary>
    private static readonly SkillDefinition Shot = Strike with { Id = "shot", Range = 6 };

    /// <summary>Compétence « lay » : pose la zone sur un carré de rayon 1, à 4 cases au plus.</summary>
    private static SkillDefinition Laying(GroundZoneDefinition ground) => new()
    {
        Id = "lay", Weight = SkillWeight.Light, Range = 4, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        Ground = ground,
    };

    private static readonly GroundZoneDefinition Smoke = new() { DurationMs = 1000, BlocksSight = true };
    private static readonly GroundZoneDefinition WindWall = new() { DurationMs = 1000, BlocksShots = true };

    private static StatusEffect For(StatusKind status, int ms) => new() { Status = status, DurationMs = ms };

    private static UnitSetup CasterAt(int x, int y, params SkillDefinition[] skills) =>
        Unit(1, team: 0, x, y, gauge: Capacity, skills: skills);

    private static ActivateCommand Lay(int x, int y) => Use(Caster, "lay", cell: new Cell(x, y));

    private static RejectionReason? CanUse(CombatSimulation sim, UnitId unit, string skill, UnitId target) =>
        sim.Check(Use(unit, skill, target), ignoreTiming: true);

    [Fact]
    public void Smoke_CutsEveryLineOfSightThroughIt_UntilItEnds()
    {
        var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, Laying(Smoke), Shot), Unit(2, team: 1, 6, 0, skills: new[] { Shot }));

        Assert.Null(CanUse(sim, Caster, "shot", Enemy));
        sim.Submit(Lay(3, 0));
        var events = sim.Tick();

        var zone = Assert.Single(sim.GroundZones);
        Assert.Equal(new[] { new Cell(2, 0), new Cell(3, 0), new Cell(4, 0) }, zone.Cells);
        Assert.Contains(new GroundZoneCreated(1, zone.Id, Caster, "lay", zone.Cells, 21), events);
        Assert.Equal(RejectionReason.NoLineOfSight, CanUse(sim, Caster, "shot", Enemy));
        Assert.Equal(RejectionReason.NoLineOfSight, CanUse(sim, Enemy, "shot", Caster));

        var later = sim.TickTimes(20);

        Assert.Contains(new GroundZoneEnded(21, zone.Id), later);
        Assert.Empty(sim.GroundZones);
        Assert.Null(CanUse(sim, Caster, "shot", Enemy));
    }

    [Fact]
    public void Smoke_HidesThoseInsideFromAfar_ButNotAtContact()
    {
        // L'ennemi 2 est dans la fumée, au contact du lanceur ; l'ennemi 3 est juste derrière, à deux cases.
        var sim = Simulation(new Grid(8, 2),
            CasterAt(1, 0, Laying(Smoke), Shot), Unit(2, team: 1, 2, 0), Unit(3, team: 1, 3, 1));

        sim.Submit(Lay(3, 0));
        sim.Tick();

        Assert.Null(CanUse(sim, Caster, "shot", Enemy));
        Assert.Equal(RejectionReason.NoLineOfSight, CanUse(sim, Caster, "shot", new UnitId(3)));
    }

    [Fact]
    public void ASkillThatClearsSmoke_RemovesTheSmokeItTouches_NotTheOtherZones()
    {
        var gust = Strike with
        {
            Id = "gust", Range = 4, Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 0, ThroughWalls = false }, ClearsSmoke = true,
        };
        // La fumée en haut (lignes 0 et 1), le pare-tirs en bas (lignes 3 et 4).
        var sim = Simulation(new Grid(8, 5),
            CasterAt(0, 0, Laying(Smoke)),
            Unit(3, team: 0, 0, 4, gauge: Capacity, skills: new[] { Laying(WindWall) }),
            Unit(4, team: 0, 7, 0, gauge: Capacity, skills: new[] { gust }));
        sim.Submit(Lay(3, 0));
        sim.Submit(Use(new UnitId(3), "lay", cell: new Cell(3, 4)));
        sim.Tick();
        Assert.Equal(2, sim.GroundZones.Count);

        // Visée dans la fumée même : une compétence qui la dissipe n'a pas besoin d'y voir.
        sim.Submit(Use(new UnitId(4), "gust", cell: new Cell(4, 0)));
        sim.Tick();

        Assert.True(Assert.Single(sim.GroundZones).Definition.BlocksShots);
    }

    [Fact]
    public void AWindWall_StopsShots_ButNotMentalPowers()
    {
        var mind = new SkillDefinition
        {
            Id = "mind", Weight = SkillWeight.Light, Range = 6, MinDamage = 0, MaxDamage = 0, CooldownMs = 0, IsMental = true,
            Effects = new[] { For(StatusKind.Stunned, 1000) },
        };
        var sim = Simulation(new Grid(8, 1),
            CasterAt(0, 0, Laying(WindWall), Shot, mind), Unit(2, team: 1, 6, 0, skills: new[] { Shot }));

        sim.Submit(Lay(3, 0));
        sim.Tick();

        Assert.Equal(RejectionReason.NoLineOfSight, CanUse(sim, Caster, "shot", Enemy));
        Assert.Equal(RejectionReason.NoLineOfSight, CanUse(sim, Enemy, "shot", Caster));
        Assert.Null(CanUse(sim, Caster, "mind", Enemy));
    }

    [Fact]
    public void AWindWall_DoesNotStopWhatOnlyActsOnTheGround()
    {
        // Poser une fumée à travers un pare-tirs n'est pas un tir.
        var sim = Simulation(new Grid(8, 1),
            CasterAt(0, 0, Laying(WindWall)), Unit(3, team: 0, 1, 0, gauge: Capacity, skills: new[] { Laying(Smoke) }));
        sim.Submit(Lay(3, 0));
        sim.Tick();

        Assert.Null(sim.Check(Use(new UnitId(3), "lay", cell: new Cell(5, 0))));
    }

    [Fact]
    public void SmokeAndWindWalls_StopABeam_AlsoWhenItIsFiredFromInside()
    {
        var beam = Strike with { Id = "beam", Zone = new ZoneDefinition { Shape = ZoneShape.Line, Size = 6, ThroughWalls = false } };
        var aim = new SkillUse("beam", null, new Cell(1, 0));
        foreach (var ground in new[] { Smoke, WindWall })
        {
            var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, Laying(ground), beam), Unit(2, team: 1, 6, 0));
            sim.Submit(Lay(4, 0));
            sim.Tick();
            Assert.Equal(new[] { new Cell(1, 0), new Cell(2, 0) }, sim.PreviewAction(Caster, new Cell(0, 0), aim).Cells);

            // Depuis une case de la zone : la ligne ne part pas.
            Assert.Null(sim.PreviewAction(Caster, new Cell(3, 0), new SkillUse("beam", null, new Cell(2, 0))).Cells);
        }
    }

    [Fact]
    public void ADome_BlocksTheStatusesOfAGround_AndFalls()
    {
        var frost = new GroundZoneDefinition { DurationMs = 2000, Effects = new[] { For(StatusKind.Immobilized, 200) } };
        var dome = new SkillDefinition
        {
            Id = "dome", Weight = SkillWeight.Light, Range = 0, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false }, CreatesDome = true,
        };
        var sim = Simulation(new Grid(8, 1),
            CasterAt(0, 0, Laying(frost)), Unit(2, team: 1, 3, 0, gauge: Capacity, skills: new[] { dome }), Unit(3, team: 1, 4, 0));
        sim.Submit(Use(Enemy, "dome", cell: new Cell(3, 0)));
        sim.Tick();

        sim.Submit(Lay(3, 0));
        var events = sim.Tick();

        Assert.False(sim.Get(2).HasStatus(StatusKind.Immobilized));
        Assert.False(sim.Get(3).HasStatus(StatusKind.Immobilized));
        Assert.Contains(events, e => e is DomeEnded);
    }

    [Fact]
    public void AHeroStoppedBeforeHisDestination_LosesHisAction()
    {
        var frost = new GroundZoneDefinition { DurationMs = 2000, Effects = new[] { For(StatusKind.Immobilized, 200) } };
        // Le héros 3 fait un pas dans la zone (cases 2 à 4) pour frapper l'ennemi 2, qui est juste derrière.
        var sim = Simulation(new Grid(8, 1),
            CasterAt(0, 0, Laying(frost)), Unit(3, team: 0, 1, 0, gauge: Capacity), Unit(2, team: 1, 5, 0));
        sim.Submit(Lay(3, 0));
        sim.Tick();
        sim.TickTimes(5); // l'ennemi n'est pas dans la zone

        sim.Submit(new ActivateCommand(new UnitId(3), new Cell(4, 0), new SkillUse("strike", Enemy)));
        var events = sim.Tick();

        Assert.DoesNotContain(events, e => e is CommandRejected);
        // Arrêté sur la première case gelée (2,0), pas encore à destination : l'action est perdue.
        Assert.Equal(new Cell(2, 0), sim.Get(3).Position);
        Assert.Equal(100, sim.Get(2).Hp);
    }

    [Fact]
    public void AHeroImmobilizedOnHisDestination_StillActs()
    {
        var frost = new GroundZoneDefinition { DurationMs = 2000, Effects = new[] { For(StatusKind.Immobilized, 200) } };
        // Le héros 3 fait un seul pas, qui l'amène sur la première case gelée, au contact de l'ennemi 2.
        var covering = Simulation(new Grid(8, 1),
            CasterAt(0, 0, Laying(frost)), Unit(3, team: 0, 2, 0, gauge: Capacity), Unit(2, team: 1, 4, 0));
        covering.Submit(Lay(4, 0)); // cases 3 à 5
        covering.Tick();
        covering.Submit(new ActivateCommand(new UnitId(3), new Cell(3, 0), new SkillUse("strike", Enemy)));
        covering.Tick();

        Assert.True(covering.Get(3).HasStatus(StatusKind.Immobilized));
        Assert.Equal(new Cell(3, 0), covering.Get(3).Position);
        Assert.Equal(90, covering.Get(2).Hp); // arrivé, il a frappé
    }

    [Fact]
    public void AWindWall_StopsABeamAtItsFirstCell()
    {
        var beam = Strike with { Id = "beam", Zone = new ZoneDefinition { Shape = ZoneShape.Line, Size = 6, ThroughWalls = false } };
        var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, Laying(WindWall), beam), Unit(2, team: 1, 6, 0));
        var aim = new SkillUse("beam", null, new Cell(1, 0));

        Assert.Equal(6, sim.PreviewAction(Caster, new Cell(0, 0), aim).Cells!.Count);
        sim.Submit(Lay(4, 0));
        sim.Tick();

        Assert.Equal(new[] { new Cell(1, 0), new Cell(2, 0) }, sim.PreviewAction(Caster, new Cell(0, 0), aim).Cells);
    }

    [Fact]
    public void ARenewingGround_KeepsItsStatus_OnEveryoneInside_WhileTheyStay()
    {
        // Déséquilibré 100 ms (2 ticks), reposé tant que l'unité reste ; la zone dure 1 s (20 ticks).
        var gas = new GroundZoneDefinition { DurationMs = 1000, Renews = true, Effects = new[] { For(StatusKind.OffBalance, 100) } };
        var sim = Simulation(new Grid(8, 1),
            CasterAt(0, 0, Laying(gas)), Unit(2, team: 1, 3, 0), Unit(3, team: 0, 4, 0), Unit(4, team: 1, 6, 0));

        sim.Submit(Lay(3, 0));
        sim.Tick();

        for (var tick = 2; tick <= 20; tick++)
        {
            sim.Tick();
            Assert.True(sim.Get(2).HasStatus(StatusKind.OffBalance), $"tick {tick}");
        }
        Assert.True(sim.Get(3).HasStatus(StatusKind.OffBalance)); // l'allié du lanceur aussi
        Assert.False(sim.Get(4).HasStatus(StatusKind.OffBalance)); // hors de la zone

        sim.TickTimes(3); // la zone a disparu au tick 21 : l'état s'éteint de lui-même

        Assert.False(sim.Get(2).HasStatus(StatusKind.OffBalance));
    }

    [Fact]
    public void AGround_CatchesWhoWalksIn_EvenJustPassingThrough()
    {
        var gas = new GroundZoneDefinition { DurationMs = 1000, Renews = true, Effects = new[] { For(StatusKind.OffBalance, 100) } };
        var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, Laying(gas)), Unit(2, team: 1, 7, 0, gauge: Capacity, mobility: 6));
        sim.Submit(Lay(4, 0));
        sim.Tick();

        // De (7,0) à (1,0) : il traverse la zone (cases 3 à 5) sans s'y arrêter.
        sim.Submit(new ActivateCommand(Enemy, new Cell(1, 0)));
        sim.Tick();

        Assert.Equal(new Cell(1, 0), sim.Get(2).Position);
        Assert.True(sim.Get(2).HasStatus(StatusKind.OffBalance));
    }

    [Fact]
    public void AGroundThatDoesNotRenew_StrikesOnEntryOnly()
    {
        // Immobilisé 200 ms (4 ticks) à l'entrée ; la zone dure 2 s.
        var frost = new GroundZoneDefinition { DurationMs = 2000, Effects = new[] { For(StatusKind.Immobilized, 200) } };
        var sim = Simulation(new Grid(8, 2),
            CasterAt(0, 0, Laying(frost)), Unit(2, team: 1, 3, 0), Unit(3, team: 1, 7, 1, gauge: Capacity, mobility: 6));

        sim.Submit(Lay(3, 0));
        sim.Tick();
        Assert.True(sim.Get(2).HasStatus(StatusKind.Immobilized)); // déjà dedans quand la zone apparaît

        // L'autre veut traverser la zone (colonnes 2 à 4) : il est arrêté sur la première case gelée.
        sim.Submit(new ActivateCommand(new UnitId(3), new Cell(1, 1)));
        var events = sim.Tick();

        Assert.Contains(new UnitStopped(2, new UnitId(3), new Cell(4, 1)), events);
        Assert.Equal(new Cell(4, 1), sim.Get(3).Position);

        sim.TickTimes(6);

        // Libérés, ils ne sont pas repris tant qu'ils ne ressortent pas.
        Assert.False(sim.Get(2).HasStatus(StatusKind.Immobilized));
        Assert.False(sim.Get(3).HasStatus(StatusKind.Immobilized));
        Assert.Single(sim.GroundZones);
    }

    [Fact]
    public void AnEnemiesOnlyGround_SparesTheSideThatLaidIt()
    {
        var frost = new GroundZoneDefinition
        {
            DurationMs = 2000, Affects = ZoneTargets.Enemies, Effects = new[] { For(StatusKind.Immobilized, 200) },
        };
        var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, Laying(frost)), Unit(2, team: 1, 3, 0), Unit(3, team: 0, 4, 0));

        sim.Submit(Lay(3, 0));
        sim.Tick();

        Assert.True(sim.Get(2).HasStatus(StatusKind.Immobilized));
        Assert.False(sim.Get(3).HasStatus(StatusKind.Immobilized));
    }

    [Fact]
    public void AGroundSkill_ReplaysIdentically()
    {
        var gas = new GroundZoneDefinition { DurationMs = 1000, Renews = true, Effects = new[] { For(StatusKind.OffBalance, 100) } };
        var setup = Setup(new Grid(8, 1), CasterAt(0, 0, Laying(gas)), Unit(2, team: 1, 3, 0));
        var sim = new CombatSimulation(setup);
        sim.Submit(Lay(3, 0));
        sim.TickTimes(30);

        var replay = CombatSimulation.Replay(setup, sim.CommandLog, 30);

        Assert.Equal(sim.Checksum(), replay.Checksum());
        Assert.Equal(sim.EventLog, replay.EventLog);
    }

    [Theory]
    // Sans zone ; sans durée ; qui ne fait rien ; réservée aux alliés ; dégâts hors piège ; renouvelée sans état.
    [InlineData(""" "range": 4, "minDamage": 5, "maxDamage": 5, "ground": { "durationMs": 1000, "blocksSight": true } """)]
    [InlineData(""" "range": 4, "minDamage": 0, "maxDamage": 0, "zone": { "shape": "square", "size": 1, "throughWalls": false }, "ground": { "blocksSight": true } """)]
    [InlineData(""" "range": 4, "minDamage": 0, "maxDamage": 0, "zone": { "shape": "square", "size": 1, "throughWalls": false }, "ground": { "durationMs": 1000 } """)]
    [InlineData(""" "range": 4, "minDamage": 0, "maxDamage": 0, "zone": { "shape": "square", "size": 1, "throughWalls": false }, "ground": { "durationMs": 1000, "blocksSight": true, "affects": "allies" } """)]
    [InlineData(""" "range": 4, "minDamage": 0, "maxDamage": 0, "zone": { "shape": "square", "size": 1, "throughWalls": false }, "ground": { "durationMs": 1000, "blocksSight": true, "minDamage": 1, "maxDamage": 2 } """)]
    [InlineData(""" "range": 4, "minDamage": 0, "maxDamage": 0, "zone": { "shape": "square", "size": 1, "throughWalls": false }, "ground": { "durationMs": 1000, "blocksSight": true, "renews": true } """)]
    // Avec un dôme, ou dans une zone de soutien.
    [InlineData(""" "range": 0, "minDamage": 0, "maxDamage": 0, "zone": { "shape": "square", "size": 1, "throughWalls": false }, "createsDome": true, "ground": { "durationMs": 1000, "blocksSight": true } """)]
    [InlineData(""" "range": 4, "minDamage": 0, "maxDamage": 0, "zone": { "shape": "square", "size": 1, "throughWalls": false, "affects": "allies" }, "ground": { "durationMs": 1000, "blocksSight": true } """)]
    // Dissiper la fumée sans zone.
    [InlineData(""" "range": 4, "minDamage": 5, "maxDamage": 5, "clearsSmoke": true """)]
    public void InvalidGroundSkills_AreRejectedAtLoad(string fields)
    {
        Assert.Throws<ArgumentException>(() =>
            SkillDefinition.ListFromJson($$"""[ { "id": "x", "weight": "light", "cooldownMs": 0, {{fields}} } ]"""));
    }
}
