using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class ReplayTests
{
    // Dégâts aléatoires, et un état en alternance pour couvrir aussi leur application et leur fin.
    private static readonly SkillDefinition RandomStrike = Strike with { Id = "random-strike", MinDamage = 5, MaxDamage = 25 };

    private static readonly SkillDefinition DazingStrike = RandomStrike with
    {
        Id = "dazing-strike",
        CooldownMs = 1000,
        Effects = new[]
        {
            new StatusEffect { Status = StatusKind.OffBalance, DurationMs = 1500 },
            new StatusEffect { Status = StatusKind.Stunned, DurationMs = 300 },
        },
    };

    [Fact]
    public void Replay_FromInitialStateAndCommandLog_ReproducesTheCombat()
    {
        var grid = Grid.FromRows(
            "........",
            "..##....",
            "........",
            "....#...");
        var skills = new[] { RandomStrike, DazingStrike };
        var setup = Setup(grid,
            Unit(1, team: 0, 0, 0, initiative: 20, mobility: 3, hp: 60, onDefeat: DefeatOutcome.Downed, skills: skills),
            Unit(2, team: 0, 0, 2, initiative: 15, mobility: 4, hp: 60, onDefeat: DefeatOutcome.Downed, skills: skills),
            Unit(3, team: 1, 7, 0, initiative: 12, mobility: 2, hp: 40, skills: skills),
            Unit(4, team: 1, 7, 3, initiative: 25, mobility: 3, hp: 40, skills: skills));
        var original = new CombatSimulation(setup);

        // Chaque unité prête frappe un ennemi adjacent s'il y en a un, sinon avance vers l'autre camp.
        for (var tick = 0; tick < 200; tick++)
        {
            foreach (var unit in original.Units.Where(u => u.IsReady))
            {
                var enemy = original.Units.FirstOrDefault(e =>
                    e.Team != unit.Team && e.Status == UnitStatus.Active && e.Position.IsAdjacentTo(unit.Position));
                var skill = original.CurrentTick >= unit.CooldownEndTick(DazingStrike.Id) ? DazingStrike : RandomStrike;
                var command = enemy is not null
                    ? new ActivateCommand(unit.Id, MoveTo: null, new SkillUse(skill.Id, enemy.Id))
                    : new ActivateCommand(unit.Id, new Cell(unit.Position.X + (unit.Team == 0 ? 1 : -1), unit.Position.Y));
                original.Submit(command);
            }
            original.Tick();
        }
        Assert.Contains(original.EventLog, e => e is UnitMoved);
        Assert.Contains(original.EventLog, e => e is UnitDamaged);
        Assert.Contains(original.EventLog, e => e is CommandRejected);
        Assert.Contains(original.EventLog, e => e is StatusApplied);
        Assert.Contains(original.EventLog, e => e is StatusEnded);

        var replayed = CombatSimulation.Replay(setup, original.CommandLog, original.CurrentTick);

        Assert.Equal(original.EventLog, replayed.EventLog);
        Assert.Equal(
            original.Units.Select(u => (u.Id, u.Position, u.Gauge, u.Hp, u.Status, string.Join(",", u.StatusEndTicks))),
            replayed.Units.Select(u => (u.Id, u.Position, u.Gauge, u.Hp, u.Status, string.Join(",", u.StatusEndTicks))));
    }

    [Fact]
    public void Setup_CopiesUnitAndSkillLists()
    {
        var skills = new[] { Strike };
        var units = new[] { Unit(1, team: 0, 0, 0, skills: skills) };
        var setup = Setup(new Grid(3, 1), units);

        units[0] = Unit(2, team: 0, 1, 0);
        skills[0] = Smash;

        Assert.Equal(new UnitId(1), setup.Units.Single().Id);
        Assert.Equal("strike", setup.Units.Single().Skills.Single().Id);
        Assert.IsNotType<UnitSetup[]>(setup.Units);
    }

    [Fact]
    public void Replay_RejectsUnorderedCommandLog()
    {
        var setup = Setup(new Grid(3, 1), Unit(1, team: 0, 0, 0));
        var log = new[]
        {
            new LoggedCommand(5, new ActivateCommand(new UnitId(1), new Cell(1, 0))),
            new LoggedCommand(3, new ActivateCommand(new UnitId(1), new Cell(2, 0))),
        };

        Assert.Throws<ArgumentException>(() => CombatSimulation.Replay(setup, log, untilTick: 10));
    }

    [Fact]
    public void Logs_CannotBeModifiedFromOutside()
    {
        var sim = Simulation(new Grid(3, 1), Unit(1, team: 0, 0, 0));

        Assert.IsNotType<List<CombatEvent>>(sim.EventLog);
        Assert.IsNotType<List<LoggedCommand>>(sim.CommandLog);
        Assert.IsNotType<List<Unit>>(sim.Units);
    }
}
