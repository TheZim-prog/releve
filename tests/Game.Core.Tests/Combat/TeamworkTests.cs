using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>
/// Ce qu'une unité fait pour son camp : Réanimation express, Rechargement, Frappe coordonnée, et les passifs Diversion
/// et Analyse tactique. (Départ canon est posé par le scénario : TechSpeedsterContentTests.)
/// </summary>
public class TeamworkTests
{
    private static readonly UnitId Hero = new(1);
    private static readonly UnitId Ally = new(2);
    private static readonly UnitId Foe = new(9);

    private static readonly SkillDefinition ExpressRevive = new()
    {
        Id = "revive", Target = SkillTargeting.Ally, Weight = SkillWeight.Light, Range = 4, MinDamage = 0, MaxDamage = 0,
        CooldownMs = 0, RevivesAlly = true,
    };

    private static readonly SkillDefinition Reload = new()
    {
        Id = "reload", Target = SkillTargeting.Ally, Weight = SkillWeight.Light, Range = 4, MinDamage = 0, MaxDamage = 0,
        CooldownMs = 0, ResetsCooldowns = true,
    };

    private static readonly SkillDefinition Coordinated = new()
    {
        Id = "coordinated", Weight = SkillWeight.Heavy, Range = 6, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
        CallsAllyStrikes = true,
    };

    private static UnitSetup HeroWith(SkillDefinition skill, int x = 0, int y = 0) =>
        Unit(1, team: 0, x, y, gauge: Capacity, skills: new[] { skill });

    [Fact]
    public void ExpressRevive_RunsNextToTheDownedAlly_AndRaisesItWhereItLies()
    {
        var sim = Simulation(new Grid(8, 1), HeroWith(ExpressRevive), Unit(2, team: 0, 5, 0, hp: 40, onDefeat: DefeatOutcome.Downed),
            Unit(9, team: 1, 7, 0, gauge: Capacity, skills: new[] { Smash with { MinDamage = 50, MaxDamage = 50, CooldownMs = 0 } }));
        sim.Submit(Use(Foe, "smash", Ally) with { MoveTo = new Cell(6, 0) });
        sim.Tick();
        Assert.Equal(UnitStatus.Downed, sim.Get(2).Status);

        sim.Submit(Use(Hero, "revive", Ally));
        var events = sim.Tick();

        Assert.Contains(new UnitDisplaced(2, Hero, new Cell(0, 0), new Cell(4, 0)), events);
        Assert.Contains(events, e => e is UnitRevived { Unit.Value: 2, By.Value: 1 });
        Assert.Equal((UnitStatus.Active, new Cell(5, 0)), (sim.Get(2).Status, sim.Get(2).Position));
    }

    [Fact]
    public void ExpressRevive_TargetsADownedAllyWithinItsSteps()
    {
        var sim = Simulation(new Grid(8, 1), HeroWith(ExpressRevive), Unit(2, team: 0, 3, 0), Unit(9, team: 1, 7, 0));

        Assert.Equal(RejectionReason.InvalidTarget, sim.Check(Use(Hero, "revive", Ally))); // debout
        Assert.Equal(RejectionReason.InvalidTarget, sim.Check(Use(Hero, "revive", Foe)));
    }

    [Fact]
    public void Reload_EndsTheAllysCooldowns_AndIsUselessWithoutAny()
    {
        var sim = Simulation(new Grid(6, 1), HeroWith(Reload), Unit(2, team: 0, 1, 0, gauge: Capacity), Unit(9, team: 1, 2, 0));
        Assert.Equal(RejectionReason.NoEffect, sim.Check(Use(Hero, "reload", Ally)));
        sim.Submit(Use(Ally, "smash", Foe));
        sim.Tick();
        Assert.True(sim.Get(2).HasCooldownRunning(sim.CurrentTick));

        sim.Submit(Use(Hero, "reload", Ally));

        Assert.Contains(new CooldownsReset(2, Ally), sim.Tick());
        Assert.Null(sim.Get(2).UnavailableAt(Smash, sim.CurrentTick));
    }

    [Fact]
    public void CoordinatedStrike_EachAllyInRangeHitsTheTargetWithItsBasicAttack_ForFree()
    {
        var sim = Simulation(new Grid(8, 3), HeroWith(Coordinated),
            Unit(2, team: 0, 4, 1, basicAttack: "strike"),             // au contact : frappe
            Unit(3, team: 0, 7, 2, basicAttack: "strike"),             // trop loin pour son attaque de base
            Unit(4, team: 0, 6, 0, basicAttack: "strike", gauge: 40),  // au contact : frappe, jauge intacte
            Unit(9, team: 1, 5, 1));

        sim.Submit(Use(Hero, "coordinated", Foe));
        var events = sim.Tick();

        Assert.Equal(80, sim.Get(9).Hp);
        Assert.True(sim.Get(4).Gauge >= 40);
        // Les coups gratuits ne sont pas des actions des alliés.
        Assert.Equal(new[] { 2, 4 }, events.OfType<StrikeJoined>().Select(j => j.Unit.Value));
        Assert.Equal(new[] { 1 }, events.OfType<SkillUsed>().Select(u => u.Unit.Value));
    }

    [Fact]
    public void CoordinatedStrike_WithoutAnAllyToStrike_IsUseless()
    {
        var sim = Simulation(new Grid(8, 1), HeroWith(Coordinated), Unit(2, team: 0, 1, 0, basicAttack: "strike"), Unit(9, team: 1, 6, 0));

        Assert.Equal(RejectionReason.NoEffect, sim.Check(Use(Hero, "coordinated", Foe)));
    }

    [Fact]
    public void Diversion_EnemiesNextToTheSpeedster_DoNotPunishItsAllies_ButStillPunishIt()
    {
        CombatSimulation Escape(int mover, Cell to)
        {
            var sim = Simulation(new Grid(6, 3),
                Unit(1, team: 0, 2, 0, gauge: Capacity, passive: PassiveKind.Diversion),
                Unit(2, team: 0, 2, 2, gauge: Capacity),
                Unit(9, team: 1, 2, 1, basicAttack: "strike"));
            sim.Submit(new ActivateCommand(new UnitId(mover), to, null));
            sim.Tick();
            return sim;
        }

        Assert.Equal(100, Escape(2, new Cell(5, 2)).Get(2).Hp);
        Assert.Equal(90, Escape(1, new Cell(5, 0)).Get(1).Hp);
    }

    [Fact]
    public void TacticalAnalysis_RevealsTheIntentionsOfNearbyEnemies()
    {
        var sim = Simulation(new Grid(10, 1), Unit(1, team: 0, 0, 0, passive: PassiveKind.TacticalAnalysis),
            Unit(8, team: 1, 3, 0), Unit(9, team: 1, 4, 0), Unit(2, team: 0, 1, 0));

        Assert.Equal(new[] { new UnitId(8) }, sim.RevealedIntentions());
    }
}
