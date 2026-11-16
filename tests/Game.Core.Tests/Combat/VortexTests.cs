using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Vortex : les ennemis de la zone sont attirés vers son centre, les plus proches d'abord.</summary>
public class VortexTests
{
    private static readonly UnitId Speedster = new(1);

    /// <summary>Carré 5x5 à 4 cases au plus, ennemis seulement : 10 dégâts, déséquilibre, attirés de 2 cases.</summary>
    private static readonly SkillDefinition Vortex = Strike with
    {
        Id = "vortex", Weight = SkillWeight.Heavy, Range = 4,
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 2, ThroughWalls = false, Affects = ZoneTargets.Enemies },
        Effects = new[] { new StatusEffect { Status = StatusKind.OffBalance, DurationMs = 1000 } },
        ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Pull, Distance = 2 },
    };

    private static UnitSetup SpeedsterAt(int x, int y) => Unit(1, team: 0, x, y, gauge: Capacity, skills: new[] { Vortex });

    [Fact]
    public void Vortex_PullsTheEnemiesTowardsTheCenter_ClosestFirst_AndTheSpeedsterStaysPut()
    {
        var sim = Simulation(new Grid(9, 9), SpeedsterAt(0, 4),
            Unit(2, team: 0, 4, 6),   // un allié dans la zone : ni touché ni attiré
            Unit(7, team: 1, 6, 4),   // à 2 cases : bute sur celui qui a pris le centre
            Unit(8, team: 1, 5, 4),   // à 1 case : arrive au centre
            Unit(9, team: 1, 2, 2));  // en diagonale : s'arrête à une case du centre

        sim.Submit(Use(Speedster, "vortex", cell: new Cell(4, 4)));
        sim.Tick();

        Assert.Equal(new Cell(4, 4), sim.Get(8).Position);
        Assert.Equal(new Cell(5, 4), sim.Get(7).Position);
        Assert.Equal(new Cell(3, 3), sim.Get(9).Position);
        Assert.Equal((new Cell(4, 6), 100), (sim.Get(2).Position, sim.Get(2).Hp));
        Assert.Equal(new Cell(0, 4), sim.Get(1).Position);
        Assert.All(new[] { 7, 8, 9 }, id => Assert.Equal((90, true), (sim.Get(id).Hp, sim.Get(id).HasStatus(StatusKind.OffBalance))));
    }

    [Fact]
    public void Vortex_IsStoppedByTheScenery_WithoutCollisionDamage()
    {
        var sim = Simulation(Grid.FromRows(".......", ".......", ".....o.", "......."), SpeedsterAt(0, 2), Unit(9, team: 1, 6, 2));

        sim.Submit(Use(Speedster, "vortex", cell: new Cell(4, 2)));
        var events = sim.Tick();

        // À couvert derrière l'obstacle : la moitié des dégâts, et rien de plus.
        Assert.Equal((new Cell(6, 2), 95), (sim.Get(9).Position, sim.Get(9).Hp));
        Assert.DoesNotContain(events, e => e is UnitDisplaced);
    }

    [Fact]
    public void Vortex_DoesNotMoveAnUnshakableEnemy()
    {
        var sim = Simulation(new Grid(9, 9), SpeedsterAt(0, 4), Unit(9, team: 1, 6, 4, passive: PassiveKind.Unshakable));

        sim.Submit(Use(Speedster, "vortex", cell: new Cell(4, 4)));
        var events = sim.Tick();

        Assert.Equal(new Cell(6, 4), sim.Get(9).Position);
        Assert.Contains(events, e => e is UnitResisted { Unit.Value: 9 });
    }

    [Fact]
    public void APull_NeedsASquareZone_AndCausesNoCollision()
    {
        Vortex.Validate();
        Assert.Throws<ArgumentException>(() => (Vortex with { Zone = null }).Validate());
        Assert.Throws<ArgumentException>(() => (Vortex with
        {
            ForcedMove = Vortex.ForcedMove! with { CollisionMinDamage = 5, CollisionMaxDamage = 5 },
        }).Validate());
    }
}
