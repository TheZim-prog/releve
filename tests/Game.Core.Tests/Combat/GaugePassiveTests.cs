using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>
/// Passifs qui jouent sur la jauge. Règles de test : capacité 100 ; Voleur de temps 10 % ; Sur sa lancée 40 % ;
/// Récupération d'énergie 10 % ; Sillage 5 % ; auras à 2 cases, Commandement x1,5 et Champ de brouillage x0,5.
/// Les unités dont on observe la jauge ont une initiative de 0, sauf pour les auras.
/// </summary>
public class GaugePassiveTests
{
    private static readonly UnitId Hero = new(1);
    private static readonly UnitId Enemy = new(2);

    private static UnitSetup HeroWith(PassiveKind passive, params SkillDefinition[] skills) =>
        Unit(1, team: 0, 0, 0, gauge: Capacity, initiative: 0, passive: passive, skills: skills.Length > 0 ? skills : null);

    [Fact]
    public void TimeThief_EachHitThatHurts_TakesGaugeFromTheTarget()
    {
        var sim = Simulation(new Grid(4, 1), HeroWith(PassiveKind.TimeThief), Unit(2, team: 1, 1, 0, gauge: 50, initiative: 0));

        sim.Submit(Use(Hero, "strike", Enemy));

        Assert.Contains(new GaugeLost(1, Enemy, 10, 40), sim.Tick());
    }

    [Fact]
    public void Momentum_TakingAnEnemyOut_GivesGaugeBack()
    {
        // Action légère : la jauge repart à 40, puis le gain de 40.
        var sim = Simulation(new Grid(4, 1), HeroWith(PassiveKind.Momentum), Unit(2, team: 1, 1, 0, hp: 5), Unit(3, team: 1, 3, 0));

        sim.Submit(Use(Hero, "strike", Enemy));
        var events = sim.Tick();

        Assert.Contains(events, e => e is UnitRemoved { Unit.Value: 2 });
        Assert.Contains(new GaugeGained(1, Hero, 40, 80), events);
        Assert.Equal(80, sim.Get(1).Gauge);
    }

    [Fact]
    public void Momentum_GivesNothing_WhenTheTargetSurvives()
    {
        var sim = Simulation(new Grid(4, 1), HeroWith(PassiveKind.Momentum), Unit(2, team: 1, 1, 0, hp: 100));

        sim.Submit(Use(Hero, "strike", Enemy));

        Assert.DoesNotContain(sim.Tick(), e => e is GaugeGained);
        Assert.Equal(40, sim.Get(1).Gauge);
    }

    [Fact]
    public void EnergyRecovery_GivesGaugeForEachEnemyStunned()
    {
        // Une lourde de zone (la jauge repart à 0) qui étourdit deux ennemis : 10 + 10.
        var pulse = new SkillDefinition
        {
            Id = "pulse", Weight = SkillWeight.Heavy, Range = 3, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false, Affects = ZoneTargets.Enemies },
            Effects = new[] { new StatusEffect { Status = StatusKind.Stunned, DurationMs = 1000 } },
        };
        var sim = Simulation(new Grid(6, 2), HeroWith(PassiveKind.EnergyRecovery, pulse),
            Unit(2, team: 1, 2, 0), Unit(3, team: 1, 3, 0), Unit(4, team: 1, 5, 1));

        sim.Submit(Use(Hero, "pulse", cell: new Cell(2, 1)));
        var events = sim.Tick();

