using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Réactions à une attaque : gardes, Réflexes, Garde du corps, Riposte, Coup d'arrêt.</summary>
public class ReactionTests
{
    private static readonly UnitId Hero = new(1);

    /// <summary>Tir à 5 cases, 10 dégâts.</summary>
    private static readonly SkillDefinition Shot = Strike with { Id = "shot", Range = 5 };

    private static SkillDefinition Guarding(GuardDefinition guard) => new()
    {
        Id = "guard", Target = SkillTargeting.Self, Weight = SkillWeight.Light, Range = 0, MinDamage = 0, MaxDamage = 0,
        CooldownMs = 0, Guard = guard,
    };

    private static readonly SkillDefinition Parry = Guarding(new GuardDefinition { Against = GuardAgainst.Melee });
    private static readonly SkillDefinition Deflection = Guarding(new GuardDefinition { Against = GuardAgainst.Ranged });

    /// <summary>Provoque les ennemis au contact, puis pare tout au contact pendant 1 s (20 ticks) et riposte.</summary>
    private static readonly SkillDefinition WeaponMaster = new()
    {
        Id = "master", Weight = SkillWeight.Heavy, Range = 0, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false, Affects = ZoneTargets.Enemies },
        TauntActions = 1,
        Guard = new GuardDefinition { Against = GuardAgainst.Melee, DurationMs = 1000, BlocksAll = true, Ripostes = true },
    };

    /// <summary>Un ennemi prêt à frapper (au contact ou à distance).</summary>
    private static UnitSetup Attacker(int id, int x, int y, PassiveKind? passive = null) =>
        Unit(id, team: 1, x, y, gauge: Capacity, skills: new[] { Strike, Shot }, basicAttack: "strike", passive: passive);

    private static ActivateCommand Hit(int attacker, string skill, int target) => Use(new UnitId(attacker), skill, new UnitId(target));

    private static CombatSimulation Guarded(SkillDefinition guard, params UnitSetup[] enemies)
    {
        var sim = Simulation(new Grid(8, 3), new[] { Unit(1, team: 0, 1, 1, gauge: Capacity, skills: new[] { guard, Strike }) }.Concat(enemies).ToArray());
        sim.Submit(Use(Hero, "guard"));
        sim.Tick();
        return sim;
    }

    [Fact]
    public void AParry_BlocksTheNextMeleeAttack_Only()
    {
        var sim = Guarded(Parry, Attacker(2, 0, 1), Attacker(3, 2, 1));

        sim.Submit(Hit(2, "strike", 1));
        sim.Submit(Hit(3, "strike", 1));
        var events = sim.Tick();

        Assert.Contains(new AttackParried(2, Hero, new UnitId(2)), events);
        Assert.Contains(new GuardEnded(2, Hero), events);
        Assert.Equal(90, sim.Get(1).Hp); // le second coup passe
    }

    [Fact]
    public void AParry_LetsShotsThrough_ADeflectionLetsMeleeThrough()
    {
        var parry = Guarded(Parry, Attacker(2, 5, 1));
        var deflection = Guarded(Deflection, Attacker(2, 0, 1), Attacker(3, 5, 1));

        parry.Submit(Hit(2, "shot", 1));
        parry.Tick();
        deflection.Submit(Hit(3, "shot", 1));
        deflection.Submit(Hit(2, "strike", 1));
        var deflected = deflection.Tick();

        Assert.Equal(90, parry.Get(1).Hp);
        Assert.Contains(new AttackParried(2, Hero, new UnitId(3)), deflected);
        Assert.Equal(90, deflection.Get(1).Hp);
    }

    [Fact]
    public void AGuard_FallsAtTheNextAction_AndAStunnedUnitCannotParry()
    {
        var acting = Guarded(Parry);
        acting.TickTimes(10);
        acting.Submit(new ActivateCommand(Hero, new Cell(2, 1)));
        Assert.Contains(new GuardEnded(12, Hero), acting.Tick());

        var stun = Shot with { Id = "stun", Effects = new[] { new StatusEffect { Status = StatusKind.Stunned, DurationMs = 1000 } } };
        var stunned = Guarded(Parry, Unit(2, team: 1, 5, 1, gauge: Capacity, skills: new[] { stun }), Attacker(3, 0, 1));
        stunned.Submit(Hit(2, "stun", 1));
        stunned.Submit(Hit(3, "strike", 1));
        stunned.Tick();

        Assert.Equal(80, stunned.Get(1).Hp);
    }

    [Fact]
    public void Reflexes_DodgeTheFirstTargetedAttackOfEachCycle_NotZones()
    {
        var nova = Strike with { Id = "nova", Range = 0, Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false } };
        var sim = Simulation(new Grid(8, 3),
            Unit(1, team: 0, 1, 1, gauge: Capacity, passive: PassiveKind.Reflexes),
            Attacker(2, 0, 1), Attacker(3, 2, 1), Unit(4, team: 1, 1, 0, gauge: Capacity, skills: new[] { nova }));

        sim.Submit(Hit(2, "strike", 1));
        sim.Submit(Hit(3, "strike", 1));
        sim.Submit(Use(new UnitId(4), "nova", cell: new Cell(1, 0)));
        var events = sim.Tick();

        Assert.Single(events.OfType<AttackDodged>());
        Assert.Equal(80, sim.Get(1).Hp); // le second coup et la zone

        // Il rejoue : ses réflexes reviennent.
        sim.Submit(new ActivateCommand(Hero, new Cell(1, 2)));
        sim.Tick();
        Assert.Equal(CycleReactions.None, sim.Get(1).SpentReactions);
    }

    [Fact]
    public void ABodyguard_TakesAnAttackAimedAtAnAdjacentAlly_OncePerCycle()
    {
        var sim = Simulation(new Grid(8, 3),
            Unit(1, team: 0, 1, 0, passive: PassiveKind.Bodyguard), Unit(2, team: 0, 0, 0),
            Attacker(3, 0, 1), Attacker(4, 1, 1));

        sim.Submit(Hit(3, "strike", 2));
        sim.Submit(Hit(4, "strike", 2));
        var events = sim.Tick();

        Assert.Contains(new AttackIntercepted(1, Hero, new UnitId(2), new UnitId(3)), events);
        Assert.Equal(90, sim.Get(1).Hp);
        Assert.Equal(90, sim.Get(2).Hp); // le second coup, une fois la garde utilisée
    }

    [Fact]
    public void Riposte_CounterAttacksAMeleeAttacker_OncePerCycle_AndIsNotRiposted()
    {
        var sim = Simulation(new Grid(8, 3),
            Unit(1, team: 0, 1, 1, basicAttack: "strike", passive: PassiveKind.Riposte),
            Attacker(2, 0, 1, passive: PassiveKind.Riposte), Attacker(3, 2, 1), Attacker(4, 5, 1));

        sim.Submit(Hit(2, "strike", 1));
        sim.Submit(Hit(3, "strike", 1));
        sim.Submit(Hit(4, "shot", 1));
        var events = sim.Tick();

        Assert.Equal(new[] { new CounterAttack(1, Hero, new UnitId(2)) }, events.OfType<CounterAttack>());
        Assert.Equal(90, sim.Get(2).Hp);
        Assert.Equal(100, sim.Get(3).Hp);
        Assert.Equal(70, sim.Get(1).Hp); // trois coups, aucune riposte de la riposte
    }

    [Fact]
    public void TheTargetsOwnDefense_ComesBeforeTheBodyguard()
    {
        var sim = Simulation(new Grid(8, 3),
            Unit(1, team: 0, 1, 0, passive: PassiveKind.Bodyguard), Unit(2, team: 0, 0, 0, passive: PassiveKind.Reflexes),
            Attacker(3, 0, 1));

        sim.Submit(Hit(3, "strike", 2));
        var events = sim.Tick();

        Assert.Contains(events, e => e is AttackDodged);
        Assert.DoesNotContain(events, e => e is AttackIntercepted);
        Assert.Equal(CycleReactions.None, sim.Get(1).SpentReactions);
    }

    [Fact]
    public void ABodyguard_DoesNotReachAroundAWallCorner()
    {
        // Le colosse est en diagonale de l'allié, mais des murs bouchent les deux coins.
        var sim = Simulation(Grid.FromRows("....", "..#.", ".#..", "...."),
            Unit(1, team: 0, 2, 2, passive: PassiveKind.Bodyguard), Unit(2, team: 0, 1, 1), Attacker(3, 0, 1));

        sim.Submit(Hit(3, "strike", 2));
        sim.Tick();

        Assert.Equal((100, 90), (sim.Get(1).Hp, sim.Get(2).Hp));
    }

    [Fact]
    public void AGuard_FallsWhenItsUnitIsDowned()
    {
        var sim = Guarded(Parry, Unit(2, team: 1, 5, 1, gauge: Capacity, skills: new[] { Shot with { MinDamage = 200, MaxDamage = 200 } }));

        sim.Submit(Hit(2, "shot", 1));
        sim.Tick();

        Assert.Null(sim.Get(1).Guard);
    }

    [Fact]
    public void AStoppingBlow_StopsTheMoverWhereItWasHit()
    {
        var sim = Simulation(new Grid(8, 1),
            Unit(1, team: 0, 0, 0, basicAttack: "strike", passive: PassiveKind.StoppingBlow),
            Unit(2, team: 1, 1, 0, gauge: Capacity, mobility: 4));

        sim.Submit(new ActivateCommand(new UnitId(2), new Cell(4, 0)));
        var events = sim.Tick();

        Assert.Contains(new UnitStopped(1, new UnitId(2), new Cell(1, 0)), events);
        Assert.Equal(new Cell(1, 0), sim.Get(2).Position);
    }

    [Fact]
    public void TheWeaponMaster_TauntsAdjacentEnemies_ThenParriesAndRipostesEveryMeleeAttack_ForAWhile()
    {
        // Les deux ennemis au contact sont des IA (la provocation ne joue que sur elles) : ils frappent dans le même tick.
        UnitSetup Brawler(int id, int x) =>
            Unit(id, team: 1, x, 1, gauge: Capacity, skills: new[] { Strike }, basicAttack: "strike", behavior: Behaviors.Basic);
        var sim = Simulation(new Grid(8, 3),
            Unit(1, team: 0, 1, 1, gauge: Capacity, basicAttack: "strike", skills: new[] { WeaponMaster, Strike }),
            Brawler(2, 0), Brawler(3, 2), Attacker(4, 5, 1));
        sim.Submit(Use(Hero, "master", cell: new Cell(1, 1)));
        var events = sim.Tick();
        Assert.Equal(new[] { 2, 3 }, events.OfType<UnitTaunted>().Select(t => t.Unit.Value));
        Assert.Equal(2, events.OfType<AttackParried>().Count());
        Assert.Equal(2, events.OfType<CounterAttack>().Count());

        sim.Submit(Hit(4, "shot", 1));
        sim.Tick();

        Assert.Equal((90, 90), (sim.Get(2).Hp, sim.Get(3).Hp));
        Assert.Equal(90, sim.Get(1).Hp); // le tir passe

        Assert.Contains(new GuardEnded(21, Hero), sim.TickTimes(20));
    }

    [Theory]
    // Une garde sur un ennemi, chargée, qui bloque tout sans durée, ou qui riposte aux tirs.
    [InlineData(""" "range": 1, "minDamage": 5, "maxDamage": 5, "guard": { "against": "melee" } """)]
    [InlineData(""" "target": "self", "range": 0, "minDamage": 0, "maxDamage": 0, "chargeMs": 500, "guard": { "against": "melee" } """)]
    [InlineData(""" "target": "self", "range": 0, "minDamage": 0, "maxDamage": 0, "guard": { "against": "melee", "blocksAll": true } """)]
    [InlineData(""" "target": "self", "range": 0, "minDamage": 0, "maxDamage": 0, "guard": { "against": "ranged", "durationMs": 500, "ripostes": true } """)]
    public void InvalidGuards_AreRejectedAtLoad(string fields)
    {
        Assert.Throws<ArgumentException>(() =>
            SkillDefinition.ListFromJson($$"""[ { "id": "x", "weight": "light", "cooldownMs": 0, {{fields}} } ]"""));
    }
}
