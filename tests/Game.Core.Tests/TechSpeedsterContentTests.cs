using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Progression;
using Game.Core.Scenarios;
using static Game.Core.Tests.ContentTestKit;

namespace Game.Core.Tests;

/// <summary>Les compétences et passifs du tech et du speedster ajoutés au contenu réel. Terrain vide de 9 × 5.</summary>
public class TechSpeedsterContentTests
{
    [Fact]
    public void CoordinatedStrike_TheColossusAndTheFighterNextToTheBrawler_HitIt()
    {
        var sim = Fight("tech", "coordinated-strike", (0, 2), ("brawler", 1, 6, 2), ("colossus", 0, 5, 2), ("fighter", 0, 7, 2));

        var events = Use(sim, "coordinated-strike", target: 2);

        Assert.Equal(2, events.OfType<UnitDamaged>().Count(d => d.Unit.Value == 2));
    }

    [Fact]
    public void Hacking_AndVirus_ReplaceTheirEffectsOnRobots()
    {
        Assert.True(Content.Skills["hacking"].VsRobots!.ReplacesEffects);
        Assert.Contains(Content.Skills["hacking"].VsRobots!.Effects, e => e.Status == StatusKind.Dominated);
        Assert.Contains(Content.Skills["virus"].VsRobots!.Effects, e => e.Status == StatusKind.Confused);
        Assert.Contains(Content.Skills["virus"].Effects, e => e.Status == StatusKind.OffBalance);
    }

    [Fact]
    public void FlyingStart_TheScenarioStartsTheSpeedsterWithAFullGauge()
    {
        // Le speedster d'essai, avec Départ canon au lieu de Réflexes.
        var sheet = Content.Heroes.Sheets["trial-speedster"];
        var fast = sheet with { Learned = sheet.Learned.Select(s => s == "reflexes" ? "flyingStart" : s).ToList(), Passive = "flyingStart" };
        var heroes = new HeroCatalog(Content.Heroes.Rules, Content.Units, Content.Skills, Content.Heroes.Trees.Values, new[] { fast });
        var scenario = new ScenarioDefinition
        {
            Id = "flying-start", Name = "Départ canon", Seed = 1, Map = new[] { "....." },
            Units = new[]
            {
                new ScenarioUnit { Hero = "trial-speedster", Team = 0, X = 0, Y = 0 },
                new ScenarioUnit { Unit = "brawler", Team = 1, X = 4, Y = 0 },
            },
        };

        var setup = scenario.ToSetup(Rules, Content.Units, heroes);

        Assert.Equal(new[] { Rules.GaugeCapacity, 0 }, setup.Units.Select(u => u.InitialGauge));
    }

    [Fact]
    public void TheNewSkillsAndPassives_AreCoded()
    {
        Assert.True(Content.Skills["express-revive"].RevivesAlly);
        Assert.True(Content.Skills["reload"].ResetsCooldowns);
        foreach (var passive in new[] { "flyingStart", "diversion", "tacticalAnalysis" })
            Assert.True(Passives.TryParse(passive, out _), passive);
        Assert.True(Rules.TacticalAnalysisRange > 0);
    }
}
