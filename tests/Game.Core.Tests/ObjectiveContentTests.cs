using Game.Core.Network;
using Xunit.Abstractions;
using static Game.Core.Tests.ContentTestKit;

namespace Game.Core.Tests;

/// <summary>
/// Les scénarios d'essai des objectifs (data/scenarios/objectives) se jouent jusqu'au bout et se rejouent à l'identique,
/// les héros confiés à l'IA de base (qui ne sait ni neutraliser un objet ni protéger exprès : l'issue ne dit rien de
/// l'équilibre, elle est seulement affichée).
/// </summary>
public class ObjectiveContentTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("objectives-eliminate")]
    [InlineData("objectives-rescue")]
    [InlineData("objectives-pursuit")]
    [InlineData("objectives-countdown")]
    [InlineData("objectives-protect")]
    [InlineData("objectives-combined")]
    public void AnObjectiveScenario_PlaysToTheEnd_AndReplays(string scenario)
    {
        var (sim, outcome) = PlayToTheEnd(new CombatLaunch(scenario, Array.Empty<HeroAssignment>()).BuildSetup(Content), scenario);

        output.WriteLine(outcome);
        foreach (var objective in sim.Objectives)
            output.WriteLine($"  {objective.Definition.Kind}{(objective.Definition.Secondary ? " (secondaire)" : "")} : {objective.State}");
    }
}
