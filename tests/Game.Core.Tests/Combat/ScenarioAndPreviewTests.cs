using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class ScenarioAndPreviewTests
{
    private const string Scenario = """
        {
          "id": "test", "name": "Test", "seed": 7,
          "map": [ "...#", "..o." ],
          "units": [
            { "unit": "hero", "team": 0, "x": 0, "y": 0 },
            { "unit": "thug", "team": 1, "x": 3, "y": 1 }
          ]
        }
        """;

    private static readonly IReadOnlyDictionary<string, UnitDefinition> Catalog = new Dictionary<string, UnitDefinition>
    {
        ["hero"] = Unit(0, 0, 0, 0).Definition with { Id = "hero" },
        ["thug"] = Unit(0, 0, 0, 0, behavior: Behaviors.Basic).Definition with { Id = "thug" },
    };

    [Fact]
    public void AScenario_BuildsTheInitialState()
    {
        var setup = ScenarioDefinition.FromJson(Scenario).ToSetup(Rules, Catalog);

        Assert.Equal(7UL, setup.Seed);
        Assert.Equal(Terrain.Wall, setup.Grid.TerrainAt(new Cell(3, 0)));
        Assert.Equal(Terrain.LowObstacle, setup.Grid.TerrainAt(new Cell(2, 1)));
        Assert.Equal(new[] { (1, "hero", 0, new Cell(0, 0)), (2, "thug", 1, new Cell(3, 1)) },
            setup.Units.Select(u => (u.Id.Value, u.Definition.Id, u.Team, u.Position)));
    }

    [Theory]
    [InlineData("\"x\": 3, \"y\": 1", "\"x\": 3, \"y\": 0")] // sur un mur
    [InlineData("\"x\": 3, \"y\": 1", "\"x\": 0, \"y\": 0")] // deux unités sur la même case
    [InlineData("\"team\": 1", "\"team\": -1")]
    [InlineData("\"..o.\"", "\"..x.\"")] // caractère inconnu
    [InlineData("\"..o.\"", "\"..o\"")]  // rangée trop courte
    public void InvalidScenarios_AreRejected(string valid, string invalid)
    {
        Assert.Throws<ArgumentException>(() => ScenarioDefinition.FromJson(Scenario.Replace(valid, invalid)));
    }

    [Fact]
    public void AnUnknownUnitType_IsRejected()
    {
        var scenario = ScenarioDefinition.FromJson(Scenario.Replace("\"thug\"", "\"ghost\""));

        Assert.Throws<ArgumentException>(() => scenario.ToSetup(Rules, Catalog));
    }

    [Fact]
    public void Check_TellsWhetherACommandWouldPass_WithoutApplyingIt()
    {
        var sim = Simulation(new Grid(6, 3), Unit(1, team: 0, 0, 1, gauge: Capacity), Unit(2, team: 1, 4, 1),
            Unit(3, team: 1, 5, 2, behavior: Behaviors.Basic));

        Assert.Null(sim.Check(new ActivateCommand(new UnitId(1), new Cell(3, 1), new SkillUse("strike", new UnitId(2)))));
        Assert.Equal(RejectionReason.OutOfRange, sim.Check(new ActivateCommand(new UnitId(1), null, new SkillUse("strike", new UnitId(2)))));
        Assert.Equal(RejectionReason.NotControllable, sim.Check(new ActivateCommand(new UnitId(3), new Cell(5, 1))));
        Assert.Equal(new Cell(0, 1), sim.Get(1).Position);
        Assert.Empty(sim.EventLog);
    }

    [Fact]
    public void Check_CanIgnoreTiming_ForAnOrderGivenInAdvance()
    {
        var sim = Simulation(new Grid(6, 3), Unit(1, team: 0, 0, 1, gauge: 0), Unit(2, team: 1, 4, 1));
        var strike = new ActivateCommand(new UnitId(1), new Cell(3, 1), new SkillUse("strike", new UnitId(2)));

        Assert.Equal(RejectionReason.NotReady, sim.Check(strike));
        Assert.True(RejectionReason.NotReady.OnlyNeedsTime());
        Assert.Null(sim.Check(strike, ignoreTiming: true));
        // Le reste est toujours vérifié.
        Assert.Equal(RejectionReason.OutOfRange,
            sim.Check(new ActivateCommand(new UnitId(1), null, new SkillUse("strike", new UnitId(2))), ignoreTiming: true));
        Assert.False(RejectionReason.OutOfRange.OnlyNeedsTime());
    }

    [Fact]
    public void MovePreviews_MatchWhatAMoveWouldDo()
    {
        var sim = Simulation(new Grid(5, 1), Unit(1, team: 0, 0, 0, mobility: 2), Unit(2, team: 0, 1, 0));

        Assert.Equal(new[] { new Cell(0, 0), new Cell(2, 0) }, sim.ReachableCells(new UnitId(1)).Keys); // l'allié se traverse
        Assert.Equal(new[] { new Cell(1, 0), new Cell(2, 0) }, sim.PathFor(new UnitId(1), new Cell(2, 0)));
        Assert.Null(sim.PathFor(new UnitId(1), new Cell(3, 0))); // trop loin
        Assert.True(sim.IsPlayerControlled(new UnitId(1)));
    }

    [Fact]
    public void TeamsStillFighting_IgnoreCiviliansGadgetsAndTheFallen()
    {
        var civilian = Unit(5, team: 2, 4, 2) with
        {
            Definition = Unit(5, team: 2, 4, 2).Definition with
            {
                IsCivilian = true, Behavior = Behaviors.Flee, Skills = Array.Empty<SkillDefinition>(), OpportunityAttacksPerCycle = 0,
            },
        };
        var sim = Simulation(new Grid(6, 3), Unit(1, team: 0, 0, 0), Unit(2, team: 1, 3, 0), Unit(3, team: 1, 4, 0), civilian);
        sim.Get(3).SummonedBy = new UnitId(2);

        Assert.Equal(new[] { 0, 1 }, sim.TeamsStillFighting().Order());
        sim.Get(2).Status = UnitStatus.Removed;
        Assert.Equal(new[] { 0 }, sim.TeamsStillFighting()); // il ne reste qu'un gadget et un civil en face
    }

    [Fact]
    public void PreviewAction_ShowsTheZoneFromTheGhostCell()
    {
        var blast = Strike with
        {
            Id = "blast", Range = 2,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        };
        var sim = Simulation(new Grid(8, 3), Unit(1, team: 0, 0, 1, skills: new[] { blast }));

        var fromHere = sim.PreviewAction(new UnitId(1), new Cell(0, 1), new SkillUse("blast", null, new Cell(4, 1)));
        var fromGhost = sim.PreviewAction(new UnitId(1), new Cell(3, 1), new SkillUse("blast", null, new Cell(4, 1)));

        Assert.Equal(RejectionReason.OutOfRange, fromHere.Reason);
        Assert.True(fromGhost.IsValid);
        Assert.Equal(9, fromGhost.Cells!.Count);
        Assert.Equal(RejectionReason.UnknownSkill, sim.PreviewAction(new UnitId(1), new Cell(0, 1), new SkillUse("nope", null)).Reason);
    }
}
