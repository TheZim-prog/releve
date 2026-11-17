using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Objectives;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Objectives;

public class ProtectionTests
{
    private static ObjectiveDefinition Protect(ProtectUntil until, bool secondary = false) => new()
    {
        Kind = ObjectiveKind.Protect, Until = until, Secondary = secondary,
        DurationMs = until == ProtectUntil.Time ? 1000 : null, // 20 ticks
    };

    /// <summary>Le témoin à protéger, un allié commandé par le jeu.</summary>
    private static UnitSetup Witness(int x, int y) => Unit(5, team: 0, x, y, behavior: Behaviors.Basic) with { IsProtected = true };

    /// <summary>Un héros en (0, 1), le témoin en (0, 2), un voyou au loin ; une vague d'un voyou à 100 ms.</summary>
    private static CombatSimulation Guard(params ObjectiveDefinition[] objectives) =>
        new(Setup(new Grid(8, 3), Unit(1, team: 0, 0, 1), Unit(2, team: 1, 7, 0), Witness(0, 2))
            with { Objectives = objectives, Waves = new[] { new WaveSetup(100, new[] { Unit(0, team: 1, 7, 2) }) } });

    [Fact]
    public void WhenAProtectedPersonFalls_TheProtectionFails()
    {
        var sim = Guard(Protect(ProtectUntil.Waves));
        sim.Get(5).Status = UnitStatus.Downed;

        sim.Tick();

        Assert.Equal(ObjectiveState.Failed, sim.Objectives[0].State);
        Assert.False(sim.Outcome);
    }

    [Fact]
    public void AProtectedPersonWhoEscapes_HasNotFallen()
    {
        var sim = Guard(Protect(ProtectUntil.Time));
        sim.Get(5).Escaped = true;
        sim.Get(5).Status = UnitStatus.Removed;

        sim.Tick();

        Assert.Equal(ObjectiveState.InProgress, sim.Objectives[0].State);
    }

    [Fact]
    public void UntilTime_HoldingOnSucceeds_WhateverTheEnemiesLeft()
    {
        var sim = Guard(Protect(ProtectUntil.Time));

        sim.TickTimes(19);
        Assert.Null(sim.Outcome);
        sim.Tick();

        Assert.True(sim.Outcome);
        Assert.Equal(UnitStatus.Active, sim.Get(2).Status);
    }

    [Fact]
    public void UntilWaves_EveryWaveMustArriveAndFall()
    {
        var sim = Guard(Protect(ProtectUntil.Waves));
        sim.Get(2).Status = UnitStatus.Removed;

        sim.Tick();
        Assert.Null(sim.Outcome); // la vague n'est pas encore arrivée

        sim.Tick();
        sim.Get(6).Status = UnitStatus.Removed; // le renfort (identifiant 6)
        sim.Tick();
        Assert.True(sim.Outcome);
    }

    [Fact]
    public void WhenEveryHeroIsDown_ThePersonStillStanding_DoesNotSaveTheCombat()
    {
        var sim = Guard(Protect(ProtectUntil.Time));
        sim.Get(1).Status = UnitStatus.Downed;

        sim.Tick();

        Assert.False(sim.Outcome);
        Assert.False(sim.Get(5).IsHero);
    }

    [Fact]
    public void AtVictory_ASecondaryProtectionWhosePeopleStand_Succeeds()
    {
        var sim = Guard(new ObjectiveDefinition { Kind = ObjectiveKind.Eliminate }, Protect(ProtectUntil.Time, secondary: true));
        sim.Get(2).Status = UnitStatus.Removed;
        sim.TickTimes(2);
        sim.Get(6).Status = UnitStatus.Removed;

        sim.Tick();

        Assert.True(sim.Outcome);
        Assert.Equal(ObjectiveState.Completed, sim.Objectives[1].State);
    }

    [Theory]
    [InlineData(ObjectiveKind.Protect, null, null)]               // jusqu'à quand ?
    [InlineData(ObjectiveKind.Protect, ProtectUntil.Time, null)]  // tenir combien de temps ?
    [InlineData(ObjectiveKind.Protect, ProtectUntil.Waves, 1000)] // une durée sans objet
    [InlineData(ObjectiveKind.Eliminate, ProtectUntil.Waves, null)]
    public void AProtection_SaysUntilWhen(ObjectiveKind kind, ProtectUntil? until, int? durationMs)
    {
        Assert.Throws<ArgumentException>(() => new ObjectiveDefinition { Kind = kind, Until = until, DurationMs = durationMs }.Validate());
    }

    public static TheoryData<string> InvalidProtections => new()
    {
        "nobody protected", "protected without protection", "protected hero", "protected enemy", "waves without enemies",
    };

    [Theory]
    [MemberData(nameof(InvalidProtections))]
    public void AProtectionThatCannotWork_IsRejected(string invalid)
    {
        var hero = Unit(1, team: 0, 0, 1);
        var enemy = Unit(2, team: 1, 7, 0);
        var witness = Witness(0, 2);
        var enemyWave = new[] { new WaveSetup(100, new[] { Unit(0, team: 1, 7, 2) }) };
        var (units, objectives, waves) = invalid switch
        {
            "nobody protected" => (new[] { hero, enemy }, new[] { Protect(ProtectUntil.Time) }, enemyWave),
            "protected without protection" => (new[] { hero, enemy, witness }, ObjectiveDefinition.Default.ToArray(), enemyWave),
            "protected hero" => (new[] { hero with { IsProtected = true }, enemy }, new[] { Protect(ProtectUntil.Time) }, enemyWave),
            "protected enemy" => (new[] { hero, enemy with { IsProtected = true } }, new[] { Protect(ProtectUntil.Time) }, enemyWave),
            _ => (new[] { hero, enemy, witness }, new[] { Protect(ProtectUntil.Waves) }, Array.Empty<WaveSetup>()),
        };

        Assert.Throws<ArgumentException>(() => new CombatSimulation(Setup(new Grid(8, 3), units)
            with { Objectives = objectives, Waves = waves }));
    }
}
