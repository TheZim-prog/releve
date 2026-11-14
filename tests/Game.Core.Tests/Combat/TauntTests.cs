using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>
/// Provocation (docs/design.md 2.7, colosse) : l'ennemi provoqué ne vise plus que celui qui l'a provoqué, pendant ses
/// prochaines activations. Dans ces combats, l'ennemi (unité 2) a deux héros à sa portée : le colosse (unité 1,
/// 1000 PV) et un héros fragile (unité 3, 50 PV), que son IA préfère d'ordinaire.
/// </summary>
public class TauntTests
{
    private static readonly UnitId Colossus = new(1);
    private static readonly UnitId Enemy = new(2);
    private static readonly UnitId Fragile = new(3);

    private static SkillDefinition Taunt(int actions, int range = 7) =>
        Strike with { Id = "taunt", Range = range, MinDamage = 0, MaxDamage = 0, TauntActions = actions };

    /// <summary>Le colosse en (0,0), l'ennemi à l'IA de base en (enemyX,0), le héros fragile juste à côté de l'ennemi.</summary>
    private static CombatSimulation Combat(int actions = 1, int enemyX = 1, int enemyInitiative = 50, string behavior = Behaviors.Basic,
        IReadOnlyList<SkillDefinition>? enemySkills = null, bool boss = false) => Simulation(new Grid(9, 2),
        Unit(1, team: 0, 0, 0, hp: 1000, gauge: Capacity, initiative: Capacity, skills: new[] { Taunt(actions) }),
        Unit(2, team: 1, enemyX, 0, hp: 500, initiative: enemyInitiative, behavior: behavior, skills: enemySkills ?? new[] { Strike }, isBoss: boss),
        Unit(3, team: 0, enemyX, 1, hp: 50));

    /// <summary>Les cibles frappées par l'ennemi pendant les ticks qui suivent, dans l'ordre.</summary>
    private static List<UnitId?> TargetsOfTheEnemy(CombatSimulation sim, int ticks) =>
        sim.TickTimes(ticks).OfType<SkillUsed>().Where(e => e.Unit == Enemy).Select(e => e.Target).ToList();

    [Fact]
    public void WithoutTaunt_TheEnemyGoesForTheFragileHero()
    {
        Assert.All(TargetsOfTheEnemy(Combat(), 6), target => Assert.Equal(Fragile, target));
    }

    [Fact]
    public void ATauntedEnemy_StrikesTheTaunterOnce_ThenChoosesAgain()
    {
        var sim = Combat(actions: 1);
        sim.Submit(Use(Colossus, "taunt", Enemy));
        var events = sim.Tick();
        Assert.Contains(new UnitTaunted(1, Enemy, Colossus, 1), events);

        var targets = TargetsOfTheEnemy(sim, 8);

        Assert.Equal(Colossus, targets[0]);
        Assert.All(targets.Skip(1), target => Assert.Equal(Fragile, target));
        Assert.Contains(sim.EventLog, e => e is TauntEnded { Unit.Value: 2 });
        Assert.Equal(0, sim.Get(2).TauntActionsLeft);
    }

    [Fact]
    public void AChallenge_LastsTwoActivations()
    {
        var sim = Combat(actions: 2);
        sim.Submit(Use(Colossus, "taunt", Enemy));
        sim.Tick();

        var targets = TargetsOfTheEnemy(sim, 12);

        Assert.Equal(new UnitId?[] { Colossus, Colossus }, targets.Take(2));
        Assert.All(targets.Skip(2), target => Assert.Equal(Fragile, target));
    }

    [Fact]
    public void TheTaunt_DropsTheIntentionAlreadyChosen()
    {
        // À mi-jauge, l'ennemi a fixé son intention sur le héros fragile ; provoqué, il la change aussitôt.
        var sim = Combat(enemyInitiative: 10);
        sim.Get(2).Gauge = 60;
        sim.Tick();
        Assert.Equal(Fragile, sim.Get(2).Intention!.Target);

        sim.Submit(Use(Colossus, "taunt", Enemy));
        sim.Tick();

        Assert.Equal(Colossus, sim.Get(2).Intention!.Target);
    }

