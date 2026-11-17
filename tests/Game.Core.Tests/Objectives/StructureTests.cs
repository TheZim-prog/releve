using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Objectives;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Objectives;

public class StructureTests
{
    private static readonly SkillDefinition Heal =
        Strike with { Id = "heal", Target = SkillTargeting.Ally, MinDamage = 0, MaxDamage = 0, MinHeal = 5, MaxHeal = 5 };

    private static readonly SkillDefinition Projection =
        Strike with { Id = "projection", ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Push, Distance = 2 } };

    private static readonly SkillDefinition StunBlow =
        Strike with { Id = "stun-blow", Effects = new[] { new StatusEffect { Status = StatusKind.Stunned, DurationMs = 2000 } } };

    /// <summary>Le relais à protéger : une structure de 50 PV, du camp des joueurs.</summary>
    private static UnitSetup Relay(int x, int y) =>
        new UnitDefinition
        {
            Id = "relay", Name = "Relais", MaxHp = 50, Initiative = 0, Mobility = 0, OnDefeat = DefeatOutcome.Removed,
            OpportunityAttacksPerCycle = 0, Behavior = Behaviors.Inert, Skills = Array.Empty<SkillDefinition>(), BasicAttack = null,
            IsStructure = true,
        }.ToSetup(new UnitId(5), team: 0, new Cell(x, y)) with { IsProtected = true };

    private static readonly ObjectiveDefinition Protect =
        new() { Kind = ObjectiveKind.Protect, Until = ProtectUntil.Time, DurationMs = 60000 };

    private static CombatSimulation Defend(Grid grid, params UnitSetup[] units) =>
        new(Setup(grid, units) with { Objectives = new[] { Protect } });

    [Fact]
    public void AStructure_BlocksEveryone_ButNotTheSight()
    {
        var sim = Defend(new Grid(5, 1), Unit(1, team: 0, 0, 0, mobility: 4), Relay(2, 0), Unit(2, team: 1, 4, 0, skills: new[] { Strike with { Range = 4 } }));

        Assert.Null(sim.PathFor(new UnitId(1), new Cell(3, 0))); // même un allié ne passe pas
        Assert.Null(sim.Check(Use(new UnitId(2), "strike", new UnitId(1)), ignoreTiming: true)); // on tire par-dessus
    }

    [Fact]
    public void AStructure_TakesHits_ButNoStatusNoMoveNoHeal()
    {
        var sim = Defend(new Grid(6, 1), Relay(2, 0),
            Unit(2, team: 1, 3, 0, gauge: Capacity, skills: new[] { Projection }),
            Unit(3, team: 1, 1, 0, gauge: Capacity, skills: new[] { StunBlow }),
            Unit(1, team: 0, 5, 0));
        sim.Submit(Use(new UnitId(2), "projection", new UnitId(5)));
        sim.Submit(Use(new UnitId(3), "stun-blow", new UnitId(5)));

        sim.Tick();

        var relay = sim.Get(5);
        Assert.Equal(30, relay.Hp);
        Assert.Equal(new Cell(2, 0), relay.Position);
        Assert.Empty(relay.StatusEndTicks);
    }

    [Fact]
    public void AStructure_Resists_StatusesAndForcedMoves()
    {
        var sim = Defend(new Grid(6, 1), Relay(2, 0), Unit(2, team: 1, 3, 0, gauge: Capacity, skills: new[] { Projection }), Unit(1, team: 0, 5, 0));
        sim.Submit(Use(new UnitId(2), "projection", new UnitId(5)));

        var events = sim.Tick();

        Assert.Contains(new UnitResisted(1, new UnitId(5)), events);
    }

    [Fact]
    public void APierce_StopsBeforeAStructure()
    {
        var pierce = Strike with { Id = "pierce", Range = 5, SelfMove = SelfMoveType.Pierce };
        var sim = Defend(new Grid(7, 1), Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { pierce }), Relay(2, 0), Unit(2, team: 1, 6, 0));

        sim.Submit(Use(new UnitId(1), "pierce", cell: new Cell(1, 0)));
        sim.Tick();

        Assert.Equal(new Cell(1, 0), sim.Get(1).Position);
    }

    [Fact]
    public void NoSkill_CanRepairOrSupportAStructure()
    {
        var sim = Defend(new Grid(4, 1), Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { Heal }), Relay(1, 0), Unit(2, team: 1, 3, 0));
        sim.Get(5).Hp = 20;

        Assert.Equal(RejectionReason.InvalidTarget, sim.Check(Use(new UnitId(1), "heal", new UnitId(5))));
    }

    [Fact]
    public void Enemies_GoForTheStructure_AndDestroyingItFailsTheProtection()
    {
        var sim = Defend(new Grid(6, 3), Unit(1, team: 0, 2, 0, hp: 20), Relay(2, 2) with { Definition = Relay(2, 2).Definition with { MaxHp = 10 } },
            Unit(2, team: 1, 3, 1, gauge: Capacity, behavior: Behaviors.Basic));

        var events = sim.Tick();

        Assert.Contains(events, e => e is SkillUsed { Unit.Value: 2, Target.Value: 5 });
        Assert.Equal(UnitStatus.Removed, sim.Get(5).Status);
        Assert.Equal(ObjectiveState.Failed, sim.Objectives[0].State);
        Assert.False(sim.Outcome);
    }

    [Fact]
    public void AStructure_IsNeitherAHeroNorACombatant()
    {
        var sim = Defend(new Grid(4, 1), Unit(1, team: 0, 0, 0), Relay(1, 0), Unit(2, team: 1, 3, 0));

        Assert.False(sim.Get(5).IsHero);
        Assert.False(sim.Get(5).IsStandingCombatant);
        sim.Get(1).Status = UnitStatus.Downed;
        sim.Tick();
        Assert.False(sim.Outcome); // le relais debout ne sauve pas le combat
    }

    [Theory]
    [InlineData("behavior")]
    [InlineData("initiative")]
    [InlineData("skills")]
    public void AStructure_NeverActs(string broken)
    {
        var relay = Relay(0, 0).Definition;
        var invalid = broken switch
        {
            "behavior" => relay with { Behavior = Behaviors.Basic },
            "initiative" => relay with { Initiative = 10 },
            _ => relay with { Skills = new[] { Strike } },
        };

        Assert.Throws<ArgumentException>(invalid.Validate);
    }
}
