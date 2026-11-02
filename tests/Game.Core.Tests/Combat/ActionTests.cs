using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class ActionTests
{
    private static readonly UnitId Hero = new(1);
    private static readonly UnitId Foe = new(2);

    private static ActivateCommand Attack(UnitId attacker, UnitId target, string skill = "strike", Cell? moveTo = null) =>
        new(attacker, moveTo, new SkillUse(skill, target));

    [Fact]
    public void LightAttack_DealsDamage_AndRestartsGaugeAt40Percent()
    {
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity),
            Unit(2, team: 1, 1, 0, hp: 50));

        sim.Submit(Attack(Hero, Foe));

        Assert.Equal(
            new CombatEvent[]
            {
                new SkillUsed(1, Hero, "strike", Foe),
                new UnitDamaged(1, Foe, 10, 40),
            },
            sim.Tick().Where(e => e is not UnitReady));
        Assert.Equal(40, sim.Get(2).Hp);
        Assert.Equal(40 + 10, sim.Get(1).Gauge); // reprise à 40, puis +initiative
    }

    [Fact]
    public void HeavyAttack_RestartsGaugeFromZero()
    {
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity),
            Unit(2, team: 1, 1, 0));

        sim.Submit(Attack(Hero, Foe, "smash"));
        sim.Tick();

        Assert.Equal(70, sim.Get(2).Hp);
        Assert.Equal(10, sim.Get(1).Gauge);
    }

    [Fact]
    public void MoveThenAttack_AttacksFromDestination_WithActionWeight()
    {
        var sim = Simulation(new Grid(5, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity),
            Unit(2, team: 1, 4, 0));

        sim.Submit(Attack(Hero, Foe, moveTo: new Cell(3, 0)));

        Assert.Equal(
            new CombatEvent[]
            {
                new UnitMoved(1, Hero, new Cell(0, 0), new[] { new Cell(1, 0), new Cell(2, 0), new Cell(3, 0) }),
                new SkillUsed(1, Hero, "strike", Foe),
                new UnitDamaged(1, Foe, 10, 90),
            },
            sim.Tick());
        Assert.Equal(40 + 10, sim.Get(1).Gauge); // poids de l'action, pas du déplacement
    }

    [Fact]
    public void InvalidAction_CancelsTheWholeCommand_IncludingTheMove()
    {
        var sim = Simulation(new Grid(6, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity),
            Unit(2, team: 1, 5, 0));

        var command = Attack(Hero, Foe, moveTo: new Cell(2, 0)); // de (2,0), la cible en (5,0) est hors de portée
        sim.Submit(command);

        Assert.Equal(new CommandRejected(1, command, RejectionReason.OutOfRange), sim.Tick().Single());
        Assert.Equal(new Cell(0, 0), sim.Get(1).Position);
        Assert.True(sim.Get(1).IsReady);
    }

    [Fact]
    public void DiagonalMelee_IsBlockedByAWallCorner()
    {
        var open = Simulation(Grid.FromRows("..", ".."),
            Unit(1, team: 0, 0, 1, gauge: Capacity),
            Unit(2, team: 1, 1, 0));
        var cornered = Simulation(Grid.FromRows("#.", ".."),
            Unit(1, team: 0, 0, 1, gauge: Capacity),
            Unit(2, team: 1, 1, 0));

        open.Submit(Attack(Hero, Foe));
        var blocked = Attack(Hero, Foe);
        cornered.Submit(blocked);

        Assert.Contains(open.Tick(), e => e is UnitDamaged);
        Assert.Equal(new CommandRejected(1, blocked, RejectionReason.OutOfRange), cornered.Tick().Single());
    }

    [Fact]
    public void InvalidTargetsAndSkills_AreRejected()
    {
        var sim = Simulation(new Grid(4, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity),
            Unit(3, team: 0, 1, 0),
            Unit(2, team: 1, 2, 0));

        var ally = Attack(Hero, new UnitId(3));
        var self = Attack(Hero, Hero);
        var nobody = Attack(Hero, new UnitId(99));
        var unknownSkill = Attack(Hero, Foe, "fireball");
        foreach (var command in new[] { ally, self, nobody, unknownSkill })
            sim.Submit(command);

        Assert.Equal(
            new CombatEvent[]
            {
                new CommandRejected(1, ally, RejectionReason.InvalidTarget),
                new CommandRejected(1, self, RejectionReason.InvalidTarget),
                new CommandRejected(1, nobody, RejectionReason.InvalidTarget),
                new CommandRejected(1, unknownSkill, RejectionReason.UnknownSkill),
            },
            sim.Tick());
    }

    [Fact]
    public void Cooldown_BlocksTheSkillUntilItEnds_OtherSkillsStayUsable()
    {
        // Initiative = capacité : l'unité est de nouveau prête à chaque tick.
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, initiative: Capacity, gauge: Capacity),
            Unit(2, team: 1, 1, 0, hp: 10_000));

        sim.Submit(Attack(Hero, Foe, "smash")); // tick 1 : recharge jusqu'au tick 101
        sim.Tick();

        var tooSoon = Attack(Hero, Foe, "smash");
        sim.Submit(tooSoon);
        Assert.Contains(new CommandRejected(2, tooSoon, RejectionReason.OnCooldown), sim.Tick());

        sim.Submit(Attack(Hero, Foe, "strike"));
        Assert.Contains(sim.Tick(), e => e is SkillUsed { SkillId: "strike" });

        sim.TickTimes(96); // ticks 4 à 99
        var stillTooSoon = Attack(Hero, Foe, "smash");
        sim.Submit(stillTooSoon);
        Assert.Contains(new CommandRejected(100, stillTooSoon, RejectionReason.OnCooldown), sim.Tick());

        sim.Submit(Attack(Hero, Foe, "smash"));
        Assert.Contains(sim.Tick(), e => e is SkillUsed { Tick: 101, SkillId: "smash" });
    }

    [Fact]
    public void Damage_IsDrawnWithinRange_AndIsReproducibleFromTheSeed()
    {
        var ranged = Strike with { MinDamage = 5, MaxDamage = 15 };

        List<int> Damages(ulong seed)
        {
            var setup = Setup(new Grid(3, 1),
                Unit(1, team: 0, 0, 0, initiative: Capacity, gauge: Capacity, skills: new[] { ranged }),
                Unit(2, team: 1, 1, 0, hp: 10_000));
            var sim = new CombatSimulation(setup with { Seed = seed });
            for (var i = 0; i < 50; i++)
            {
                sim.Submit(Attack(Hero, Foe));
                sim.Tick();
            }
            return sim.EventLog.OfType<UnitDamaged>().Select(e => e.Amount).ToList();
        }

        var first = Damages(seed: 7);

        Assert.Equal(50, first.Count);
        Assert.All(first, d => Assert.InRange(d, 5, 15));
        Assert.True(first.Distinct().Count() > 1);
        Assert.Equal(first, Damages(seed: 7));
        Assert.NotEqual(first, Damages(seed: 8));
    }

    [Fact]
    public void DefeatedEnemy_IsRemoved_AndFreesItsCell()
    {
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity),
            Unit(2, team: 1, 1, 0, hp: 5, gauge: 50));

        sim.Submit(Attack(Hero, Foe));
        var events = sim.Tick();

        Assert.Equal(new UnitDamaged(1, Foe, 10, 0), events[1]);
        Assert.Equal(new UnitRemoved(1, Foe), events[2]);
        var foe = sim.Get(2);
        Assert.Equal(UnitStatus.Removed, foe.Status);
        Assert.Null(sim.UnitAt(new Cell(1, 0)));

        sim.TickTimes(10); // la jauge du vaincu est gelée
        Assert.Equal(50, foe.Gauge);

        var ghost = new ActivateCommand(Foe, new Cell(2, 0));
        sim.Submit(ghost);
        Assert.Equal(new CommandRejected(12, ghost, RejectionReason.Incapacitated), sim.Tick().Single());
    }

    [Fact]
    public void DefeatedHero_IsDowned_StaysOnItsCell_AndCannotBeTargeted()
    {
        var sim = Simulation(new Grid(4, 1),
            Unit(1, team: 0, 1, 0, hp: 5, gauge: 30, onDefeat: DefeatOutcome.Downed),
            Unit(2, team: 1, 2, 0, initiative: Capacity, gauge: Capacity),
            Unit(3, team: 1, 3, 0, initiative: Capacity, gauge: Capacity));

        sim.Submit(Attack(Foe, Hero));
        Assert.Contains(new UnitDowned(1, Hero), sim.Tick());

        var hero = sim.Get(1);
        Assert.Equal(UnitStatus.Downed, hero.Status);
        Assert.Same(hero, sim.UnitAt(new Cell(1, 0)));

        // Il bloque toujours sa case pour l'ennemi, et ne peut plus être frappé.
        var walkOnto = new ActivateCommand(new UnitId(3), new Cell(0, 0));
        var hitAgain = Attack(Foe, Hero);
        sim.Submit(walkOnto);
        sim.Submit(hitAgain);
        Assert.Equal(
            new CombatEvent[]
            {
                new CommandRejected(2, walkOnto, RejectionReason.Unreachable),
                new CommandRejected(2, hitAgain, RejectionReason.InvalidTarget),
            },
            sim.Tick());

        sim.TickTimes(10); // jauge gelée à sa valeur au moment de la chute
        Assert.Equal(30, hero.Gauge);
        Assert.False(hero.IsReady);
    }
}
