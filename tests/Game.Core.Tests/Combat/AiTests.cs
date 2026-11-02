using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class AiTests
{
    private static readonly UnitId Ai = new(9);
    private static readonly UnitId HeroA = new(1);
    private static readonly UnitId HeroB = new(2);

    /// <summary>Ennemi IA (équipe 1), comportement de base, initiative 10 : intention au tick 5, prêt au tick 10.</summary>
    private static UnitSetup AiAt(int x, int y, int mobility = 3, int hp = 100, IReadOnlyList<SkillDefinition>? skills = null) =>
        Unit(9, team: 1, x, y, mobility: mobility, hp: hp, skills: skills, behavior: Behaviors.Basic);

    private static UnitSetup HeroAt(int id, int x, int y, int hp = 100, bool ready = false) =>
        Unit(id, team: 0, x, y, hp: hp, gauge: ready ? Capacity : 0, initiative: ready ? Capacity : 0);

    [Fact]
    public void Intention_IsFixedAtTheThreshold_ThenExecutedWhenTheGaugeIsFull()
    {
        var sim = Simulation(new Grid(7, 3), HeroAt(1, 3, 1), AiAt(6, 1));

        Assert.Empty(sim.TickTimes(4).OfType<IntentionChosen>());
        Assert.Equal(new CombatEvent[] { new IntentionChosen(5, Ai, new Intention("smash", HeroA)) }, sim.Tick());

        sim.TickTimes(5); // prêt au tick 10
        var events = sim.Tick();

        Assert.IsType<UnitMoved>(events[0]);
        Assert.Equal(new SkillUsed(11, Ai, "smash", HeroA), events[1]);
        Assert.Equal(new UnitDamaged(11, HeroA, 30, 70), events[2]);
        Assert.Equal(new Cell(4, 1), sim.Get(9).Position); // le moins de pas possible
        Assert.Null(sim.Get(9).Intention);
        Assert.Empty(sim.CommandLog); // l'IA ne passe pas par le journal : elle se rejoue d'elle-même
    }

    [Fact]
    public void Target_IsTheMostFragileReachableEnemy()
    {
        var sim = Simulation(new Grid(9, 3),
            HeroAt(1, 4, 1, hp: 50),
            HeroAt(2, 4, 0, hp: 30),
            HeroAt(3, 0, 1, hp: 10), // plus fragile encore, mais hors d'atteinte
            AiAt(7, 1));

        Assert.Contains(new IntentionChosen(5, Ai, new Intention("smash", HeroB)), sim.TickTimes(5));
    }

    [Fact]
    public void WithNoReachableEnemy_ItTargetsTheNearestAndApproaches()
    {
        var sim = Simulation(new Grid(12, 1), HeroAt(1, 0, 0), HeroAt(2, 3, 0), AiAt(11, 0, mobility: 2));

        Assert.Contains(new IntentionChosen(5, Ai, new Intention("smash", HeroB)), sim.TickTimes(5));

        var events = sim.TickTimes(6);
        Assert.Equal(new Cell(9, 0), Assert.Single(events.OfType<UnitMoved>()).To);
        Assert.DoesNotContain(events, e => e is SkillUsed);
    }

    [Fact]
    public void Intention_ChangesWhenTheTargetGetsOutOfReach()
    {
        var sim = Simulation(new Grid(12, 3),
            HeroAt(1, 6, 1, hp: 30, ready: true),
            HeroAt(2, 6, 2, hp: 80),
            AiAt(9, 1));
        sim.TickTimes(5);
        Assert.Equal(new Intention("smash", HeroA), sim.Get(9).Intention);

        sim.Submit(new ActivateCommand(HeroA, new Cell(3, 0))); // A s'enfuit hors d'atteinte
        sim.TickTimes(5);
        var events = sim.Tick();

        Assert.Equal(new IntentionChosen(11, Ai, new Intention("smash", HeroB)), events[0]);
        Assert.Contains(new SkillUsed(11, Ai, "smash", HeroB), events);
    }

    [Fact]
    public void Intention_IsKeptWhileStillPossible_EvenIfABetterTargetAppears()
    {
        var sim = Simulation(new Grid(9, 3), HeroAt(1, 4, 1, hp: 50), HeroAt(2, 4, 0, hp: 60), AiAt(7, 1));
        sim.TickTimes(5);
        Assert.Equal(new Intention("smash", HeroA), sim.Get(9).Intention);

        sim.Get(2).Hp = 10; // B devient bien plus fragile après le choix
        var events = sim.TickTimes(6);

        Assert.Contains(new SkillUsed(11, Ai, "smash", HeroA), events);
        Assert.DoesNotContain(events, e => e is IntentionChosen); // ticks 6 à 11 : aucun changement d'intention
    }

    [Fact]
    public void SkillOnCooldown_IsNotChosen_TheNextStrongestIs()
    {
        var sim = Simulation(new Grid(7, 3), HeroAt(1, 3, 1, hp: 1000), AiAt(6, 1));

        sim.TickTimes(11); // smash au tick 11, recharge jusqu'au tick 111
        // Jauge : 0 + 10 au tick 11, seuil de 50 atteint au tick 15.
        Assert.Contains(new IntentionChosen(15, Ai, new Intention("strike", HeroA)), sim.TickTimes(4));
    }

    [Fact]
    public void PlayerCommands_ResolveBeforeTheAi()
    {
        // Les deux sont prêts au même tick ; le héros achève l'IA avant qu'elle n'agisse.
        var sim = Simulation(new Grid(3, 1), HeroAt(1, 0, 0, ready: true), AiAt(1, 0, hp: 10) with { InitialGauge = Capacity });

        sim.Submit(new ActivateCommand(HeroA, null, new SkillUse("strike", Ai)));
        var events = sim.Tick();

        Assert.Contains(new UnitRemoved(1, Ai), events);
        Assert.DoesNotContain(events, e => e is SkillUsed { Unit.Value: 9 });
    }

    [Fact]
    public void StunnedAi_WaitsAndKeepsItsIntention()
    {
        var stunBlow = Strike with { Id = "stun-blow", MinDamage = 0, MaxDamage = 0, Effects = new[] { new StatusEffect { Status = StatusKind.Stunned, DurationMs = 2000 } } };
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { stunBlow }),
            AiAt(1, 0) with { InitialGauge = 60 });
        sim.Tick();
        var intention = sim.Get(9).Intention;
        Assert.NotNull(intention);

        sim.Submit(new ActivateCommand(HeroA, null, new SkillUse("stun-blow", Ai)));
        var events = sim.TickTimes(20);

        Assert.DoesNotContain(events, e => e is SkillUsed { Unit.Value: 9 });
        Assert.Equal(intention, sim.Get(9).Intention);
    }

    [Fact]
    public void AiVersusAi_IsDeterministic_AndReplaysWithoutAnyCommand()
    {
        var grid = Grid.FromRows(
            "..........",
            "...##.....",
            "..........",
            ".....#....");
        var ranged = Strike with { Id = "bolt", Range = 4, MinDamage = 5, MaxDamage = 15 };
        var setup = Setup(grid,
            Unit(1, team: 0, 0, 0, initiative: 14, hp: 80, skills: new[] { Strike, Smash }, basicAttack: "strike", behavior: Behaviors.Basic),
            Unit(2, team: 0, 0, 3, initiative: 11, hp: 60, skills: new[] { ranged }, basicAttack: "bolt", behavior: Behaviors.Basic),
            Unit(3, team: 1, 9, 0, initiative: 12, hp: 80, skills: new[] { Strike, Smash }, basicAttack: "strike", behavior: Behaviors.Basic),
            Unit(4, team: 1, 9, 3, initiative: 16, hp: 60, skills: new[] { ranged }, basicAttack: "bolt", behavior: Behaviors.Basic));
        var original = new CombatSimulation(setup);
        original.TickTimes(1500);

        Assert.Contains(original.EventLog, e => e is SkillUsed);
        Assert.Contains(original.EventLog, e => e is UnitRemoved);
        var replayed = CombatSimulation.Replay(setup, Array.Empty<LoggedCommand>(), original.CurrentTick);
        Assert.Equal(original.EventLog, replayed.EventLog);
    }

    [Fact]
    public void Players_CannotCommandAnAiUnit()
    {
        var sim = Simulation(new Grid(5, 1), HeroAt(1, 0, 0), AiAt(4, 0) with { InitialGauge = Capacity });

        var hijack = new ActivateCommand(Ai, new Cell(3, 0));
        sim.Submit(hijack);

        Assert.Contains(new CommandRejected(1, hijack, RejectionReason.NotControllable), sim.Tick());
    }

    [Fact]
    public void Target_IsTheMostFragileReachableWithAnySkill_ThenTheStrongestSkillThatReachesIt()
    {
        // L'IA en (6,0), mobilité 1 : le smash (corps à corps) n'atteint que A en (4,0) ; le bolt (portée 4)
        // atteint B en (2,4) depuis (5,1), et B est bien plus fragile.
        var bolt = Strike with { Id = "bolt", Range = 4 };
        var sim = Simulation(new Grid(7, 5),
            HeroAt(1, 4, 0, hp: 90),
            HeroAt(2, 2, 4, hp: 10),
            AiAt(6, 0, mobility: 1, skills: new[] { Smash, bolt }));

        Assert.Contains(new IntentionChosen(5, Ai, new Intention("bolt", HeroB)), sim.TickTimes(5));
    }

    [Fact]
    public void Approach_GoesAroundWalls_EvenIfItFirstMovesAway()
    {
        // Le héros est derrière un long mur : il faut d'abord s'éloigner pour le contourner par le bas.
        var grid = Grid.FromRows(
            "....#.",
            "....#.",
            "....#.",
            "......");
        var sim = Simulation(grid, HeroAt(1, 5, 0), AiAt(3, 0, mobility: 2));

        var moved = sim.TickTimes(11).OfType<UnitMoved>().Single();

        Assert.Equal(new Cell(3, 2), moved.To);
    }

    [Fact]
    public void SelfCenteredZone_IsUsedWhenTheTargetIsAround()
    {
        var slam = new SkillDefinition
        {
            Id = "slam", Weight = SkillWeight.Heavy, Range = 0, MinDamage = 40, MaxDamage = 40, CooldownMs = 0,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        };
        var sim = Simulation(new Grid(5, 3), HeroAt(1, 2, 1), AiAt(4, 1, skills: new[] { Strike, slam }));

        var events = sim.TickTimes(11);

        Assert.Contains(new IntentionChosen(5, Ai, new Intention("slam", HeroA)), events);
        Assert.Contains(events, e => e is SkillUsed { SkillId: "slam" });
        Assert.Equal(60, sim.Get(1).Hp);
    }

    [Fact]
    public void Zone_ThatWouldHitAnAlly_IsAvoided()
    {
        var blast = new SkillDefinition
        {
            Id = "blast", Weight = SkillWeight.Heavy, Range = 3, MinDamage = 40, MaxDamage = 40, CooldownMs = 0,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        };
        var sim = Simulation(new Grid(7, 3),
            HeroAt(1, 1, 1),
            Unit(5, team: 1, 2, 1), // allié de l'IA, collé au héros
            AiAt(4, 1, skills: new[] { blast, Strike }));

        Assert.Contains(new IntentionChosen(5, Ai, new Intention("strike", HeroA)), sim.TickTimes(5));
    }

    [Fact]
    public void Intention_IsReplacedAsSoonAsItsTargetFalls()
    {
        var sim = Simulation(new Grid(9, 3),
            HeroAt(1, 4, 1, hp: 10),
            HeroAt(2, 4, 0, hp: 80),
            Unit(3, team: 1, 3, 1, gauge: Capacity), // allié de l'IA, commandé par le test
            AiAt(7, 1));
        sim.TickTimes(5);
        Assert.Equal(new Intention("smash", HeroA), sim.Get(9).Intention);

        // Tick 6 : A tombe, l'IA change d'intention dans le même tick, bien avant d'être prête.
        sim.Submit(new ActivateCommand(new UnitId(3), null, new SkillUse("strike", HeroA)));
        Assert.Contains(new IntentionChosen(6, Ai, new Intention("smash", HeroB)), sim.Tick());
    }

    [Fact]
    public void Setup_RejectsAnUnknownBehavior()
    {
        Assert.Throws<ArgumentException>(() => Simulation(new Grid(2, 1), Unit(1, team: 1, 0, 0, behavior: "berserk")));
    }
}