    [Fact]
    public void TooFarToStrike_TheTauntedEnemyComesCloser_AndThatUsesTheTaunt()
    {
        // L'ennemi est à 6 cases du colosse, avec le héros fragile à son contact : provoqué, il le laisse et avance.
        var sim = Combat(enemyX: 6);
        sim.Submit(Use(Colossus, "taunt", Enemy));
        sim.Tick();

        // Jusqu'à la fin de la provocation, c'est-à-dire sa prochaine activation.
        var events = new List<CombatEvent>();
        while (sim.Get(2).TauntActionsLeft > 0 && sim.CurrentTick < 20)
            events.AddRange(sim.Tick());

        Assert.Contains(events, e => e is UnitMoved { Unit.Value: 2 });
        Assert.DoesNotContain(events, e => e is SkillUsed { Unit.Value: 2 });
        Assert.Contains(events, e => e is TauntEnded { Unit.Value: 2 });
        Assert.True(sim.Get(2).Position.DistanceTo(new Cell(0, 0)) < 6);
    }

    [Fact]
    public void AChargeInProgress_IsNotInterrupted()
    {
        var charged = Strike with { Id = "charged", ChargeMs = 500 }; // 10 ticks
        var sim = Combat(enemySkills: new[] { charged });
        sim.TickTimes(3); // l'ennemi a commencé à charger sur le héros fragile
        Assert.NotNull(sim.Get(2).Charge);

        sim.Submit(Use(Colossus, "taunt", Enemy));
        var events = sim.TickTimes(20);

        Assert.DoesNotContain(events, e => e is ChargeInterrupted);
        Assert.Equal(Fragile, events.OfType<UnitDamaged>().First().Unit); // la charge part comme prévu, sur sa cible d'origine
        // Sa prochaine activation, elle, vise le colosse : c'est là que la provocation est dépensée.
        Assert.Equal(Colossus, events.OfType<ChargeStarted>().First().Target);
    }

    [Fact]
    public void ATauntedGunner_AimsAtTheTaunter_FromAfar()
    {
        var shot = Strike with { Id = "shot", Range = 5 };
        var sim = Combat(enemyX: 3, behavior: Behaviors.Gunner, enemySkills: new[] { shot });
        sim.Submit(Use(Colossus, "taunt", Enemy));
        sim.Tick();

        var events = sim.TickTimes(3);

        Assert.Contains(events, e => e is SkillUsed { Unit.Value: 2, Target.Value: 1 });
        Assert.True(sim.Get(2).Position.DistanceTo(new Cell(0, 0)) > 1); // il ne vient pas au contact
    }

    [Fact]
    public void ItWorksOnABoss_AndEndsWhenTheTaunterFalls()
    {
        var boss = Combat(boss: true);
        boss.Submit(Use(Colossus, "taunt", Enemy));
        boss.Tick();
        Assert.Equal(Colossus, TargetsOfTheEnemy(boss, 3).First());

        var sim = Combat(actions: 5);
        sim.Submit(Use(Colossus, "taunt", Enemy));
        sim.Tick();
        sim.Get(1).Status = UnitStatus.Downed; // le colosse est à terre
        Assert.All(TargetsOfTheEnemy(sim, 6), target => Assert.Equal(Fragile, target));
    }

    [Fact]
    public void WhenTheTaunterFalls_TheTauntEnds_ForGood()
    {
        var sim = Combat(actions: 5);
        sim.Submit(Use(Colossus, "taunt", Enemy));
        sim.Tick();
        sim.Get(1).Hp = 1; // le prochain coup de l'ennemi le met à terre
        sim.Get(1).Gauge = 0;

        var events = sim.TickTimes(3);

        Assert.Contains(events, e => e is UnitDowned or UnitRemoved);
        Assert.Contains(events, e => e is TauntEnded { Unit.Value: 2 });
        Assert.Equal((0, (UnitId?)null), (sim.Get(2).TauntActionsLeft, sim.Get(2).TauntedBy));
    }

