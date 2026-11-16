using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Tourniquet : l'ennemi attrapé frappe tout autour du lanceur, puis il est lancé vers la case choisie.</summary>
public class SpinThrowTests
{
    private static readonly UnitId Colossus = new(1);
    private static readonly UnitId Grabbed = new(9);

    /// <summary>10 dégâts à tout le carré 3x3 autour de lui, puis l'ennemi attrapé part à 3 cases.</summary>
    private static readonly SkillDefinition Spin = Strike with
    {
        Id = "spin", Weight = SkillWeight.Heavy, Range = 0,
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Spin, Distance = 3, CollisionMinDamage = 5, CollisionMaxDamage = 5 },
    };

    private static UnitSetup ColossusAt(int x, int y, PassiveKind? passive = null) =>
        Unit(1, team: 0, x, y, gauge: Capacity, skills: new[] { Spin }, passive: passive);

    [Fact]
    public void Spin_HitsEveryoneAround_ThenThrowsTheGrabbedEnemy_TowardTheChosenCell()
    {
        var sim = Simulation(new Grid(9, 5), ColossusAt(4, 2), Unit(9, team: 1, 5, 2), Unit(8, team: 1, 4, 1), Unit(2, team: 0, 3, 3));

        sim.Submit(Use(Colossus, "spin", Grabbed, new Cell(0, 2)));
        var events = sim.Tick();

        Assert.Contains(new SkillUsed(1, Colossus, "spin", Grabbed, events.OfType<SkillUsed>().Single().Zone), events);
        Assert.Equal((90, 90, 90), (sim.Get(9).Hp, sim.Get(8).Hp, sim.Get(2).Hp));
        Assert.Contains(new UnitDisplaced(1, Grabbed, new Cell(5, 2), new Cell(1, 2)), events);
    }

    [Fact]
    public void Spin_WithNoRoomTowardTheChosenCell_LeavesTheEnemyInPlace_AndItCollides()
    {
        var sim = Simulation(new Grid(9, 5), ColossusAt(4, 2), Unit(9, team: 1, 5, 2), Unit(8, team: 1, 3, 2));

        sim.Submit(Use(Colossus, "spin", Grabbed, new Cell(0, 2)));
        var events = sim.Tick();

        Assert.DoesNotContain(events, e => e is UnitDisplaced);
        Assert.Contains(new UnitCollided(1, Grabbed, new UnitId(8)), events);
        Assert.Equal((85, 85), (sim.Get(9).Hp, sim.Get(8).Hp));
    }

    [Fact]
    public void Spin_GrabsAnAdjacentEnemy_AndNeedsADirection()
    {
        var sim = Simulation(new Grid(9, 5), ColossusAt(4, 2), Unit(9, team: 1, 6, 2), Unit(2, team: 0, 3, 2), Unit(8, team: 1, 5, 3));

        Assert.Equal(RejectionReason.OutOfRange, sim.Check(Use(Colossus, "spin", Grabbed, new Cell(0, 2))));
        Assert.Equal(RejectionReason.InvalidTarget, sim.Check(Use(Colossus, "spin", new UnitId(2), new Cell(0, 2))));
        Assert.Equal(RejectionReason.InvalidTarget, sim.Check(Use(Colossus, "spin", new UnitId(8))));
        Assert.Equal(RejectionReason.InvalidTarget, sim.Check(Use(Colossus, "spin", new UnitId(8), new Cell(4, 2))));
        Assert.Null(sim.Check(Use(Colossus, "spin", new UnitId(8), new Cell(8, 4))));
    }

    [Fact]
    public void AnUnshakableEnemy_IsHitButNotThrown()
    {
        var sim = Simulation(new Grid(9, 5), ColossusAt(4, 2), Unit(9, team: 1, 5, 2, passive: PassiveKind.Unshakable));

        sim.Submit(Use(Colossus, "spin", Grabbed, new Cell(0, 2)));
        var events = sim.Tick();

        Assert.Equal(90, sim.Get(9).Hp);
        Assert.Contains(new UnitResisted(1, Grabbed), events);
        Assert.Equal(new Cell(5, 2), sim.Get(9).Position);
    }

    [Fact]
    public void ASpin_HitsASquareAroundItsCaster()
    {
        Spin.Validate();
        Assert.Throws<ArgumentException>(() => (Spin with { Zone = null, Range = 1 }).Validate());
        Assert.Throws<ArgumentException>(() => (Spin with { Range = 2 }).Validate());
        Assert.Throws<ArgumentException>(() => (Spin with { ChargeMs = 1000 }).Validate());
    }
}
