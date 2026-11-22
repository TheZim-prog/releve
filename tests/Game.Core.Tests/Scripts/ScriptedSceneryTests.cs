using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Scripts;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Scripts;

/// <summary>
/// Un décor qui s'effondre sur ordre du script : une structure dont la seule compétence, réservée au script, est une
/// zone chargée (affichée au sol, esquivable, bloquée par un dôme). Elle ne sert qu'une fois, puis disparaît.
/// </summary>
public class ScriptedSceneryTests
{
    private static readonly UnitId Colossus = new(1), Facade = new(3), Civil = new(5);

    /// <summary>Zone 3x3 chargée 1 s (20 ticks), 50 dégâts : de quoi tuer un civil.</summary>
    private static readonly SkillDefinition Collapse = new()
    {
        Id = "collapse", Weight = SkillWeight.Heavy, Range = 6, MinDamage = 50, MaxDamage = 50, CooldownMs = 0,
        ChargeMs = 1000, ScriptOnly = true,
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
    };

    private static readonly SkillDefinition Dome = new()
    {
        Id = "dome", Weight = SkillWeight.Light, Range = 3, MinDamage = 0, MaxDamage = 0, CooldownMs = 0, CreatesDome = true,
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
    };

    private static UnitDefinition FacadeDefinition(params SkillDefinition[] skills) => new()
    {
        Id = "facade", Name = "Façade", MaxHp = 50, Initiative = 0, Mobility = 0, OnDefeat = DefeatOutcome.Removed,
        OpportunityAttacksPerCycle = 0, Behavior = Behaviors.Inert, Skills = skills, BasicAttack = null,
        IsStructure = true, MaxActivations = 1,
    };

    private static readonly UnitDefinition CivilianDefinition = new()
    {
        Id = "civilian", Name = "Civil", MaxHp = 30, Initiative = 10, Mobility = 3, OnDefeat = DefeatOutcome.Removed,
        BasicAttack = null, OpportunityAttacksPerCycle = 0, Behavior = Behaviors.Flee, Skills = Array.Empty<SkillDefinition>(),
        IsCivilian = true,
    };

    private static ScriptTrigger Trigger(string id, ScriptCondition? when, params ScriptEffect[] effects) =>
        new() { Id = id, When = when, Do = effects };

    private static readonly ScriptEffect CollapseOnTheCivilian = new()
    {
        Act = new[] { new ScriptAction { Unit = "facade", Skill = "collapse", Cell = new Cell(4, 2) } },
    };

    /// <summary>
    /// Le colosse (1) en (2, 2), la façade (3) collée au mur du haut en (4, 0), un civil (5) en (4, 2), sous le verrou :
    /// le civil attend.
    /// </summary>
    private static CombatSetup Street(params ScriptTrigger[] script) =>
        Setup(new Grid(9, 5),
                Unit(1, team: 0, 2, 2, skills: new[] { Strike, Dome }) with { Tag = "colossus" },
                FacadeDefinition(Collapse).ToSetup(Facade, team: 2, new Cell(4, 0)) with { Tag = "facade" },
                CivilianDefinition.ToSetup(Civil, team: 2, new Cell(4, 2)))
            with { Script = new[] { Trigger("lock", null, new ScriptEffect { Lock = true }) }.Concat(script).ToArray() };

    [Fact]
    public void AFacade_CollapsesOnTheScriptsWord_AfterItsCharge()
    {
        var sim = new CombatSimulation(Street(Trigger("collapse", null, CollapseOnTheCivilian)));

        var started = sim.Tick();
        Assert.Contains(started, e => e is ChargeStarted { Unit.Value: 3, SkillId: "collapse", ReleaseTick: 20 });

        var events = sim.TickTimes(20);

        Assert.Contains(events, e => e is UnitDamaged { Unit.Value: 5 });
        Assert.Equal(UnitStatus.Removed, sim.FindUnit(Civil)!.Status);
        // Elle ne sert qu'une fois : elle disparaît.
        Assert.Contains(new UnitRemoved(20, Facade), events);
    }

    [Fact]
    public void TheColossusDome_ShieldsTheCivilians_FromTheCollapse()
    {
        var sim = new CombatSimulation(Street(Trigger("collapse", null,
            CollapseOnTheCivilian,
            new ScriptEffect
            {
                Say = "Colosse : ton dôme sur le civil !",
                Impose = new[] { new ScriptAction { Unit = "colossus", Skill = "dome", Cell = new Cell(4, 2) } },
            })));
        sim.Tick();

        sim.Submit(new ActivateCommand(Colossus, null, new SkillUse("dome", null, new Cell(4, 2))));
        var events = sim.TickTimes(21);

        Assert.Contains(events, e => e is HitBlocked { Unit.Value: 5 });
        Assert.Equal(30, sim.FindUnit(Civil)!.Hp);
    }

    [Fact]
    public void TheFacadesFall_CanTriggerTheNextStep()
    {
        var sim = new CombatSimulation(Street(
            Trigger("collapse", null, CollapseOnTheCivilian),
            Trigger("free", new ScriptCondition { UnitDown = "facade" }, new ScriptEffect { Lock = false })));

        sim.TickTimes(19);
        Assert.True(sim.IsLocked);
        sim.Tick(); // tick 20 : l'effondrement, puis le script

        Assert.False(sim.IsLocked);
    }

    [Fact]
    public void AStructure_NeverUsesItsSkill_OnItsOwn()
    {
        var sim = new CombatSimulation(Street());

        var events = sim.TickTimes(60);

        Assert.DoesNotContain(events, e => e is ChargeStarted or SkillUsed);
    }

    [Fact]
    public void AStructure_MayOnlyHaveSkillsReservedToTheScript()
    {
        var error = Assert.Throws<ArgumentException>(() => FacadeDefinition(Collapse with { ScriptOnly = false }).Validate());

        Assert.Contains("only skills reserved to the script", error.Message);
    }

    [Fact]
    public void AStructuresSkill_CannotMoveIt_NorSetAnythingUp()
    {
        Assert.Throws<ArgumentException>(() => FacadeDefinition(Collapse with { SelfMove = SelfMoveType.Leap }).Validate());
        Assert.Throws<ArgumentException>(() => FacadeDefinition(Collapse with { CreatesDome = true }).Validate());
    }

    [Fact]
    public void OrderingAStructureToMove_IsRefused()
    {
        var order = new ScriptEffect { Act = new[] { new ScriptAction { Unit = "facade", Skill = "collapse", Cell = new Cell(4, 2), MoveTo = new Cell(5, 0) } } };

        var error = Assert.Throws<ArgumentException>(() => new CombatSimulation(Street(Trigger("move", null, order))));

        Assert.Contains("never moves", error.Message);
    }

    [Fact]
    public void AStructure_CannotLeave()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            new CombatSimulation(Street(Trigger("go", null, new ScriptEffect { Leave = new[] { "facade" } }))));

        Assert.Contains("cannot run away", error.Message);
    }
}
