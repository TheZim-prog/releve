using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.ContentTestKit;

namespace Game.Core.Tests;

/// <summary>
/// Les compétences de déplacement du contenu réel (Ruée, Aller-retour, Catapulte…) : leurs données font ce que le
/// document de conception décrit. Terrain vide de 9 × 5, le lanceur est l'unité 1.
/// </summary>
public class MoveContentTests
{
    [Theory]
    [InlineData("fighter", "rush")]
    [InlineData("speedster", "running-strike")]
    [InlineData("speedster", "trip")]
    [InlineData("colossus", "crush")]
    public void ItGoesNextToAnEnemyAFewCellsAway_AndHitsOnlyHim(string caster, string skill)
    {
        var sim = Fight(caster, skill, (0, 2), ("brawler", 1, 4, 2), ("brawler", 1, 4, 3), ("fighter", 0, 3, 3));

        var events = Use(sim, skill, target: 2);

        Assert.Equal(new Cell(3, 2), PositionOf(sim, 1));
        Assert.Equal(new[] { 2 }, Damaged(events));
        Assert.DoesNotContain(events, e => e is OpportunityAttack);
    }

    [Fact]
    public void RoundTrip_HitsAndComesBack()
    {
        var sim = Fight("speedster", "round-trip", (0, 2), ("brawler", 1, 4, 2));

        var events = Use(sim, "round-trip", target: 2);

        Assert.Equal(new[] { 2 }, Damaged(events));
        Assert.Equal(new Cell(0, 2), PositionOf(sim, 1));
    }

    [Fact]
    public void RunningStrike_HitsHarderAfterARun_ThanFromContact()
    {
        var skill = Content.Skills["running-strike"];
        var afterARun = Fight("speedster", "running-strike", (0, 2), ("brute", 1, 6, 2));
        var fromContact = Fight("speedster", "running-strike", (5, 2), ("brute", 1, 6, 2));

        var far = Use(afterARun, "running-strike", target: 2).OfType<UnitDamaged>().Single().Amount;
        var close = Use(fromContact, "running-strike", target: 2).OfType<UnitDamaged>().Single().Amount;

        Assert.InRange(close, skill.MinDamage, skill.MaxDamage);
        Assert.True(far > skill.MaxDamage);
    }

    [Fact]
    public void Clearance_PushesItsTargetTwoCells_AndTripImmobilizesIt()
    {
        var clearance = Fight("speedster", "clearance", (0, 2), ("brawler", 1, 4, 2));
        var trip = Fight("speedster", "trip", (0, 2), ("brawler", 1, 4, 2));

        Use(clearance, "clearance", target: 2);
        var tripped = Use(trip, "trip", target: 2);

        Assert.Equal(new Cell(6, 2), PositionOf(clearance, 2));
        Assert.Equal(new Cell(4, 2), PositionOf(trip, 2));
        Assert.Equal(new[] { 2 }, Affected(tripped, StatusKind.Immobilized));
    }

    [Fact]
    public void Retreat_HitsAnAdjacentEnemy_ThenStepsBack()
    {
        var sim = Fight("fighter", "retreat", (3, 2), ("brawler", 1, 4, 2));

        var events = Use(sim, "retreat", target: 2, cell: (0, 2));

        Assert.Equal(new[] { 2 }, Damaged(events));
        Assert.Equal(new Cell(0, 2), PositionOf(sim, 1));
        Assert.DoesNotContain(events, e => e is OpportunityAttack);
    }

    [Fact]
    public void Slalom_SparesTheAlliesItCrosses_UnlikeTheBreakthrough()
    {
        var slalom = Fight("speedster", "slalom", (0, 2), ("brawler", 1, 1, 2), ("fighter", 0, 2, 2));
        var breakthrough = Fight("speedster", "breakthrough", (0, 2), ("brawler", 1, 1, 2), ("fighter", 0, 2, 2));

        Assert.Equal(new[] { 2 }, Damaged(Use(slalom, "slalom", cell: (1, 2))));
        Assert.Equal(new[] { 2, 3 }, Damaged(Use(breakthrough, "breakthrough", cell: (1, 2))));
        Assert.True(Content.Skills["slalom"].MaxDamage < Content.Skills["breakthrough"].MaxDamage);
    }

