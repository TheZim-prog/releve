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
    [InlineData("scripts-allies")]
    [InlineData("scripts-lock")]
    [InlineData("scripts-collapse")]
    public void AScriptScenario_PlaysToTheEnd_AndReplays(string scenario)
    {
        var (sim, outcome) = PlayToTheEnd(new CombatLaunch(scenario, Array.Empty<HeroAssignment>()).BuildSetup(Content), scenario);

        output.WriteLine(outcome);
        foreach (var message in sim.EventLog.OfType<ScriptMessageShown>())
            output.WriteLine($"  {message.Tick / (double)Rules.TicksPerSecond:0.0} s  {message.Message.Speaker ?? "(jeu)"} : {message.Message.Text}");
        Assert.NotEmpty(sim.EventLog.OfType<ScriptMessageShown>());
        // Les héros de l'IA font les actions imposées eux-mêmes : aucune n'est abandonnée ; les ordres sont tous suivis.
        Assert.DoesNotContain(sim.EventLog, e => e is ImposedActionEnded { Done: false } or ScriptedActionFailed);
    }

    [Fact]
    public void UnderTheLock_TheSpeedsterPlaysTwice_BeforeTheColossus_AndNobodyElseActs()
    {
        var setup = new CombatLaunch("scripts-lock", Array.Empty<HeroAssignment>()).BuildSetup(Content);
        var speedster = setup.Units.Single(u => u.Tag == "speedster").Id;
        var colossus = setup.Units.Single(u => u.Tag == "colossus").Id;

        var (sim, _) = PlayToTheEnd(setup, "scripts-lock");

        var unlocked = sim.EventLog.OfType<ScriptLockChanged>().Single(e => !e.Locked).Tick;
        var lessons = sim.EventLog.OfType<ImposedActionEnded>().Where(e => e.Done).Select(e => e.Unit);
        Assert.Equal(new[] { speedster, speedster, colossus }, lessons);
        var actors = sim.EventLog.Where(e => e.Tick < unlocked)
            .Select(e => e switch { UnitMoved m => m.Unit, SkillUsed s => s.Unit, _ => (UnitId?)null })
            .OfType<UnitId>();
        Assert.All(actors, a => Assert.Contains(a, new[] { speedster, colossus }));
    }

    [Fact]
    public void TheDome_SavesTheCivilians_FromTheCollapse_WhileTheKingpinLeaves()
    {
        var setup = new CombatLaunch("scripts-collapse", Array.Empty<HeroAssignment>()).BuildSetup(Content);
        var kingpin = setup.Units.Single(u => u.Tag == "kingpin").Id;
        var civilians = setup.Units.Where(u => u.Definition.IsCivilian).Select(u => u.Id).ToHashSet();

        var (sim, _) = PlayToTheEnd(setup, "scripts-collapse");

        Assert.Equal(civilians, sim.EventLog.OfType<HitBlocked>().Select(e => e.Unit).ToHashSet());
        Assert.DoesNotContain(sim.EventLog, e => e is UnitDamaged d && civilians.Contains(d.Unit));
        Assert.Contains(new UnitEscaped(0, kingpin), sim.EventLog);
        // Le caïd parti, ses hommes restent : le combat continue jusqu'à eux.
        Assert.True(sim.Outcome);
        Assert.Contains(sim.EventLog, e => e is UnitRemoved r && setup.Units.Any(u => u.Id == r.Unit && u.Team == 1));
        Assert.DoesNotContain(sim.EventLog, e => e is UnitFleeing f && f.Unit != kingpin && !civilians.Contains(f.Unit));
    }

    [Fact]
    public void TheScriptedTelepath_IsNotPlayable_AndFallsToTheKingpinsShot()
    {
        var setup = new CombatLaunch("scripts-allies", Array.Empty<HeroAssignment>()).BuildSetup(Content);
        var telepath = setup.Units.Single(u => u.Tag == "telepath");

        Assert.DoesNotContain(CombatLaunch.PlayableHeroes(Content, "scripts-allies"), u => u.Id == telepath.Id);
        var (sim, _) = PlayToTheEnd(setup, "scripts-allies");
        var shot = sim.EventLog.OfType<SkillUsed>().Single(e => e.SkillId == "treacherous-shot");
        Assert.Equal(telepath.Id, shot.Target);
        Assert.Contains(sim.EventLog, e => e is UnitDowned d && d.Unit == telepath.Id);
        // L'IA du caïd ne s'en est jamais servie d'elle-même : un seul tir, celui du script.
        Assert.Single(sim.EventLog.OfType<SkillUsed>(), e => e.SkillId == "treacherous-shot");
    }
}
