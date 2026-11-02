using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class OpportunityAttackTests
{
    private static readonly UnitId Runner = new(1);
    private static readonly UnitId Guard = new(2);

    /// <summary>Le coureur (équipe 0) est prêt ; il n'a pas d'attaque de base, donc ne punit personne.</summary>
    private static UnitSetup RunnerAt(int x, int y, int id = 1, int hp = 100, int mobility = 5) =>
        Unit(id, team: 0, x, y, gauge: Capacity, hp: hp, mobility: mobility, onDefeat: DefeatOutcome.Downed);

    /// <summary>Un garde ennemi dont l'attaque de base est <paramref name="basic"/> (Strike, 10 dégâts, par défaut).</summary>
    private static UnitSetup GuardAt(int id, int x, int y, int opportunityAttacks = 1, SkillDefinition? basic = null) =>
        Unit(id, team: 1, x, y, skills: new[] { basic ?? Strike }, basicAttack: (basic ?? Strike).Id,
            opportunityAttacks: opportunityAttacks);

    private static IReadOnlyList<CombatEvent> Move(CombatSimulation sim, Cell to, SkillUse? action = null, UnitId? who = null)
    {
        sim.Submit(new ActivateCommand(who ?? Runner, to, action));
        return sim.Tick().Where(e => e is not UnitReady).ToList();
    }

    [Fact]
    public void LeavingTheEnemysReach_TriggersItsBasicAttack()
    {
        var sim = Simulation(new Grid(5, 1), RunnerAt(1, 0), GuardAt(2, 0, 0));

        Assert.Equal(
            new CombatEvent[]
            {
                new OpportunityAttack(1, Guard, Runner, "strike"),
                new UnitDamaged(1, Runner, 10, 90),
                new UnitMoved(1, Runner, new Cell(1, 0), new[] { new Cell(2, 0), new Cell(3, 0) }),
            },
            Move(sim, new Cell(3, 0)));
        Assert.Equal(0, sim.Get(2).OpportunityAttacksLeft);
    }

    [Fact]
    public void MoveIsSplitAroundTheAttack_AtTheStepThatLeavesTheReach()
    {
        // Trajet (1,0), (2,0), (3,0), (4,0) le long du garde en (2,1) : seul le pas (3,0) → (4,0) sort de sa portée.
        var sim = Simulation(new Grid(5, 2), RunnerAt(0, 0), GuardAt(2, 2, 1));

        Assert.Equal(
            new CombatEvent[]
            {
                new UnitMoved(1, Runner, new Cell(0, 0), new[] { new Cell(1, 0), new Cell(2, 0), new Cell(3, 0) }),
                new OpportunityAttack(1, Guard, Runner, "strike"),
                new UnitDamaged(1, Runner, 10, 90),
                new UnitMoved(1, Runner, new Cell(3, 0), new[] { new Cell(4, 0) }),
            },
            Move(sim, new Cell(4, 0)));
    }

    [Fact]
    public void CirclingAroundAnEnemy_IsFree()
    {
        // De (1,1) à (3,1) autour du garde en (2,1) : (1,0), (2,0), (3,0), (3,1) restent tous à son contact.
        var sim = Simulation(new Grid(5, 3), RunnerAt(1, 1), GuardAt(2, 2, 1, opportunityAttacks: 3));

        Assert.DoesNotContain(Move(sim, new Cell(3, 1)), e => e is OpportunityAttack);
    }

    [Fact]
    public void OneAttackPerCycle_RechargedWhenTheEnemyActs()
    {
        // Deux coureurs fuient le même garde : il n'a qu'une attaque par cycle.
        var sim = Simulation(new Grid(5, 3), RunnerAt(1, 1), RunnerAt(3, 1, id: 3), GuardAt(2, 2, 1));

        Assert.Single(Move(sim, new Cell(0, 1)).OfType<OpportunityAttack>());
        Assert.DoesNotContain(Move(sim, new Cell(4, 1), who: new UnitId(3)), e => e is OpportunityAttack);

        // Le garde (initiative 10) est prêt au tick 10 ; il rejoue et récupère son attaque.
        sim.TickTimes(8);
        sim.Submit(new ActivateCommand(Guard, new Cell(2, 0)));
        sim.Tick();
        Assert.Equal(1, sim.Get(2).OpportunityAttacksLeft);
    }

    [Fact]
    public void SeveralAttacksPerCycle_PunishSeveralUnits()
    {
        var sim = Simulation(new Grid(5, 3), RunnerAt(1, 1), RunnerAt(3, 1, id: 3), GuardAt(2, 2, 1, opportunityAttacks: 2));

        Assert.Single(Move(sim, new Cell(0, 1)).OfType<OpportunityAttack>());
        Assert.Single(Move(sim, new Cell(4, 1), who: new UnitId(3)).OfType<OpportunityAttack>());
        Assert.Equal(0, sim.Get(2).OpportunityAttacksLeft);
    }

    [Fact]
    public void EachEnemyLeftBehind_Punishes_InUnitOrder()
    {
        // En quittant (1,1) pour (2,1), le coureur sort de la portée des gardes en (0,1) et (0,2).
        var sim = Simulation(new Grid(4, 3), RunnerAt(1, 1), GuardAt(3, 0, 1), GuardAt(2, 0, 2));

        var attackers = Move(sim, new Cell(3, 1)).OfType<OpportunityAttack>().Select(a => a.Attacker.Value);

        Assert.Equal(new[] { 3, 2 }, attackers);
    }

    [Fact]
    public void StunnedEnemy_DoesNotPunish()
    {
        var stun = new StatusEffect { Status = StatusKind.Stunned, DurationMs = 5000 };
        var sim = Simulation(new Grid(5, 1),
            Unit(1, team: 0, 1, 0, gauge: Capacity, initiative: Capacity, skills: new[] { Strike with { Effects = new[] { stun } } }),
            GuardAt(2, 0, 0));
        sim.Submit(new ActivateCommand(Runner, null, new SkillUse("strike", Guard)));
        sim.Tick();

        Assert.DoesNotContain(Move(sim, new Cell(3, 0)), e => e is OpportunityAttack);
    }

    [Fact]
    public void WallOnTheCorner_MeansTheUnitWasNotInReach()
    {
        // Garde en (0,1), coureur en (1,0) : la diagonale frôle le mur (0,0), comme pour le corps à corps.
        var sim = Simulation(Grid.FromRows("#..", "..."), RunnerAt(1, 0), GuardAt(2, 0, 1));

        Assert.DoesNotContain(Move(sim, new Cell(2, 0)), e => e is OpportunityAttack);
    }

    [Fact]
    public void UnitWithThePassive_IgnoresOpportunityAttacks()
    {
        var speedster = Unit(1, team: 0, 1, 0, gauge: Capacity, mobility: 5, passive: PassiveKind.IgnoresOpportunityAttacks);
        var sim = Simulation(new Grid(5, 1), speedster, GuardAt(2, 0, 0));

        var events = Move(sim, new Cell(3, 0));

        Assert.Equal(new UnitMoved(1, Runner, new Cell(1, 0), new[] { new Cell(2, 0), new Cell(3, 0) }), Assert.Single(events));
        Assert.Equal(1, sim.Get(2).OpportunityAttacksLeft);
    }

    [Fact]
    public void UnitWithoutBasicAttack_DoesNotPunish()
    {
        var sim = Simulation(new Grid(5, 1), RunnerAt(1, 0), Unit(2, team: 1, 0, 0));

        Assert.DoesNotContain(Move(sim, new Cell(3, 0)), e => e is OpportunityAttack);
    }

    [Fact]
    public void FallingOnTheWay_StopsTheMove_AndCancelsTheAction()
    {
        var sim = Simulation(new Grid(6, 1),
            RunnerAt(1, 0, hp: 10),
            GuardAt(2, 0, 0),
            Unit(3, team: 1, 5, 0));

        var events = Move(sim, new Cell(4, 0), new SkillUse("strike", new UnitId(3)));

        Assert.Equal(
            new CombatEvent[]
            {
                new OpportunityAttack(1, Guard, Runner, "strike"),
                new UnitDamaged(1, Runner, 10, 0),
                new UnitDowned(1, Runner, new Cell(1, 0)),
            },
            events);
        Assert.Equal(new Cell(1, 0), sim.Get(1).Position);
        Assert.Equal(100, sim.Get(3).Hp);
    }

    [Fact]
    public void FallingWhileCrossingAnAlly_LandsOnTheLastFreeCell()
    {
        // Trajet (1,1), (2,1) [allié], (3,1). Le garde en (1,0) atteint (0,1), (1,1) et (2,1) mais pas (3,1) :
        // le coureur sort de sa portée en quittant la case de l'allié, et tombe sur (1,1).
        var grid = Grid.FromRows(
            ".....",
            ".....",
            "#####");
        var sim = Simulation(grid, RunnerAt(0, 1, hp: 10), Unit(4, team: 0, 2, 1), GuardAt(2, 1, 0));

        var events = Move(sim, new Cell(3, 1));

        Assert.Equal(new UnitMoved(1, Runner, new Cell(0, 1), new[] { new Cell(1, 1), new Cell(2, 1) }), events[0]);
        Assert.Equal(new UnitDowned(1, Runner, new Cell(1, 1)), events.OfType<UnitDowned>().Single());
        Assert.Equal(new Cell(1, 1), sim.Get(1).Position);
        Assert.Same(sim.Get(4), sim.UnitAt(new Cell(2, 1)));
    }

    [Fact]
    public void StunnedOnTheWay_StopsTheMove_CancelsTheAction_AndRestartsAsAMove()
    {
        var dazing = Strike with { Id = "dazing", Effects = new[] { new StatusEffect { Status = StatusKind.Stunned, DurationMs = 500 } } };
        var sim = Simulation(new Grid(6, 1),
            RunnerAt(1, 0),
            GuardAt(2, 0, 0, basic: dazing),
            Unit(3, team: 1, 5, 0));

        var events = Move(sim, new Cell(4, 0), new SkillUse("strike", new UnitId(3)));

        Assert.Equal(
            new CombatEvent[]
            {
                new OpportunityAttack(1, Guard, Runner, "dazing"),
                new UnitDamaged(1, Runner, 10, 90),
                new StatusApplied(1, Runner, StatusKind.Stunned, 11),
                new UnitStopped(1, Runner, new Cell(1, 0)),
            },
            events);
        Assert.Equal(100, sim.Get(3).Hp);
        Assert.Equal(Rules.GaugeAt(Rules.MoveOnlyRestartPercent), sim.Get(1).Gauge); // gelée par l'étourdissement
    }

    [Fact]
    public void Setup_RejectsABasicAttackTheUnitDoesNotHave()
    {
        Assert.Throws<ArgumentException>(() =>
            Simulation(new Grid(2, 1), Unit(1, team: 0, 0, 0, basicAttack: "fireball")));
        Assert.Throws<ArgumentException>(() =>
            Simulation(new Grid(2, 1), Unit(1, team: 0, 0, 0, opportunityAttacks: -1)));
    }
}
