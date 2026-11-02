using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class CivilianTests
{
    private static readonly UnitId Hero = new(1);
    private static readonly UnitId Civil = new(5);

    private static UnitDefinition CivilianDefinition(int hp = 30) => new()
    {
        Id = "civilian", Name = "Civil", MaxHp = hp, Initiative = 10, Mobility = 3, OnDefeat = DefeatOutcome.Removed,
        BasicAttack = null, OpportunityAttacksPerCycle = 0, Behavior = Behaviors.Flee, Skills = Array.Empty<SkillDefinition>(),
        IsCivilian = true,
    };

    private static UnitSetup Civilian(int x, int y, int gauge = 0, int hp = 30) =>
        CivilianDefinition(hp).ToSetup(Civil, team: 2, new Cell(x, y), gauge);

    private static UnitSetup HeroAt(int x, int y, params SkillDefinition[] skills) =>
        Unit(1, team: 0, x, y, gauge: Capacity, skills: skills.Length == 0 ? null : skills);

    private static SkillDefinition Blast(ZoneTargets affects) => Strike with
    {
        Id = "blast", Range = 4,
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false, Affects = affects },
    };

    [Fact]
    public void ACivilian_RunsToTheEdge_AndIsEvacuated()
    {
        var sim = Simulation(new Grid(7, 7), Civilian(3, 3, gauge: Capacity));

        var events = sim.Tick();

        Assert.Contains(events, e => e is UnitMoved { Unit.Value: 5 });
        Assert.Contains(new UnitEscaped(1, Civil), events);
        Assert.True(sim.FindUnit(Civil)!.Escaped);
    }

    [Fact]
    public void NobodyTargetsACivilianDirectly()
    {
        var heal = Strike with { Id = "heal", Target = SkillTargeting.Ally, MinDamage = 0, MaxDamage = 0, MinHeal = 5, MaxHeal = 5 };
        var sim = Simulation(new Grid(6, 3), HeroAt(1, 1, Strike, heal), Civilian(2, 1, hp: 30));
        sim.FindUnit(Civil)!.Hp = 20;

        var strike = new ActivateCommand(Hero, null, new SkillUse("strike", Civil));
        var healIt = new ActivateCommand(Hero, null, new SkillUse("heal", Civil));
        sim.Submit(strike);
        sim.Submit(healIt);

        Assert.Equal(
            new[] { strike, healIt }.Select(c => new CommandRejected(1, c, RejectionReason.InvalidTarget)),
            sim.Tick().OfType<CommandRejected>());
    }

    [Theory]
    [InlineData(ZoneTargets.All, true)]
    [InlineData(ZoneTargets.Enemies, false)]
    public void OnlyZonesHittingEveryone_HurtCivilians(ZoneTargets affects, bool hurt)
    {
        var sim = Simulation(new Grid(8, 3), HeroAt(0, 1, Blast(affects)), Civilian(4, 1), Unit(2, team: 1, 4, 0));

        sim.Submit(new ActivateCommand(Hero, null, new SkillUse("blast", null, new Cell(4, 1))));
        var events = sim.Tick();

        Assert.Equal(hurt, events.Any(e => e is UnitDamaged { Unit.Value: 5 }));
    }

    [Fact]
    public void ACivilianAtZeroHp_IsLost()
    {
        var sim = Simulation(new Grid(8, 3), HeroAt(0, 1, Blast(ZoneTargets.All)), Civilian(4, 1, hp: 10));

        sim.Submit(new ActivateCommand(Hero, null, new SkillUse("blast", null, new Cell(4, 1))));

        Assert.Contains(new UnitRemoved(1, Civil), sim.Tick());
        Assert.Equal(UnitStatus.Removed, sim.FindUnit(Civil)!.Status);
    }

    [Fact]
    public void CiviliansCanBeWalkedThrough_AndDoNotBlockDiagonals()
    {
        // Couloir d'une case : le héros traverse le civil.
        var corridor = Simulation(new Grid(6, 1), HeroAt(0, 0), Civilian(2, 0));
        corridor.Submit(new ActivateCommand(Hero, new Cell(3, 0)));
        Assert.Contains(corridor.Tick(), e => e is UnitMoved { Unit.Value: 1 });

        // Diagonale qui frôle un civil : faite en un pas (un ennemi l'aurait bloquée, imposant un détour de deux pas).
        var diagonal = Simulation(new Grid(4, 2), HeroAt(0, 1), Civilian(1, 1));
        diagonal.Submit(new ActivateCommand(Hero, new Cell(1, 0)));
        Assert.Single(diagonal.Tick().OfType<UnitMoved>().Single(e => e.Unit == Hero).Path);
    }

    [Fact]
    public void CiviliansDoNotBlockShots()
    {
        var shot = Strike with { Id = "shot", Range = 5 };
        var sim = Simulation(new Grid(6, 1), HeroAt(0, 0, shot), Civilian(2, 0), Unit(2, team: 1, 4, 0));

        sim.Submit(new ActivateCommand(Hero, null, new SkillUse("shot", new UnitId(2))));

        Assert.Contains(sim.Tick(), e => e is UnitDamaged { Unit.Value: 2 });
    }

    [Fact]
    public void NoOpportunityAttacks_ForOrAgainstCivilians()
    {
        // Le civil fuit loin d'un ennemi à son contact.
        var sim = Simulation(new Grid(9, 3), Civilian(4, 1, gauge: Capacity), Unit(2, team: 1, 5, 1, basicAttack: "strike"));

        var events = sim.Tick();

        Assert.Contains(events, e => e is UnitMoved { Unit.Value: 5 });
        Assert.DoesNotContain(events, e => e is OpportunityAttack);
    }

    [Fact]
    public void TheDome_ProtectsCiviliansUnderIt()
    {
        var dome = new SkillDefinition
        {
            Id = "dome", Weight = SkillWeight.Light, Range = 0, MinDamage = 0, MaxDamage = 0, CooldownMs = 0, CreatesDome = true,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        };
        var bomb = Blast(ZoneTargets.All) with { Id = "bomb" };
        var sim = Simulation(new Grid(8, 3),
            HeroAt(2, 1, dome), Civilian(3, 1), Unit(2, team: 1, 6, 1, gauge: Capacity - 10, skills: new[] { bomb }));

        sim.Submit(new ActivateCommand(Hero, null, new SkillUse("dome", null, new Cell(2, 1))));
        sim.Tick();
        sim.Submit(new ActivateCommand(new UnitId(2), null, new SkillUse("bomb", null, new Cell(3, 1))));
        var events = sim.Tick();

        Assert.Contains(new HitBlocked(2, Civil, Hero), events);
        Assert.DoesNotContain(events, e => e is UnitDamaged { Unit.Value: 5 });
    }

    [Fact]
    public void FlashRescue_CanCatchAStandingCivilian()
    {
        var rescue = new SkillDefinition
        {
            Id = "flash-rescue", Target = SkillTargeting.Ally, Weight = SkillWeight.Light, Range = 6,
            MinDamage = 0, MaxDamage = 0, CooldownMs = 0, RescueDropRadius = 4,
        };
        var sim = Simulation(new Grid(8, 3), HeroAt(0, 1, rescue), Civilian(4, 1));

        sim.Submit(new ActivateCommand(Hero, null, new SkillUse("flash-rescue", Civil, new Cell(0, 0))));
        var events = sim.Tick();

        Assert.Contains(new UnitRescued(1, Civil, Hero), events);
        Assert.Contains(new UnitDisplaced(1, Civil, new Cell(4, 1), new Cell(0, 0)), events);
        Assert.False(sim.FindUnit(Civil)!.WasRescued); // la marque ne vaut que pour les chutes des héros
    }

    [Fact]
    public void TheAi_IgnoresCivilians()
    {
        // Le civil est collé à l'ennemi IA, le héros est loin : l'IA s'approche du héros.
        var sim = Simulation(new Grid(10, 3),
            Unit(1, team: 0, 0, 1), Civilian(6, 1), Unit(2, team: 1, 7, 1, gauge: Capacity, behavior: Behaviors.Basic));

        var events = sim.Tick();

        Assert.Contains(events, e => e is IntentionChosen { Unit.Value: 2, Intention.Target.Value: 1 });
        Assert.DoesNotContain(events, e => e is UnitDamaged { Unit.Value: 5 });
    }

    [Fact]
    public void ChargesAndPiercesPassThroughCivilians_WithoutHurtingThem()
    {
        var rush = Strike with { Id = "rush", Range = 5, SelfMove = SelfMoveType.Rush };
        var pierce = Strike with { Id = "pierce", Range = 5, SelfMove = SelfMoveType.Pierce };

        var charging = Simulation(new Grid(8, 1), HeroAt(0, 0, rush), Civilian(2, 0), Unit(2, team: 1, 4, 0));
        charging.Submit(new ActivateCommand(Hero, null, new SkillUse("rush", new UnitId(2))));
        var rushed = charging.Tick();
        Assert.Contains(rushed, e => e is UnitDamaged { Unit.Value: 2 });
        Assert.DoesNotContain(rushed, e => e is UnitDamaged { Unit.Value: 5 });

        var piercing = Simulation(new Grid(8, 1), HeroAt(0, 0, pierce), Civilian(2, 0), Unit(2, team: 1, 3, 0));
        piercing.Submit(new ActivateCommand(Hero, null, new SkillUse("pierce", null, new Cell(1, 0))));
        var pierced = piercing.Tick();
        Assert.Contains(pierced, e => e is UnitDamaged { Unit.Value: 2 });
        Assert.DoesNotContain(pierced, e => e is UnitDamaged { Unit.Value: 5 });
    }

    [Fact]
    public void InvalidCivilians_AreRejected()
    {
        var civilian = CivilianDefinition();
        civilian.Validate();
        var invalid = new[]
        {
            civilian with { Behavior = Behaviors.Basic },
            civilian with { Behavior = null },
            civilian with { Skills = new[] { Strike } },
            civilian with { OpportunityAttacksPerCycle = 1 },
            civilian with { OnDefeat = DefeatOutcome.Downed },
            civilian with { Mobility = 0 },
            civilian with { Initiative = 0 },
            civilian with { IsCivilian = false }, // « flee » est réservé aux civils
        };

        Assert.All(invalid, definition => Assert.Throws<ArgumentException>(definition.Validate));
    }

    [Fact]
    public void ARushBlockedJustBeforeItsTarget_StopsWithoutStriking()
    {
        // Deux civils devant l'ennemi : le colosse s'arrête avant eux, loin de l'ennemi, sans le frapper.
        var rush = Strike with { Id = "rush", Range = 5, SelfMove = SelfMoveType.Rush };
        var sim = Simulation(new Grid(8, 1), HeroAt(0, 0, rush), Civilian(2, 0), Civilian(3, 0) with { Id = new UnitId(6) },
            Unit(2, team: 1, 4, 0));

        sim.Submit(new ActivateCommand(Hero, null, new SkillUse("rush", new UnitId(2))));
        var events = sim.Tick();

        Assert.Equal(new Cell(1, 0), sim.Get(1).Position);
        Assert.DoesNotContain(events, e => e is UnitDamaged);
    }
}
