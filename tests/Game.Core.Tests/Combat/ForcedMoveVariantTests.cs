using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>
/// Les déplacements forcés autres que la Projection au contact et la Télékinésie : renversement, poussée à distance,
/// poussée de zone, permutation, lancer d'un allié (avec impact), transport.
/// </summary>
public class ForcedMoveVariantTests
{
    private static readonly UnitId Caster = new(1);
    private static readonly UnitId Target = new(2);

    private static SkillDefinition Pushing(ForcedMoveType type, int range = 1) => Strike with
    {
        Id = "move", Range = range,
        ForcedMove = new ForcedMoveDefinition { Type = type, Distance = 3, CollisionMinDamage = 5, CollisionMaxDamage = 5 },
    };

    private static SkillDefinition OnAlly(string id, int range, ForcedMoveDefinition move) => new()
    {
        Id = id, Target = SkillTargeting.Ally, Weight = SkillWeight.Light, Range = range, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
        ForcedMove = move,
    };

    private static UnitSetup CasterAt(int x, int y, SkillDefinition skill) =>
        Unit(1, team: 0, x, y, gauge: Capacity, skills: new[] { skill });

    [Fact]
    public void Reversal_ThrowsTheTargetBehindTheCaster()
    {
        var sim = Simulation(new Grid(8, 1), CasterAt(3, 0, Pushing(ForcedMoveType.Throw)), Unit(2, team: 1, 4, 0));

        sim.Submit(Use(Caster, "move", Target));
        var events = sim.Tick();

        // Trois cases comptées depuis celle du lanceur.
        Assert.Contains(new UnitDisplaced(1, Target, new Cell(4, 0), new Cell(0, 0)), events);
        Assert.Equal(90, sim.Get(2).Hp);
    }

    [Fact]
    public void Reversal_WithNoRoomBehindTheCaster_LeavesTheTargetInPlace_AndItCollides()
    {
        var sim = Simulation(Grid.FromRows("#......."), CasterAt(1, 0, Pushing(ForcedMoveType.Throw)), Unit(2, team: 1, 2, 0));

        sim.Submit(Use(Caster, "move", Target));
        var events = sim.Tick();

        Assert.DoesNotContain(events, e => e is UnitDisplaced);
        Assert.Contains(new UnitCollided(1, Target, null), events);
        Assert.Equal(85, sim.Get(2).Hp); // 10 + 5 de collision
    }

    [Theory]
    [InlineData(3, 2, 6, 2)] // aligné : tout droit
    [InlineData(3, 3, 6, 3)] // presque aligné : tout droit aussi
    [InlineData(2, 4, 5, 7)] // en biais : en diagonale
    public void APushFromADistance_GoesAwayFromTheCaster_InTheNearestOfTheEightDirections(int x, int y, int endX, int endY)
    {
        var sim = Simulation(new Grid(8, 8), CasterAt(0, 2, Pushing(ForcedMoveType.Push, range: 5)), Unit(2, team: 1, x, y));

        sim.Submit(Use(Caster, "move", Target));
        sim.Tick();

        Assert.Equal(new Cell(endX, endY), sim.Get(2).Position);
    }

