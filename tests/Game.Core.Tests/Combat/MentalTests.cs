using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class MentalTests
{
    private static readonly UnitId Telepath = new(1);

    private static SkillDefinition Mental(string id, StatusKind status, int durationMs) => new()
    {
        Id = id, Weight = SkillWeight.Light, Range = 5, MinDamage = 0, MaxDamage = 0, CooldownMs = 0, IsMental = true,
        Effects = new[] { new StatusEffect { Status = status, DurationMs = durationMs } },
    };

    private static readonly SkillDefinition Confusion = Mental("confusion", StatusKind.Confused, 2000);   // 40 ticks
    private static readonly SkillDefinition Domination = Mental("domination", StatusKind.Dominated, 1000); // 20 ticks
    private static readonly SkillDefinition Paralysis = Mental("paralysis", StatusKind.Stunned, 500);     // 10 ticks

    private static UnitSetup TelepathAt(int x, int y, PassiveKind? passive = null) =>
        Unit(1, team: 0, x, y, gauge: Capacity, initiative: Capacity, skills: new[] { Confusion, Domination, Paralysis }, passive: passive);

    private static ActivateCommand Cast(string skill, int target) => new(Telepath, null, new SkillUse(skill, new UnitId(target)));

    [Fact]
    public void ConfusedEnemy_AttacksTheNearestUnit_EvenItsOwnAlly()
    {
        // Le cogneur (5) est collé à son allié (6) ; le héros le plus proche est à 3 cases.
        var sim = Simulation(new Grid(8, 1),
            TelepathAt(0, 0),
            Unit(5, team: 1, 3, 0, initiative: 50, behavior: Behaviors.Basic),
            Unit(6, team: 1, 4, 0));

        sim.Submit(Cast("confusion", 5));
        var events = sim.TickTimes(4);

        Assert.Contains(events, e => e is SkillUsed { Unit.Value: 5, Target.Value: 6 });
        Assert.True(sim.Get(6).Hp < 100);
    }

    [Fact]
    public void ConfusedUnit_CannotBeCommandedByPlayers()
    {
        var sim = Simulation(new Grid(8, 1),
            Unit(9, team: 1, 0, 0, gauge: Capacity, skills: new[] { Confusion }, behavior: Behaviors.Basic),
            Unit(2, team: 0, 3, 0, gauge: Capacity));

        // L'ennemi (IA) rend le héros confus ; le joueur perd la main dessus.
        sim.Tick();
        var order = new ActivateCommand(new UnitId(2), new Cell(4, 0));
        sim.Submit(order);

        Assert.Contains(new CommandRejected(2, order, RejectionReason.NotControllable), sim.Tick());
    }

    [Fact]
    public void Domination_SwitchesSides_LetsPlayersCommand_ThenReverts()
    {
        var sim = Simulation(new Grid(8, 1),
            TelepathAt(0, 0),
            Unit(5, team: 1, 3, 0, initiative: Capacity, gauge: Capacity, behavior: Behaviors.Basic),
            Unit(6, team: 1, 4, 0));

        sim.Submit(Cast("domination", 5));
        var events = sim.Tick();
        Assert.Contains(new TeamChanged(1, new UnitId(5), 0), events);
        Assert.Equal(0, sim.Get(5).Team);

        // Commandé par un joueur, il frappe son ancien allié.
        sim.Submit(new ActivateCommand(new UnitId(5), null, new SkillUse("strike", new UnitId(6))));
        Assert.Contains(sim.Tick(), e => e is UnitDamaged { Unit.Value: 6 });

        events = sim.TickTimes(20); // fin au tick 21
        Assert.Contains(new TeamChanged(21, new UnitId(5), 1), events);
        Assert.Equal(1, sim.Get(5).Team);
        Assert.Null(sim.Get(5).DominatedBy);
    }

    [Fact]
    public void DominatedUnitDefeated_ReturnsToItsTeam()
    {
        var sim = Simulation(new Grid(8, 1),
            TelepathAt(0, 0),
            Unit(5, team: 1, 3, 0, hp: 10, behavior: Behaviors.Basic),
            Unit(6, team: 1, 4, 0, gauge: Capacity, initiative: Capacity, behavior: Behaviors.Basic));

        // Dominé au tick 1 (commandes des joueurs d'abord), son ancien allié (IA) le prend aussitôt pour un ennemi.
        sim.Submit(Cast("domination", 5));
        var events = sim.TickTimes(3);

        Assert.Contains(events, e => e is UnitRemoved { Unit.Value: 5 });
        Assert.Contains(events, e => e is StatusEnded { Unit.Value: 5, Status: StatusKind.Dominated });
        Assert.Equal(1, sim.Get(5).Team);
    }

    [Fact]
    public void MentalPowers_AreRefusedOnRobots_DominationOnBosses()
    {
        var sim = Simulation(new Grid(8, 2),
            TelepathAt(0, 0),
            Unit(5, team: 1, 2, 1, isRobot: true),
            Unit(6, team: 1, 4, 0, isBoss: true));

        var stunRobot = Cast("paralysis", 5);
        var dominateBoss = Cast("domination", 6);
        sim.Submit(stunRobot);
        sim.Submit(dominateBoss);
        var events = sim.Tick();

        Assert.Contains(new CommandRejected(1, stunRobot, RejectionReason.NoEffect), events);
        Assert.Contains(new CommandRejected(1, dominateBoss, RejectionReason.NoEffect), events);

        sim.Submit(Cast("paralysis", 6)); // un boss peut être paralysé
        Assert.Contains(sim.Tick(), e => e is StatusApplied { Unit.Value: 6, Status: StatusKind.Stunned });
    }

    [Fact]
    public void MentalZone_SparesRobots()
    {
        var wave = new SkillDefinition
        {
            Id = "psi-wave", Weight = SkillWeight.Light, Range = 3, MinDamage = 5, MaxDamage = 5, CooldownMs = 0, IsMental = true,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false, Affects = ZoneTargets.Enemies },
        };
        var sim = Simulation(new Grid(8, 3),
            Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { wave }),
            Unit(5, team: 1, 3, 1, isRobot: true),
            Unit(6, team: 1, 3, 0));

        sim.Submit(new ActivateCommand(Telepath, null, new SkillUse("psi-wave", null, new Cell(3, 1))));
        var events = sim.Tick();

        Assert.Contains(new UnitResisted(1, new UnitId(5)), events);
        Assert.Equal(100, sim.Get(5).Hp);
        Assert.Equal(95, sim.Get(6).Hp);
    }

    [Fact]
    public void Grip_MakesHarmfulStatusesLast150Percent()
    {
        var sim = Simulation(new Grid(8, 1), TelepathAt(0, 0, PassiveKind.Grip), Unit(5, team: 1, 3, 0));

        sim.Submit(Cast("paralysis", 5));

        Assert.Contains(new StatusApplied(1, new UnitId(5), StatusKind.Stunned, 16), sim.Tick()); // 10 ticks × 150 %
    }

    [Fact]
    public void EndOfDomination_CancelsTheChargeStartedUnderControl()
    {
        var quake = new SkillDefinition
        {
            Id = "quake", Weight = SkillWeight.Heavy, Range = 0, MinDamage = 30, MaxDamage = 30, CooldownMs = 0, ChargeMs = 1000,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        };
        var sim = Simulation(new Grid(8, 3),
            TelepathAt(0, 1),
            Unit(5, team: 1, 3, 1, gauge: Capacity, initiative: Capacity, skills: new[] { quake }, behavior: Behaviors.Basic),
            Unit(2, team: 0, 4, 1));

        sim.Submit(Cast("domination", 5));
        sim.Tick();
        // Les joueurs lui font charger une zone qui se déclencherait après la fin de la domination (tick 21).
        sim.Submit(new ActivateCommand(new UnitId(5), null, new SkillUse("quake", null, new Cell(3, 1))));
        var events = sim.TickTimes(25);

        Assert.Contains(new ChargeInterrupted(21, new UnitId(5), "quake"), events);
        Assert.Equal(100, sim.Get(2).Hp);
    }

    [Fact]
    public void Confusion_ClearsTheIntention_WithAnEvent()
    {
        var sim = Simulation(new Grid(8, 1),
            TelepathAt(0, 0),
            Unit(5, team: 1, 3, 0, gauge: 60, behavior: Behaviors.Basic));
        sim.Tick();
        Assert.NotNull(sim.Get(5).Intention);

        sim.Submit(Cast("confusion", 5));

        Assert.Contains(new IntentionChosen(2, new UnitId(5), null), sim.Tick());
    }

    [Fact]
    public void ConfusedTelepath_CannotDominateItsOwnAlly()
    {
        var sim = Simulation(new Grid(8, 1), TelepathAt(0, 0), Unit(2, team: 0, 2, 0));
        sim.Get(1).SetStatus(StatusKind.Confused, endTick: 100);

        var (plan, reason) = sim.ValidateActivation(Cast("domination", 2));

        Assert.Null(plan);
        Assert.Equal(RejectionReason.InvalidTarget, reason);
    }

    [Theory]
    [InlineData("""{ "id": "x", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "effects": [ { "status": "confused", "durationMs": 100 } ] }""")]
    [InlineData("""{ "id": "x", "target": "ally", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "isMental": true, "gaugeGainPercent": 20 }""")]
    [InlineData("""{ "id": "x", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "isMental": true, "zone": { "shape": "square", "size": 1, "throughWalls": false }, "effects": [ { "status": "dominated", "durationMs": 100 } ] }""")]
    public void InvalidMentalSkills_AreRejectedAtLoad(string skill)
    {
        Assert.Throws<ArgumentException>(() => SkillDefinition.ListFromJson($"[{skill}]"));
    }
}