    [Fact]
    public void Propulsion_ExplodesWhereItLeaves_AndCometWhereItLands()
    {
        var propulsion = Fight("energetic", "propulsion", (1, 2), ("brawler", 1, 0, 2), ("brawler", 1, 6, 2));
        var comet = Fight("energetic", "comet", (1, 2), ("brawler", 1, 0, 2), ("brawler", 1, 6, 2));

        Assert.Equal(new[] { 2 }, Damaged(Use(propulsion, "propulsion", cell: (5, 2))));
        Assert.Equal(new[] { 3 }, Damaged(Use(comet, "comet", cell: (5, 2))));
        Assert.Equal(new Cell(5, 2), PositionOf(propulsion, 1));
        Assert.Equal(new Cell(5, 2), PositionOf(comet, 1));
    }

    [Fact]
    public void RecoilWave_HitsLessThanTheNova_AndPushesBackWhatItHits()
    {
        var sim = Fight("energetic", "recoil-wave", (4, 2), ("brawler", 1, 5, 2), ("brawler", 1, 3, 1), ("brawler", 1, 7, 2));

        var events = Use(sim, "recoil-wave", cell: (4, 2));

        Assert.Equal(new[] { 2, 3 }, Damaged(events));
        Assert.Equal(new Cell(6, 2), PositionOf(sim, 2));
        Assert.Equal(new Cell(2, 0), PositionOf(sim, 3));
        Assert.True(Content.Skills["recoil-wave"].MaxDamage < Content.Skills["nova"].MaxDamage);
    }

    [Fact]
    public void Reversal_ThrowsItsTargetBehindTheColossus()
    {
        var sim = Fight("colossus", "reversal", (4, 2), ("brawler", 1, 5, 2));

        var events = Use(sim, "reversal", target: 2);

        Assert.Equal(new[] { 2 }, Damaged(events));
        Assert.Equal(4 - Content.Skills["reversal"].ForcedMove!.Distance, PositionOf(sim, 2).X);
    }

    [Fact]
    public void Catapult_ThrowsAnAdjacentAlly_AndCannonballHurtsTheEnemiesAroundTheLanding_FromLessFar()
    {
        var catapult = Fight("colossus", "catapult", (0, 2), ("fighter", 0, 1, 2), ("brawler", 1, 7, 2));
        var cannonball = Fight("colossus", "cannonball", (0, 2), ("fighter", 0, 1, 2), ("brawler", 1, 5, 2));

        // Un ennemi ne se lance pas.
        Assert.Equal(RejectionReason.InvalidTarget,
            catapult.Check(new ActivateCommand(new UnitId(1), null, new SkillUse("catapult", new UnitId(3), new Cell(3, 2)))));
        var thrown = Use(catapult, "catapult", target: 2, cell: (6, 2));
        var landed = Use(cannonball, "cannonball", target: 2, cell: (4, 2));

        Assert.Equal(new Cell(6, 2), PositionOf(catapult, 2));
        Assert.Empty(Damaged(thrown));
        Assert.Equal(new Cell(4, 2), PositionOf(cannonball, 2));
        Assert.Equal(new[] { 3 }, Damaged(landed));
        Assert.True(Content.Skills["cannonball"].ForcedMove!.Distance < Content.Skills["catapult"].ForcedMove!.Distance);
    }

    [Fact]
    public void Carry_TakesAnAdjacentAllyAlong_AndSwapExchangesPlacesFromAfar()
    {
        var carry = Fight("speedster", "carry", (0, 2), ("fighter", 0, 1, 2));
        var swap = Fight("speedster", "swap", (0, 2), ("fighter", 0, 4, 2));

        Use(carry, "carry", target: 2, cell: (4, 2));
        Use(swap, "swap", target: 2);

        Assert.Equal(new Cell(4, 2), PositionOf(carry, 2));
        Assert.True(PositionOf(carry, 1).IsAdjacentTo(new Cell(4, 2)));
        Assert.Equal((new Cell(4, 2), new Cell(0, 2)), (PositionOf(swap, 1), PositionOf(swap, 2)));
    }

    [Fact]
    public void Shove_PushesAnEnemyAwayFromAfar_AndOnlyACollisionHurts()
    {
        var free = Fight("telepath", "shove", (0, 2), ("brawler", 1, 3, 2));
        var blocked = Fight("telepath", "shove", (0, 2), ("brawler", 1, 3, 2), ("brawler", 1, 5, 2));

        var pushed = Use(free, "shove", target: 2);
        var collided = Use(blocked, "shove", target: 2);

        Assert.Equal(new Cell(6, 2), PositionOf(free, 2));
        Assert.Empty(Damaged(pushed));
        Assert.Equal(new Cell(4, 2), PositionOf(blocked, 2));
        Assert.Equal(new[] { 2, 3 }, Damaged(collided));
    }
}
