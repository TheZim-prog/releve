using Game.Core.Combat;
using Game.Core.Network;
using static Game.Core.Tests.ContentTestKit;

namespace Game.Core.Tests;

/// <summary>
/// La première mission du prologue, jouée une fois jusqu'au bout par des héros confiés à l'IA (le combat est
/// déterministe : les tests se partagent la partie).
/// </summary>
public sealed class PrologueMissionOnePlay
{
    public const string Mission = "prologue-1";

    public PrologueMissionOnePlay()
    {
        Setup = new CombatLaunch(Mission, Array.Empty<HeroAssignment>()).BuildSetup(Content);
        (Sim, Outcome) = PlayToTheEnd(Setup, Mission);
    }

    public CombatSetup Setup { get; }
    public CombatSimulation Sim { get; }
    public string Outcome { get; }

    public UnitId Tagged(string tag) => Setup.Units.Single(u => u.Tag == tag).Id;

    /// <summary>Tick où le verrou est levé : la fin du passage scénarisé.</summary>
    public int Unlocked => Sim.EventLog.OfType<ScriptLockChanged>().Single(e => !e.Locked).Tick;
}

/// <summary>
/// La première mission du prologue (data/scenarios/prologue/mission-1.json) : chaque étape du tutoriel se déroule comme
/// prévu, puis le champ libre se gagne.
/// </summary>
public class PrologueContentTests(PrologueMissionOnePlay play) : IClassFixture<PrologueMissionOnePlay>
{
    private IReadOnlyList<CombatEvent> Log => play.Sim.EventLog;

    [Fact]
    public void TheMission_PlaysToVictory_WithEveryScriptedStepCarriedOut()
    {
        Assert.True(play.Sim.Outcome, play.Outcome);
        Assert.DoesNotContain(Log, e => e is ImposedActionEnded { Done: false } or ScriptedActionFailed);
        // Le verrou est mis au début et levé une seule fois, pour le champ libre.
        Assert.Equal(new[] { true, false }, Log.OfType<ScriptLockChanged>().Select(e => e.Locked));
    }

    [Fact]
    public void TheGaugeLesson_LetsTheSpeedsterPlayTwice_BeforeTheColossusIsReady()
    {
        var speedster = play.Tagged("speedster");
        var colossus = play.Tagged("colossus");

        var speedsterTurns = Log.OfType<ImposedActionEnded>().Where(e => e.Unit == speedster).Take(2).ToList();
        var colossusReady = Log.OfType<UnitReady>().First(e => e.Unit == colossus).Tick;

        Assert.Equal(2, speedsterTurns.Count);
        Assert.All(speedsterTurns, turn => Assert.True(turn.Tick < colossusReady, $"speedster at {turn.Tick}, colossus ready at {colossusReady}"));
    }

    [Fact]
    public void DuringTheScriptedPart_NoHeroActsTwiceAtOnce()
    {
        // Le tutoriel enseigne qu'on agit quand sa jauge est pleine : chaque étape attend celle du héros concerné.
        var turns = Log.OfType<ImposedActionEnded>().Where(e => e.Done && e.Tick < play.Unlocked).ToList();

        Assert.Equal(turns.Count, turns.Select(e => (e.Unit, e.Tick)).Distinct().Count());
    }

    [Fact]
    public void TheTelepathFalls_AndIsCarriedAway_WhileTheTechsBeaconTendsTheFighter()
    {
        var telepath = play.Tagged("telepath");
        var fighter = play.Tagged("fighter");
        var hurt = Log.OfType<SkillUsed>().First(e => e.SkillId == "power-punch" && e.Target == fighter).Tick;

        Assert.Contains(Log, e => e is UnitDowned d && d.Unit == telepath);
        Assert.Contains(Log, e => e is UnitRescued r && r.Unit == telepath);
        // Soigné pendant le passage scénarisé, après le coup du caïd : la leçon se voit avant le champ libre.
        Assert.Contains(Log, e => e is UnitHealed h && h.Unit == fighter && h.Tick >= hurt && h.Tick <= play.Unlocked);
    }

    [Fact]
    public void TheDome_SavesTheCivilians_AndTheKingpinGetsAway()
    {
        var civilians = play.Setup.Units.Where(u => u.Definition.IsCivilian).Select(u => u.Id).ToHashSet();

        Assert.Equal(civilians, Log.OfType<HitBlocked>().Select(e => e.Unit).Where(civilians.Contains).ToHashSet());
        Assert.DoesNotContain(Log, e => e is UnitDamaged d && civilians.Contains(d.Unit));
        Assert.Contains(Log, e => e is UnitEscaped x && x.Unit == play.Tagged("kingpin"));
    }
}
