using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.ContentTestKit;

namespace Game.Core.Tests;

/// <summary>Les chaînes du contenu réel (Frappe en chaîne, Ricochet, Chaîne d'éclairs). Terrain vide de 9 × 5, le lanceur est l'unité 1.</summary>
public class ChainContentTests
{
    [Fact]
    public void ChainStrike_HitsEveryEnemyOfAGroup_AndRicochetOneLess_ThenComesBack()
    {
        var group = new[] { ("brawler", 1, 3, 1), ("brawler", 1, 5, 1), ("brawler", 1, 7, 1), ("brawler", 1, 7, 3) };
        var chain = Fight("speedster", "chain-strike", (0, 1), group);
        var ricochet = Fight("speedster", "ricochet", (0, 1), group);

        var chained = Use(chain, "chain-strike", target: 2);
        var ricocheted = Use(ricochet, "ricochet", target: 2);

        Assert.Equal(new[] { 2, 3, 4, 5 }, Damaged(chained));
        Assert.Equal(3, Damaged(ricocheted).Length);
        Assert.Equal(new Cell(0, 1), PositionOf(ricochet, 1));
        Assert.DoesNotContain(chained, e => e is OpportunityAttack);
    }

    [Fact]
    public void ChainLightning_StunsEachEnemyItJumpsTo_WithLessDamageThanTheFireLance()
    {
        var sim = Fight("energetic", "chain-lightning", (0, 2), ("brawler", 1, 4, 2), ("brawler", 1, 6, 2), ("colossus", 0, 5, 3));

        var events = Use(sim, "chain-lightning", target: 2);

        Assert.Equal(new[] { 2, 3 }, Affected(events, StatusKind.Stunned)); // pas l'allié
        Assert.Equal(new Cell(0, 2), PositionOf(sim, 1));
        Assert.True(Content.Skills["chain-lightning"].MaxDamage < Content.Skills["fire-lance"].MinDamage);
    }
}
