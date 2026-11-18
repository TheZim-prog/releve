using Game.Core.Combat;
using Game.Core.Network;
using Xunit.Abstractions;
using static Game.Core.Tests.ContentTestKit;

namespace Game.Core.Tests;

/// <summary>
/// Les scénarios d'essai des scripts (data/scenarios/scripts) se jouent jusqu'au bout, messages figés continués, et se
/// rejouent à l'identique, les héros confiés à l'IA de base.
/// </summary>
public class ScriptContentTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("scripts-messages")]
    [InlineData("scripts-imposed")]
    public void AScriptScenario_PlaysToTheEnd_AndReplays(string scenario)
    {
        var (sim, outcome) = PlayToTheEnd(new CombatLaunch(scenario, Array.Empty<HeroAssignment>()).BuildSetup(Content), scenario);

        output.WriteLine(outcome);
        foreach (var message in sim.EventLog.OfType<ScriptMessageShown>())
            output.WriteLine($"  {message.Tick / (double)Rules.TicksPerSecond:0.0} s  {message.Message.Speaker ?? "(jeu)"} : {message.Message.Text}");
        Assert.NotEmpty(sim.EventLog.OfType<ScriptMessageShown>());
        // Les héros de l'IA font les actions imposées eux-mêmes : aucune n'est abandonnée.
        Assert.DoesNotContain(sim.EventLog, e => e is ImposedActionEnded { Done: false });
    }
}
