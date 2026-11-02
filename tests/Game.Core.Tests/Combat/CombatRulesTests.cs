using System.Text.Json;
using Game.Core.Combat;

namespace Game.Core.Tests.Combat;

public class CombatRulesTests
{
    private const string ValidJson = """
        {
          // commentaire
          "ticksPerSecond": 20,
          "gaugeCapacity": 1000,
          "moveOnlyRestartPercent": 50,
          "lightActionRestartPercent": 40,
          "heavyActionRestartPercent": 0,
          "offBalanceFillPercent": 50,
          "hastedFillPercent": 300,
          "empoweredDamagePercent": 150,
          "exposedDamageTakenPercent": 125,
          "weakPointExposedMs": 4000,
          "gripDurationPercent": 150,
          "intentionThresholdPercent": 50,
          "reviveHpPercent": 25,
          "reviveWeight": "light",
          "coverDamageReductionPercent": 50,
          "lowObstacleHp": 40,
        }
        """;

    [Fact]
    public void DataFile_Loads()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "combat-rules.json"));

        var rules = CombatRules.FromJson(json);

        Assert.True(rules.GaugeCapacity > 0);
    }

    [Fact]
    public void FromJson_ReadsValues_AndAcceptsComments()
    {
        var rules = CombatRules.FromJson(ValidJson);

        Assert.Equal(20, rules.TicksPerSecond);
        Assert.Equal(1000, rules.GaugeCapacity);
        Assert.Equal(500, rules.GaugeAt(rules.MoveOnlyRestartPercent));
        Assert.Equal(400, rules.GaugeAt(rules.LightActionRestartPercent));
        Assert.Equal(0, rules.GaugeAt(rules.HeavyActionRestartPercent));
    }

    [Fact]
    public void FromJson_RejectsMissingOrUnknownFields()
    {
        Assert.Throws<JsonException>(() => CombatRules.FromJson(ValidJson.Replace("\"heavyActionRestartPercent\": 0,", "")));
        Assert.Throws<JsonException>(() => CombatRules.FromJson(ValidJson.Replace("\"ticksPerSecond\"", "\"typo\": 1, \"ticksPerSecond\"")));
    }

    [Theory]
    [InlineData("\"ticksPerSecond\": 20", "\"ticksPerSecond\": 0")]
    [InlineData("\"gaugeCapacity\": 1000", "\"gaugeCapacity\": 0")]
    [InlineData("\"moveOnlyRestartPercent\": 50", "\"moveOnlyRestartPercent\": -1")]
    [InlineData("\"lightActionRestartPercent\": 40", "\"lightActionRestartPercent\": 100")]
    [InlineData("\"heavyActionRestartPercent\": 0", "\"heavyActionRestartPercent\": -5")]
    [InlineData("\"offBalanceFillPercent\": 50", "\"offBalanceFillPercent\": 101")]
    [InlineData("\"hastedFillPercent\": 300", "\"hastedFillPercent\": 90")]
    [InlineData("\"empoweredDamagePercent\": 150", "\"empoweredDamagePercent\": 1001")]
    [InlineData("\"exposedDamageTakenPercent\": 125", "\"exposedDamageTakenPercent\": 99")]
    [InlineData("\"weakPointExposedMs\": 4000", "\"weakPointExposedMs\": 0")]
    [InlineData("\"reviveHpPercent\": 25", "\"reviveHpPercent\": 0")]
    [InlineData("\"reviveHpPercent\": 25", "\"reviveHpPercent\": 101")]
    [InlineData("\"coverDamageReductionPercent\": 50", "\"coverDamageReductionPercent\": -1")]
    [InlineData("\"coverDamageReductionPercent\": 50", "\"coverDamageReductionPercent\": 101")]
    [InlineData("\"lowObstacleHp\": 40", "\"lowObstacleHp\": 0")]
    public void FromJson_RejectsOutOfRangeValues(string valid, string invalid)
    {
        Assert.Throws<ArgumentException>(() => CombatRules.FromJson(ValidJson.Replace(valid, invalid)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 1)]   // 1 tick = 50 ms à 20 ticks/s
    [InlineData(51, 2)]   // arrondi au tick supérieur
    [InlineData(5000, 100)]
    public void MillisecondsToTicks_RoundsUp(int milliseconds, int ticks)
    {
        Assert.Equal(ticks, CombatTestKit.Rules.MillisecondsToTicks(milliseconds));
    }
}