    [Fact]
    public void ATauntedEnemy_ThatCanNeitherStrikeNorComeCloser_IsReleased()
    {
        // Une tourelle (immobile, portée 1) provoquée de loin : elle ne peut rien contre le colosse. Plutôt que de
        // rester figée, elle est libérée et frappe ce qui est à sa portée.
        var sim = Simulation(new Grid(9, 2),
            Unit(1, team: 0, 0, 0, hp: 1000, gauge: Capacity, skills: new[] { Taunt(1) }),
            Unit(2, team: 1, 6, 0, initiative: 50, mobility: 0, behavior: Behaviors.Basic, skills: new[] { Strike }),
            Unit(3, team: 0, 6, 1, hp: 50));
        sim.Submit(Use(Colossus, "taunt", Enemy));
        sim.Tick();

        var events = sim.TickTimes(3);

        Assert.Contains(events, e => e is TauntEnded { Unit.Value: 2 });
        Assert.Contains(events, e => e is SkillUsed { Unit.Value: 2, Target.Value: 3 });
    }

    [Fact]
    public void WhoDoesNotObey_IsNotTaunted()
    {
        // Un ennemi en fuite, une unité commandée par un joueur : la provocation ne leur fait rien.
        var coward = Unit(2, team: 1, 3, 1, hp: 100, initiative: 50, behavior: Behaviors.Basic);
        var fleeing = Simulation(new Grid(9, 3),
            Unit(1, team: 0, 2, 1, gauge: Capacity, initiative: Capacity,
                skills: new[] { Strike with { Id = "hit", MinDamage = 60, MaxDamage = 60 }, Taunt(1) }),
            coward with { Definition = coward.Definition with { FleeAtHpPercent = 50 } });
        fleeing.Submit(Use(Colossus, "hit", Enemy));
        fleeing.Tick();
        fleeing.Submit(Use(Colossus, "taunt", Enemy));
        Assert.DoesNotContain(fleeing.Tick(), e => e is UnitTaunted);

        var played = Simulation(new Grid(9, 1), Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { Taunt(1) }), Unit(2, team: 1, 1, 0));
        played.Submit(Use(Colossus, "taunt", Enemy));
        Assert.DoesNotContain(played.Tick(), e => e is UnitTaunted);
    }

    [Fact]
    public void ANewTaunt_DoesNotShortenTheOneInProgress()
    {
        var sim = Simulation(new Grid(9, 2),
            Unit(1, team: 0, 0, 0, hp: 1000, gauge: Capacity, initiative: Capacity,
                skills: new[] { Taunt(3), Taunt(1) with { Id = "short-taunt" } }),
            Unit(2, team: 1, 6, 0, initiative: 0, behavior: Behaviors.Basic));
        sim.Submit(Use(Colossus, "taunt", Enemy));
        sim.Tick();

        sim.Submit(Use(Colossus, "short-taunt", Enemy));
        sim.Tick();

        Assert.Equal(3, sim.Get(2).TauntActionsLeft);
    }

    [Fact]
    public void ATaunt_OnlyHitsEnemies()
    {
        Taunt(1).Validate();
        var onEveryone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false };
        Assert.Throws<ArgumentException>(() => (Taunt(1) with { Range = 0, Zone = onEveryone }).Validate());
        (Taunt(1) with { Range = 0, Zone = onEveryone with { Affects = ZoneTargets.Enemies } }).Validate();
        Assert.Throws<ArgumentException>(() => (Taunt(1) with { Target = SkillTargeting.Ally }).Validate());
        Assert.Throws<ArgumentException>(() => Taunt(11).Validate());
    }
}
