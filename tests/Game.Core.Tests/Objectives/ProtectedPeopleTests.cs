using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Objectives;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Objectives;

public class ProtectedPeopleTests
{
    /// <summary>Une personne à protéger, prudente, prête.</summary>
    private static UnitSetup Witness(int x, int y, string behavior = Behaviors.Cautious) =>
        Unit(5, team: 0, x, y, gauge: Capacity, behavior: behavior) with { IsProtected = true };

    /// <summary>Un combat avec ces unités, et une protection d'une minute (pour que la marque « protégée » ait un sens).</summary>
    private static CombatSimulation Guarded(Grid grid, params UnitSetup[] units) =>
        new(Setup(grid, units) with
        {
            Objectives = new[] { new ObjectiveDefinition { Kind = ObjectiveKind.Protect, Until = ProtectUntil.Time, DurationMs = 60000 } },
        });

    [Fact]
    public void ACautiousPerson_WaitsWhileNobodyAttacksIt()
    {
        var sim = Guarded(new Grid(8, 1), Witness(3, 0), Unit(2, team: 1, 5, 0));

        var events = sim.TickTimes(5);

        Assert.Equal(new Cell(3, 0), sim.Get(5).Position);
        Assert.DoesNotContain(events, e => e is UnitMoved or SkillUsed);
    }

    [Fact]
    public void ACautiousPerson_BacksAwayOnceAttacked_AndNeverStrikes()
    {
        var sim = Guarded(new Grid(8, 1), Witness(3, 0), Unit(2, team: 1, 4, 0, gauge: Capacity));
        sim.Submit(Use(new UnitId(2), "strike", new UnitId(5)));

        var events = sim.TickTimes(2);

        Assert.Equal(new Cell(0, 0), sim.Get(5).Position); // 3 cases plus loin, au bout de la rue
        Assert.DoesNotContain(events, e => e is SkillUsed { Unit.Value: 5 });
        Assert.False(sim.Get(5).AttackedSinceActivation);
    }

    [Fact]
    public void ACautiousPerson_BacksAwayEvenFromAHitThatDealsNoDamage()
    {
        // Elle esquive le coup (Réflexes) : aucun dégât, mais elle a bien été attaquée.
        var witness = Witness(3, 0);
        var sim = Guarded(new Grid(8, 1), witness with { Definition = witness.Definition with { Passive = PassiveKind.Reflexes } },
            Unit(2, team: 1, 4, 0, gauge: Capacity));
        sim.Submit(Use(new UnitId(2), "strike", new UnitId(5)));

        var events = sim.TickTimes(2);

        Assert.Contains(events, e => e is AttackDodged);
        Assert.Equal(100, sim.Get(5).Hp);
        Assert.Equal(new Cell(0, 0), sim.Get(5).Position);
    }

    [Fact]
    public void ACautiousPerson_DoesNotBackAwayIntoAnOpportunityAttack()
    {
        // Le voyou, collé à elle, a encore son attaque d'opportunité : partir lui offrirait un coup gratuit.
        var sim = Guarded(new Grid(8, 1), Witness(3, 0), Unit(2, team: 1, 4, 0, basicAttack: "strike"));
        sim.Get(5).AttackedSinceActivation = true;

        sim.Tick();

        Assert.Equal(new Cell(3, 0), sim.Get(5).Position);
    }

    [Fact]
    public void APersonWhoDefendsItself_UsesTheBasicBehavior()
    {
        var sim = Guarded(new Grid(8, 1), Witness(3, 0, Behaviors.Basic), Unit(2, team: 1, 4, 0));

        var events = sim.Tick();

        Assert.Contains(events, e => e is SkillUsed { Unit.Value: 5 });
    }

    [Fact]
    public void EnemiesTarget_TheProtectedPersonFirst_WhenTheyCanReachIt()
    {
        // Le héros est plus fragile, mais la personne protégée est à portée : elle passe d'abord.
        var sim = Guarded(new Grid(8, 3), Unit(1, team: 0, 3, 0, hp: 20), Witness(3, 2), // 20 PV contre 100
            Unit(2, team: 1, 4, 1, gauge: Capacity, behavior: Behaviors.Basic));

        var events = sim.Tick();

        Assert.Contains(events, e => e is SkillUsed { Unit.Value: 2, Target.Value: 5 });
    }

    [Fact]
    public void AProtectedPersonOutOfReach_LeavesTheEnemyToItsUsualTarget()
    {
        var sim = Guarded(new Grid(10, 1), Witness(9, 0), Unit(1, team: 0, 3, 0), Unit(2, team: 1, 4, 0, gauge: Capacity, behavior: Behaviors.Basic));

        var events = sim.Tick();

        Assert.Contains(events, e => e is SkillUsed { Unit.Value: 2, Target.Value: 1 });
    }
}
