using System.Text.Json;
using Game.Core.Combat;

namespace Game.Core.Tests.Combat;

public class CombatRulesTests
{
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
        var rules = CombatRules.FromJson("""
            {
              // commentaire
              "ticksPerSecond": 20,
              "gaugeCapacity": 1000,
              "moveOnlyRestartPercent": 50,
            }
            """);

        Assert.Equal(20, rules.TicksPerSecond);
        Assert.Equal(1000, rules.GaugeCapacity);
        Assert.Equal(500, rules.MoveOnlyRestartGauge);
    }

    [Fact]
    public void FromJson_RejectsMissingOrUnknownFields()
    {
        Assert.Throws<JsonException>(() => CombatRules.FromJson("""{ "ticksPerSecond": 20, "gaugeCapacity": 1000 }"""));
        Assert.Throws<JsonException>(() => CombatRules.FromJson(
            """{ "ticksPerSecond": 20, "gaugeCapacity": 1000, "moveOnlyRestartPercent": 50, "typo": 1 }"""));
    }

    [Theory]
    [InlineData(0, 1000, 50)]
    [InlineData(20, 0, 50)]
    [InlineData(20, 1000, -1)]
    [InlineData(20, 1000, 101)]
    public void FromJson_RejectsOutOfRangeValues(int ticksPerSecond, int capacity, int restartPercent)
    {
        var json = $$"""{ "ticksPerSecond": {{ticksPerSecond}}, "gaugeCapacity": {{capacity}}, "moveOnlyRestartPercent": {{restartPercent}} }""";

        Assert.Throws<ArgumentException>(() => CombatRules.FromJson(json));
    }
}
