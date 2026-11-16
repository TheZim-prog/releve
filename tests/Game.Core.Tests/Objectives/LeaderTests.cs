using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Objectives;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Objectives;

public class LeaderTests
{
    /// <summary>Un héros prêt au contact du chef (10 PV) ; deux sbires au loin ; d'autres unités au besoin.</summary>
    private static CombatSimulation HeroFacingLeader(MinionReaction reaction, DefeatOutcome onDefeat = DefeatOutcome.Removed,
        params UnitSetup[] others)
    {
        var units = new List<UnitSetup>
        {
            Unit(1, team: 0, 0, 1, gauge: Capacity),
            Unit(2, team: 1, 1, 1, hp: 10, onDefeat: onDefeat) with { MinionsOnFall = reaction },
            Unit(3, team: 1, 6, 0),
            Unit(4, team: 1, 6, 2),
        };
        units.AddRange(others);
        return new CombatSimulation(Setup(new Grid(8, 3), units.ToArray()) with { Objectives = ObjectiveDefinition.Default });
    }

    private static IReadOnlyList<CombatEvent> FellTheLeader(CombatSimulation sim)
    {
        sim.Submit(Use(new UnitId(1), "strike", new UnitId(2)));
        return sim.Tick();
    }

    [Fact]
    public void WhenTheLeaderFalls_HisMinionsSurrender_AndTheCombatIsWon()
    {
        var sim = HeroFacingLeader(MinionReaction.Surrender);

        var events = FellTheLeader(sim);

        Assert.Contains(new LeaderFell(1, new UnitId(2), MinionReaction.Surrender), events);
        Assert.Contains(new UnitSurrendered(1, new UnitId(3)), events);
        Assert.Contains(new UnitSurrendered(1, new UnitId(4)), events);
        Assert.All(new[] { sim.Get(3), sim.Get(4) }, m => Assert.True(m is { Status: UnitStatus.Removed, Surrendered: true }));
        Assert.True(sim.Outcome);
    }

    [Fact]
    public void WhenTheLeaderFalls_HisMinionsFlee_AndTheCombatIsWonWithoutCatchingThem()
    {
        var sim = HeroFacingLeader(MinionReaction.Flee);

        var events = FellTheLeader(sim);

        Assert.Contains(new UnitFleeing(1, new UnitId(3)), events);
        Assert.All(new[] { sim.Get(3), sim.Get(4) }, m => Assert.True(m is { Status: UnitStatus.Active, IsFleeing: true }));
        Assert.True(sim.Outcome);
    }

    [Fact]
    public void ALeaderDowned_HasFallenToo()
    {
        var sim = HeroFacingLeader(MinionReaction.Surrender, DefeatOutcome.Downed);

        FellTheLeader(sim);

        Assert.True(sim.Get(3).Surrendered);
    }

    [Fact]
    public void ALeaderWhoEscapes_HasFallenToo()
    {
        // Le chef, en fuite et prêt, au bord de la carte : il s'enfuit à son activation.
        var sim = new CombatSimulation(Setup(new Grid(8, 3), Unit(1, team: 0, 0, 1),
                Unit(2, team: 1, 3, 0, gauge: Capacity, behavior: Behaviors.Basic) with { MinionsOnFall = MinionReaction.Surrender },
                Unit(3, team: 1, 6, 1))
            with { Objectives = ObjectiveDefinition.Default });
        sim.Get(2).IsFleeing = true;

        var events = sim.Tick();

        Assert.True(sim.Get(2).Escaped);
        Assert.Contains(new UnitSurrendered(1, new UnitId(3)), events);
        Assert.True(sim.Outcome);
    }

    [Fact]
    public void MinionsSurrender_RightAfterTheBlowThatFellsTheirLeader()
    {
        // Un sbire prêt, au contact du héros : il aurait frappé plus tard dans le même tick.
        var sim = HeroFacingLeader(MinionReaction.Surrender,
            others: Unit(5, team: 1, 0, 2, gauge: Capacity, behavior: Behaviors.Basic));

        var events = FellTheLeader(sim);

        Assert.True(sim.Get(5).Surrendered);
        Assert.DoesNotContain(events, e => e is SkillUsed { Unit.Value: 5 });
        Assert.Equal(100, sim.Get(1).Hp);
    }

    [Fact]
    public void ADownedMinion_SurrendersToo_OrFleesOnceRevived()
    {
        var surrender = HeroFacingLeader(MinionReaction.Surrender);
        surrender.Get(3).Status = UnitStatus.Downed;
        FellTheLeader(surrender);
        Assert.True(surrender.Get(3) is { Status: UnitStatus.Removed, Surrendered: true });

        var flee = HeroFacingLeader(MinionReaction.Flee);
        flee.Get(3).Status = UnitStatus.Downed;
        var events = FellTheLeader(flee);
        Assert.True(flee.Get(3) is { Status: UnitStatus.Downed, IsFleeing: true });
        Assert.DoesNotContain(new UnitFleeing(1, new UnitId(3)), events); // rien à annoncer : il est à terre
    }

    [Fact]
    public void OnlyTheLeadersOwnTeam_Reacts_AndOnlyOnce()
    {
        var sim = HeroFacingLeader(MinionReaction.Surrender, others: Unit(5, team: 3, 6, 1));

        var events = FellTheLeader(sim).Concat(sim.TickTimes(3)).ToList();

        Assert.Single(events.OfType<LeaderFell>());
        Assert.Equal(UnitStatus.Active, sim.Get(5).Status);
        Assert.Null(sim.Outcome); // l'autre bande se bat encore
    }

    [Fact]
    public void WhileTheLeaderStands_NothingHappens()
    {
        var sim = HeroFacingLeader(MinionReaction.Surrender);
        sim.Submit(Use(new UnitId(1), "strike", new UnitId(3))); // hors de portée : refusé, rien ne tombe

        var events = sim.TickTimes(3);

        Assert.DoesNotContain(events, e => e is LeaderFell or UnitSurrendered);
        Assert.Equal(UnitStatus.Active, sim.Get(4).Status);
    }
}
