using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>
/// États qu'une compétence ne pose qu'aux robots (ex. Démontage : un simple coup, sauf contre un robot, qu'il étourdit).
/// Les dégâts en plus et les états rallongés contre les robots sont testés avec l'IEM (EmpTests).
/// </summary>
public class RobotBonusTests
{
    private static readonly UnitId Hero = new(1);
    private static readonly UnitId Target = new(2);

    private static readonly StatusEffect Stun = new() { Status = StatusKind.Stunned, DurationMs = 1000 };

    private static SkillDefinition WithRobotEffects(params StatusEffect[] effects) => Strike with
    {
        Id = "dismantle",
        VsRobots = new RobotBonusDefinition { StatusDurationPercent = 100, Effects = effects },
    };

    [Fact]
    public void ARobotEffect_IsAppliedToRobotsOnly()
    {
        CombatSimulation Against(bool robot) => Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { WithRobotEffects(Stun) }), Unit(2, team: 1, 1, 0, isRobot: robot));

        var onARobot = Against(robot: true);
        onARobot.Submit(Use(Hero, "dismantle", Target));
        Assert.Contains(new StatusApplied(1, Target, StatusKind.Stunned, 21), onARobot.Tick());

        var onAThug = Against(robot: false);
        onAThug.Submit(Use(Hero, "dismantle", Target));
        var events = onAThug.Tick();
        Assert.Contains(events, e => e is UnitDamaged { Amount: 10 });
        Assert.DoesNotContain(events, e => e is StatusApplied);
    }

    [Fact]
    public void ARobotEffect_IsAHarmfulStatus_GivenOnce()
    {
        WithRobotEffects(new StatusEffect { Status = StatusKind.Immobilized, DurationMs = 1000 }).Validate();
        // Un robot piraté : confus (Virus), ou dominé (Piratage), sur une seule cible.
        WithRobotEffects(new StatusEffect { Status = StatusKind.Confused, DurationMs = 1000 }).Validate();
        var dominated = WithRobotEffects(new StatusEffect { Status = StatusKind.Dominated, DurationMs = 1000 });
        dominated.Validate();
        Assert.Throws<ArgumentException>(() => (dominated with
        {
            Range = 0, Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        }).Validate());
        Assert.Throws<ArgumentException>(() => WithRobotEffects(new StatusEffect { Status = StatusKind.Hasted, DurationMs = 1000 }).Validate());   // bénéfique
        Assert.Throws<ArgumentException>(() => WithRobotEffects(Stun, Stun).Validate());
        // Un état que la compétence pose déjà à tout le monde se rallonge contre les robots, il ne se répète pas.
        Assert.Throws<ArgumentException>(() => (WithRobotEffects(Stun) with { Effects = new[] { Stun } }).Validate());
    }

    [Fact]
    public void ARobotEffect_CountsAsAnEffectOfTheSkill()
    {
        // Sans dégâts ni autre effet : la compétence ne sert que contre les robots, et c'est permis.
        (WithRobotEffects(Stun) with { MinDamage = 0, MaxDamage = 0 }).Validate();
        // Un bonus qui ne change rien reste une erreur de données.
        Assert.Throws<ArgumentException>(() => WithRobotEffects().Validate());
    }
}