    [Fact]
    public void AZonePush_PushesEveryUnitHitAwayFromItsCenter_TheFarthestFirst()
    {
        var wave = Strike with
        {
            Id = "wave", Range = 0,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 2, ThroughWalls = false },
            ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Push, Distance = 1, CollisionMinDamage = 5, CollisionMaxDamage = 5 },
        };
        // 2 et 3 sont l'un derrière l'autre : 3 (le plus loin) part d'abord et laisse sa case à 2. 4 est dos au mur.
        var sim = Simulation(Grid.FromRows(".......", ".......", "#......", ".......", "......."),
            CasterAt(2, 2, wave), Unit(2, team: 1, 3, 2), Unit(3, team: 1, 4, 2), Unit(4, team: 1, 1, 2), Unit(5, team: 1, 1, 1));

        sim.Submit(Use(Caster, "wave", cell: new Cell(2, 2)));
        var events = sim.Tick();

        Assert.Equal(new Cell(4, 2), sim.Get(2).Position);
        Assert.Equal(new Cell(5, 2), sim.Get(3).Position);
        Assert.Equal(new Cell(0, 0), sim.Get(5).Position);
        Assert.Equal(new Cell(1, 2), sim.Get(4).Position);
        Assert.Equal(new[] { new UnitCollided(1, new UnitId(4), null) }, events.OfType<UnitCollided>());
        Assert.Equal(85, sim.Get(4).Hp);
        Assert.Equal(90, sim.Get(2).Hp);
    }

    [Fact]
    public void Swap_ExchangesTheCellsOfTheCasterAndItsAlly()
    {
        var swap = OnAlly("swap", 5, new ForcedMoveDefinition { Type = ForcedMoveType.Swap });
        var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, swap), Unit(2, team: 0, 3, 0), Unit(3, team: 1, 5, 0));

        Assert.Equal(RejectionReason.InvalidTarget, sim.Check(Use(Caster, "swap", new UnitId(3))));
        sim.Submit(Use(Caster, "swap", Target));
        sim.Tick();

        Assert.Equal(new Cell(3, 0), sim.Get(1).Position);
        Assert.Equal(new Cell(0, 0), sim.Get(2).Position);
        Assert.True(swap.MovesCaster); // donc impossible quand le lanceur est immobilisé
    }

    [Fact]
    public void AGadget_IsNotAnAllyToMove()
    {
        var gadget = new UnitDefinition
        {
            Id = "gadget", Name = "Gadget", MaxHp = 20, Initiative = 10, Mobility = 0, OnDefeat = DefeatOutcome.Removed,
            Behavior = Behaviors.Basic, BasicAttack = null, OpportunityAttacksPerCycle = 0, Skills = new[] { Strike },
        };
        var summon = new SkillDefinition
        {
            Id = "summon", Weight = SkillWeight.Light, Range = 1, MinDamage = 0, MaxDamage = 0, CooldownMs = 0, Summon = gadget,
        };
        var swap = OnAlly("swap", 5, new ForcedMoveDefinition { Type = ForcedMoveType.Swap });
        var sim = Simulation(new Grid(8, 1), Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { summon, swap }));
        sim.Submit(Use(Caster, "summon", cell: new Cell(1, 0)));
        var placed = sim.Tick().OfType<UnitSummoned>().Single().Unit;

        Assert.Equal(RejectionReason.InvalidTarget, sim.Check(Use(Caster, "swap", placed), ignoreTiming: true));
    }

    [Fact]
    public void AThrownAlly_KeepsItsGauge_AndHurtsTheEnemiesAroundItsLanding()
    {
        var cannonball = OnAlly("cannonball", 1,
            new ForcedMoveDefinition { Type = ForcedMoveType.Place, Distance = 3, ImpactMinDamage = 7, ImpactMaxDamage = 7 });
        var sim = Simulation(new Grid(8, 2),
            CasterAt(0, 0, cannonball), Unit(2, team: 0, 1, 0, gauge: 60), Unit(3, team: 1, 5, 0), Unit(4, team: 0, 3, 1), Unit(5, team: 1, 7, 0));

        sim.Submit(Use(Caster, "cannonball", Target, new Cell(4, 0)));
        var events = sim.Tick();

        Assert.Equal(new Cell(4, 0), sim.Get(2).Position);
        Assert.True(sim.Get(2).Gauge >= 60);
        Assert.Equal(new[] { new UnitId(3) }, events.OfType<UnitDamaged>().Select(e => e.Unit)); // ni l'allié voisin, ni l'ennemi plus loin
        Assert.Equal(93, sim.Get(3).Hp);
    }

    [Fact]
    public void Carry_TakesAStandingAllyAlong_AndDoesNotCountAsARescue()
    {
        var carry = new SkillDefinition
        {
            Id = "carry", Target = SkillTargeting.Ally, Weight = SkillWeight.Light, Range = 1, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
            RescueDropRadius = 4, CarriesStandingAlly = true,
        };
        var sim = Simulation(new Grid(8, 1),
            CasterAt(0, 0, carry), Unit(2, team: 0, 1, 0), Unit(3, team: 0, 7, 0), Unit(4, team: 1, 6, 0, basicAttack: "strike"));

        // Un allié hors de contact n'est pas emmené.
        Assert.Equal(RejectionReason.OutOfRange, sim.Check(Use(Caster, "carry", new UnitId(3), new Cell(4, 0))));
        sim.Submit(Use(Caster, "carry", Target, new Cell(4, 0)));
        var events = sim.Tick();

        Assert.Equal(new Cell(4, 0), sim.Get(2).Position);
        Assert.Equal(new Cell(3, 0), sim.Get(1).Position); // côte à côte
        Assert.DoesNotContain(events, e => e is UnitRescued or OpportunityAttack);
        Assert.False(sim.Get(2).WasRescued);
    }
}