        Assert.Equal(2, events.OfType<StatusApplied>().Count());
        Assert.Contains(new GaugeGained(1, Hero, 20, 20), events);
    }

    [Fact]
    public void Slipstream_AfterAnAction_TheAlliesInContactGainGauge()
    {
        var sim = Simulation(new Grid(6, 2), HeroWith(PassiveKind.Slipstream),
            Unit(2, team: 1, 1, 0, hp: 100),
            Unit(3, team: 0, 0, 1, initiative: 0),  // au contact
            Unit(4, team: 0, 4, 1, initiative: 0)); // trop loin

        sim.Submit(Use(Hero, "strike", Enemy));
        var events = sim.Tick();

        Assert.Equal(new[] { new GaugeGained(1, new UnitId(3), 5, 5) }, events.OfType<GaugeGained>());
    }

    [Fact]
    public void Slipstream_DoesNothing_AfterAMoveAlone()
    {
        var sim = Simulation(new Grid(6, 2), HeroWith(PassiveKind.Slipstream), Unit(3, team: 0, 2, 1, initiative: 0));

        sim.Submit(new ActivateCommand(Hero, new Cell(1, 0)));

        Assert.DoesNotContain(sim.Tick(), e => e is GaugeGained);
    }

    [Fact]
    public void Slipstream_FollowsAChargedAttack_WhenItGoesOff_NotWhenItStarts()
    {
        var charged = Strike with { Id = "charged", ChargeMs = 100 }; // 2 ticks
        var sim = Simulation(new Grid(6, 2), HeroWith(PassiveKind.Slipstream, charged),
            Unit(2, team: 1, 1, 0, hp: 100), Unit(3, team: 0, 0, 1, initiative: 0));

        sim.Submit(Use(Hero, "charged", Enemy));
        Assert.DoesNotContain(sim.Tick(), e => e is GaugeGained); // la charge commence : rien encore

        Assert.Contains(sim.TickTimes(2), e => e is GaugeGained { Unit.Value: 3 });
    }

    [Fact]
    public void Momentum_CountsAnEnemyFinishedByACollision()
    {
        // Projeté contre le bord de la carte : c'est la collision (10) qui l'achève, pas le coup (sans dégâts).
        var shove = Strike with
        {
            Id = "shove", MinDamage = 0, MaxDamage = 0,
            ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Push, Distance = 3, CollisionMinDamage = 10, CollisionMaxDamage = 10 },
        };
        var sim = Simulation(new Grid(3, 1), HeroWith(PassiveKind.Momentum, shove), Unit(2, team: 1, 1, 0, hp: 5), Unit(3, team: 1, 2, 0));

        sim.Submit(Use(Hero, "shove", Enemy));
        var events = sim.Tick();

        Assert.Contains(events, e => e is UnitRemoved { Unit.Value: 2 });
        Assert.Contains(events, e => e is GaugeGained { Unit.Value: 1, Amount: 40 });
    }

    [Fact]
    public void EnergyRecovery_GivesNothing_ForAnEnemyAlreadyStunned()
    {
        var zap = Strike with { Id = "zap", MinDamage = 0, MaxDamage = 0, Effects = new[] { new StatusEffect { Status = StatusKind.Stunned, DurationMs = 1000 } } };
        var sim = Simulation(new Grid(4, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity, initiative: Capacity, passive: PassiveKind.EnergyRecovery, skills: new[] { zap }),
            Unit(2, team: 1, 1, 0, hp: 100));
        sim.Submit(Use(Hero, "zap", Enemy));
        Assert.Contains(sim.Tick(), e => e is GaugeGained { Unit.Value: 1 });

        sim.Submit(Use(Hero, "zap", Enemy)); // déjà étourdi : l'état est seulement prolongé

        Assert.DoesNotContain(sim.Tick(), e => e is GaugeGained);
    }

    [Fact]
    public void Command_AlliesNearby_FillTheirGaugeFaster()
    {
        var sim = Simulation(new Grid(8, 1),
            Unit(1, team: 0, 0, 0, initiative: 10, passive: PassiveKind.Command),
            Unit(2, team: 0, 2, 0, initiative: 10),  // à 2 cases : dans l'aura
            Unit(3, team: 0, 3, 0, initiative: 10),  // à 3 cases : hors de portée
            Unit(4, team: 1, 1, 0, initiative: 10)); // un ennemi tout près : pas concerné

        sim.Tick();

        Assert.Equal((10, 15, 10, 10), (sim.Get(1).Gauge, sim.Get(2).Gauge, sim.Get(3).Gauge, sim.Get(4).Gauge));
    }

    [Fact]
    public void JammingField_EnemiesNearby_FillTheirGaugeSlower()
    {
        var sim = Simulation(new Grid(8, 1),
            Unit(1, team: 0, 0, 0, initiative: 10, passive: PassiveKind.JammingField),
            Unit(2, team: 1, 2, 0, initiative: 10),  // à 2 cases : ralenti
            Unit(3, team: 1, 3, 0, initiative: 10),  // hors de portée
            Unit(4, team: 0, 1, 0, initiative: 10)); // un allié tout près : pas concerné

        sim.Tick();

        Assert.Equal((10, 5, 10, 10), (sim.Get(1).Gauge, sim.Get(2).Gauge, sim.Get(3).Gauge, sim.Get(4).Gauge));
    }

    [Fact]
    public void AnAura_StopsWhenItsBearerFalls()
    {
        var sim = Simulation(new Grid(8, 1),
            Unit(1, team: 0, 0, 0, initiative: 10, onDefeat: DefeatOutcome.Downed, passive: PassiveKind.JammingField),
            Unit(2, team: 1, 1, 0, initiative: 10));
        sim.Get(1).Status = UnitStatus.Downed;

        sim.Tick();

        Assert.Equal(10, sim.Get(2).Gauge);
    }
}
