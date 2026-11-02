using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class SupportTests
{
    private static readonly UnitId Tech = new(1);
    private static readonly UnitId Ally = new(2);

    /// <summary>Remplit 50 % de la jauge d'un allié, portée 5.</summary>
    private static readonly SkillDefinition Order = new()
    {
        Id = "order", Target = SkillTargeting.Ally, Weight = SkillWeight.Light, Range = 5,
        MinDamage = 0, MaxDamage = 0, CooldownMs = 0, GaugeGainPercent = 50,
    };

    private static UnitSetup TechAt(int x, int y) => Unit(1, team: 0, x, y, gauge: Capacity, skills: new[] { Order });

    private static ActivateCommand Give(UnitId target) => new(Tech, null, new SkillUse("order", target));

    [Fact]
    public void Order_FillsPartOfAnAllysGauge()
    {
        var sim = Simulation(new Grid(6, 1), TechAt(0, 0), Unit(2, team: 0, 3, 0, gauge: 20, initiative: 0));

        sim.Submit(Give(Ally));

        Assert.Equal(
            new CombatEvent[] { new SkillUsed(1, Tech, "order", Ally), new GaugeGained(1, Ally, 50, 70) },
            sim.Tick().Where(e => e is not UnitReady));
    }

    [Fact]
    public void Order_ThatFillsTheGauge_MakesTheAllyReadyAtOnce_AndIsCapped()
    {
        var sim = Simulation(new Grid(6, 1), TechAt(0, 0), Unit(2, team: 0, 3, 0, gauge: 80, initiative: 0));

        sim.Submit(Give(Ally));
        var events = sim.Tick();

        Assert.Contains(new GaugeGained(1, Ally, 20, Capacity), events);
        Assert.Contains(new UnitReady(1, Ally), events);
        Assert.True(sim.Get(2).IsReady);
    }

    [Fact]
    public void Order_IsRefused_OnSelf_OnEnemies_OnReadyOrChargingAllies()
    {
        var charged = Strike with { Id = "charged", ChargeMs = 1000 };
        var sim = Simulation(new Grid(8, 1),
            TechAt(0, 0),
            Unit(2, team: 0, 1, 0, gauge: Capacity),                                // déjà prête
            Unit(3, team: 1, 2, 0),                                                 // ennemi
            Unit(4, team: 0, 3, 0, gauge: Capacity, skills: new[] { charged }),     // va charger
            Unit(5, team: 1, 4, 0));
        sim.Submit(new ActivateCommand(new UnitId(4), null, new SkillUse("charged", new UnitId(5))));
        sim.Tick();

        var self = Give(Tech);
        var ready = Give(Ally);
        var enemy = Give(new UnitId(3));
        var charging = Give(new UnitId(4));
        foreach (var command in new[] { self, ready, enemy, charging })
            sim.Submit(command);

        Assert.Equal(
            new CombatEvent[]
            {
                new CommandRejected(2, self, RejectionReason.InvalidTarget),
                new CommandRejected(2, ready, RejectionReason.NoEffect),
                new CommandRejected(2, enemy, RejectionReason.InvalidTarget),
                new CommandRejected(2, charging, RejectionReason.NoEffect),
            },
            sim.Tick().OfType<CommandRejected>());
    }

    [Fact]
    public void Order_OnAStunnedAlly_RaisesItsFrozenGauge_ItActsWhenTheStunEnds()
    {
        var stunBlow = Strike with { Id = "stun-blow", MinDamage = 0, MaxDamage = 0, Effects = new[] { new StatusEffect { Status = StatusKind.Stunned, DurationMs = 500 } } };
        var sim = Simulation(new Grid(6, 1),
            TechAt(0, 0),
            Unit(2, team: 0, 2, 0, gauge: 30, initiative: 0),
            Unit(3, team: 1, 3, 0, gauge: Capacity, skills: new[] { stunBlow }));
        sim.Submit(new ActivateCommand(new UnitId(3), null, new SkillUse("stun-blow", Ally))); // étourdi jusqu'au tick 11
        sim.Tick();

        sim.Submit(Give(Ally));
        Assert.Contains(new GaugeGained(2, Ally, 50, 80), sim.Tick());
        Assert.False(sim.Get(2).IsReady);
    }

    [Fact]
    public void BasicAi_DoesNotUseSupportSkills()
    {
        var sim = Simulation(new Grid(8, 1),
            Unit(1, team: 1, 0, 0, gauge: Capacity, skills: new[] { Order, Strike }, behavior: Behaviors.Basic),
            Unit(2, team: 1, 1, 0, gauge: 0, initiative: 0),
            Unit(3, team: 0, 7, 0, initiative: 0));

        Assert.DoesNotContain(sim.TickTimes(30), e => e is GaugeGained or SkillUsed { SkillId: "order" });
    }

    [Fact]
    public void Order_NeedsRangeAndLineOfSight()
    {
        var sim = Simulation(Grid.FromRows("..#...."), TechAt(0, 0), Unit(2, team: 0, 4, 0), Unit(3, team: 0, 6, 0));

        var hidden = Give(Ally);
        var far = Give(new UnitId(3));
        sim.Submit(hidden);
        sim.Submit(far);

        Assert.Equal(
            new CombatEvent[]
            {
                new CommandRejected(1, hidden, RejectionReason.NoLineOfSight),
                new CommandRejected(1, far, RejectionReason.OutOfRange),
            },
            sim.Tick());
    }

    [Theory]
    [InlineData("""{ "id": "x", "target": "ally", "weight": "light", "range": 3, "minDamage": 5, "maxDamage": 5, "cooldownMs": 0 }""")]
    [InlineData("""{ "id": "x", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "gaugeGainPercent": 50 }""")]
    [InlineData("""{ "id": "x", "target": "ally", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "gaugeGainPercent": 101 }""")]
    [InlineData("""{ "id": "x", "target": "ally", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "gaugeGainPercent": 50, "zone": { "shape": "square", "size": 1, "throughWalls": false } }""")]
    [InlineData("""{ "id": "x", "target": "ally", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "gaugeGainPercent": 50, "chargeMs": 500 }""")]
    [InlineData("""{ "id": "x", "target": "ally", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "effects": [ { "status": "stunned", "durationMs": 500 } ] }""")]
    public void InvalidSupportSkills_AreRejectedAtLoad(string skill)
    {
        Assert.Throws<ArgumentException>(() => SkillDefinition.ListFromJson($"[{skill}]"));
    }

    [Fact]
    public void BasicAttack_CannotBeASupportSkill()
    {
        Assert.Throws<ArgumentException>(() =>
            Simulation(new Grid(2, 1), Unit(1, team: 0, 0, 0, skills: new[] { Order }, basicAttack: "order")));
    }
}
