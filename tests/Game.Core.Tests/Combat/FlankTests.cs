using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Prise en tenaille : un coup qui frappe plus fort quand un allié est lui aussi au contact de la cible.</summary>
public class FlankTests
{
    private static readonly UnitId Hero = new(1);
    private static readonly UnitId Enemy = new(2);

    /// <summary>10 dégâts, 15 en tenaille.</summary>
    private static readonly SkillDefinition Pincer = Strike with { Id = "pincer", FlankDamagePercent = 150 };

    /// <summary>Le héros en (0,0) frappe l'ennemi en (1,0) ; renvoie les dégâts faits.</summary>
    private static int Damage(params UnitSetup[] others)
    {
        var units = new[] { Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { Pincer }), Unit(2, team: 1, 1, 0, hp: 100) };
        var sim = Simulation(new Grid(4, 2), units.Concat(others).ToArray());
        sim.Submit(Use(Hero, "pincer", Enemy));
        return sim.Tick().OfType<UnitDamaged>().Single().Amount;
    }

    [Fact]
    public void AFlankingStrike_HitsHarder_WhenAnAllyIsAlsoInContactWithTheTarget()
    {
        Assert.Equal(10, Damage());                         // seul
        Assert.Equal(15, Damage(Unit(3, team: 0, 2, 0)));   // un allié de l'autre côté de la cible
        Assert.Equal(15, Damage(Unit(3, team: 0, 2, 1)));   // en diagonale, c'est aussi au contact
    }

    [Fact]
    public void OnlyAStandingAllyInContact_Counts()
    {
        Assert.Equal(10, Damage(Unit(3, team: 0, 3, 1)));   // un allié trop loin
        Assert.Equal(10, Damage(Unit(3, team: 1, 2, 0)));   // un autre ennemi ne compte pas
    }

    [Fact]
    public void AGadget_DoesNotFlank()
    {
        var turret = new UnitDefinition
        {
            Id = "turret", Name = "Tourelle", MaxHp = 30, Initiative = 0, Mobility = 0, OnDefeat = DefeatOutcome.Removed,
            BasicAttack = null, OpportunityAttacksPerCycle = 0, Behavior = Behaviors.Basic, Skills = new[] { Strike },
        };
        var place = new SkillDefinition
        {
            Id = "place", Weight = SkillWeight.Light, Range = 3, MinDamage = 0, MaxDamage = 0, CooldownMs = 0, Summon = turret,
        };
        var sim = Simulation(new Grid(4, 2),
            Unit(1, team: 0, 0, 0, gauge: Capacity, initiative: Capacity, skills: new[] { Pincer, place }), Unit(2, team: 1, 1, 0, hp: 100));
        sim.Submit(Use(Hero, "place", cell: new Cell(2, 1))); // sa tourelle, au contact de la cible
        sim.Tick();

        sim.Submit(Use(Hero, "pincer", Enemy));

        Assert.Equal(10, sim.Tick().OfType<UnitDamaged>().Single().Amount);
    }

    [Fact]
    public void TheData_AreChecked()
    {
        Pincer.Validate();
        Assert.Throws<ArgumentException>(() => (Pincer with { FlankDamagePercent = 50 }).Validate());              // un bonus, pas un malus
        Assert.Throws<ArgumentException>(() => (Pincer with { Range = 3 }).Validate());                            // au contact
        Assert.Throws<ArgumentException>(() => (Pincer with { MinDamage = 0, MaxDamage = 0 }).Validate());         // un coup qui fait des dégâts
        Assert.Throws<ArgumentException>(() => (Pincer with { Range = 3, SelfMove = SelfMoveType.Pierce }).Validate()); // une seule cible
    }
}
