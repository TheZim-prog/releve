using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class ReviveTests
{
    private static readonly UnitId Helper = new(1);
    private static readonly UnitId Fallen = new(2);

    private static CombatSimulation WithFallen(Grid grid, params UnitSetup[] units)
    {
        var sim = Simulation(grid, units);
        var fallen = sim.Get(2);
        fallen.Hp = 0;
        fallen.Status = UnitStatus.Downed;
        fallen.Gauge = 70;
        return sim;
    }

    private static ActivateCommand Revive(Cell? moveTo = null, int target = 2) =>
        new(Helper, moveTo, Interaction: new InteractionUse(InteractionKind.Revive, new UnitId(target)));

    [Fact]
    public void Revive_PutsTheAllyBackOnItsFeet_WithAShareOfItsHp_AndAnEmptyGauge()
    {
        var sim = WithFallen(new Grid(6, 3), Unit(1, team: 0, 1, 1, gauge: Capacity), Unit(2, team: 0, 2, 1, hp: 90, onDefeat: DefeatOutcome.Downed));

        sim.Submit(Revive());
        var events = sim.Tick();

        Assert.Contains(new UnitRevived(1, Fallen, Helper, 22), events); // 25 % de 90, arrondi en dessous
        var fallen = sim.Get(2);
        Assert.Equal(UnitStatus.Active, fallen.Status);
        Assert.Equal(22, fallen.Hp);
        Assert.Equal(10, fallen.Gauge); // repartie de 0, plus le remplissage du tick
        Assert.Equal(Capacity * 40 / 100 + 10, sim.Get(1).Gauge); // action légère
    }

    [Fact]
    public void Revive_EndsTheFall_ButDoesNotRechargeOpportunityAttacks()
    {
        var sim = WithFallen(new Grid(6, 3), Unit(1, team: 0, 1, 1, gauge: Capacity), Unit(2, team: 0, 2, 1, onDefeat: DefeatOutcome.Downed));
        var fallen = sim.Get(2);
        fallen.WasRescued = true;
        fallen.OpportunityAttacksLeft = 0; // dépensée avant de tomber

        sim.Submit(Revive());
        sim.Tick();

        Assert.False(fallen.WasRescued);
        Assert.Equal(0, fallen.OpportunityAttacksLeft); // rechargée quand il rejouera
    }

    [Fact]
    public void Revive_WeightComesFromTheRules()
    {
        var setup = Setup(new Grid(6, 3), Unit(1, team: 0, 1, 1, gauge: Capacity), Unit(2, team: 0, 2, 1, onDefeat: DefeatOutcome.Downed));
        var sim = new CombatSimulation(setup with { Rules = Rules with { ReviveWeight = SkillWeight.Heavy } });
        sim.Get(2).Status = UnitStatus.Downed;

        sim.Submit(Revive());
        sim.Tick();

        Assert.Equal(10, sim.Get(1).Gauge); // action lourde : repartie de 0
    }

    [Fact]
    public void AnUnknownInteraction_IsRejected()
    {
        var sim = WithFallen(new Grid(6, 3), Unit(1, team: 0, 1, 1, gauge: Capacity), Unit(2, team: 0, 2, 1, onDefeat: DefeatOutcome.Downed));
        var command = new ActivateCommand(Helper, null, Interaction: new InteractionUse((InteractionKind)99, Fallen));

        sim.Submit(command);

        Assert.Equal(new CommandRejected(1, command, RejectionReason.InvalidTarget), sim.Tick().Single());
    }

    [Fact]
    public void Revive_GivesAtLeastOneHp()
    {
        var sim = WithFallen(new Grid(6, 3), Unit(1, team: 0, 1, 1, gauge: Capacity), Unit(2, team: 0, 2, 1, hp: 3, onDefeat: DefeatOutcome.Downed));

        sim.Submit(Revive());
        sim.Tick();

        Assert.Equal(1, sim.Get(2).Hp);
    }

    [Fact]
    public void MoveThenRevive_IsOneActivation()
    {
        var sim = WithFallen(new Grid(8, 3), Unit(1, team: 0, 0, 1, gauge: Capacity), Unit(2, team: 0, 4, 1, onDefeat: DefeatOutcome.Downed));

        sim.Submit(Revive(new Cell(3, 1)));
        var events = sim.Tick();

        Assert.Contains(events, e => e is UnitMoved { Unit.Value: 1 });
        Assert.Contains(events, e => e is UnitRevived);
    }

    [Fact]
    public void Revive_NeedsContact_WithoutCuttingAWallCorner()
    {
        var grid = Grid.FromRows(
            ".#...",
            ".....");
        var far = WithFallen(grid, Unit(1, team: 0, 0, 1, gauge: Capacity), Unit(2, team: 0, 3, 1, onDefeat: DefeatOutcome.Downed));
        var tooFar = Revive();
        far.Submit(tooFar);
        Assert.Equal(new CommandRejected(1, tooFar, RejectionReason.OutOfRange), far.Tick().Single());

        // En diagonale de (0,0) à (1,1), le coin du mur (1,0) coupe le contact.
        var corner = WithFallen(grid, Unit(1, team: 0, 0, 0, gauge: Capacity), Unit(2, team: 0, 1, 1, onDefeat: DefeatOutcome.Downed));
        var blocked = Revive();
        corner.Submit(blocked);
        Assert.Equal(new CommandRejected(1, blocked, RejectionReason.OutOfRange), corner.Tick().Single());
    }

    [Fact]
    public void Revive_OnlyForADownedAlly()
    {
        var sim = WithFallen(new Grid(6, 3),
            Unit(1, team: 0, 1, 1, gauge: Capacity), Unit(2, team: 0, 2, 1, onDefeat: DefeatOutcome.Downed),
            Unit(3, team: 0, 1, 0), Unit(9, team: 1, 0, 1));
        sim.Get(9).Status = UnitStatus.Downed;

        var standing = Revive(target: 3);
        var enemy = Revive(target: 9);
        var self = Revive(target: 1);
        foreach (var command in new[] { standing, enemy, self })
            sim.Submit(command);

        Assert.Equal(
            new[] { standing, enemy, self }.Select(c => new CommandRejected(1, c, RejectionReason.InvalidTarget)),
            sim.Tick().OfType<CommandRejected>());
    }

    [Fact]
    public void ASkillAndAnInteraction_AreRejectedTogether()
    {
        var sim = WithFallen(new Grid(6, 3), Unit(1, team: 0, 1, 1, gauge: Capacity), Unit(2, team: 0, 2, 1, onDefeat: DefeatOutcome.Downed),
            Unit(9, team: 1, 0, 1));

        var both = Revive() with { Skill = new SkillUse("strike", new UnitId(9)) };
        sim.Submit(both);

        Assert.Equal(new CommandRejected(1, both, RejectionReason.TwoActions), sim.Tick().Single());
    }

    [Fact]
    public void Revive_IsCancelled_IfTheHelperFallsOnTheWay()
    {
        // Un ennemi au contact frappe le releveur qui s'éloigne : il tombe, l'allié reste à terre.
        var sim = WithFallen(new Grid(8, 3),
            Unit(1, team: 0, 1, 1, hp: 5, gauge: Capacity, onDefeat: DefeatOutcome.Downed),
            Unit(2, team: 0, 5, 1, onDefeat: DefeatOutcome.Downed),
            Unit(9, team: 1, 0, 1, basicAttack: "strike"));

        sim.Submit(Revive(new Cell(4, 1)));
        var events = sim.Tick();

        Assert.Contains(events, e => e is OpportunityAttack);
        Assert.DoesNotContain(events, e => e is UnitRevived);
        Assert.Equal(UnitStatus.Downed, sim.Get(2).Status);
    }

    [Fact]
    public void ARevivedUnit_CanBeCommandedAgain_OnceItsGaugeIsFull()
    {
        var sim = WithFallen(new Grid(6, 3), Unit(1, team: 0, 1, 1, gauge: Capacity), Unit(2, team: 0, 2, 1, onDefeat: DefeatOutcome.Downed));
        sim.Submit(Revive());
        sim.Tick();

        var events = sim.TickTimes(10);

        Assert.Contains(new UnitReady(10, Fallen), events); // 10 par tick depuis 0, du tick 1 au tick 10
    }
}
