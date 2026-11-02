using System.Text.Json;
using Game.Core.Combat;

namespace Game.Core.Tests.Combat;

public class SkillDefinitionTests
{
    [Fact]
    public void ListFromJson_ReadsSkills()
    {
        var skills = SkillDefinition.ListFromJson("""
            [
              { "id": "jab", "weight": "light", "range": 1, "minDamage": 4, "maxDamage": 6, "cooldownMs": 0 },
              { "id": "slam", "weight": "heavy", "range": 1, "minDamage": 20, "maxDamage": 30, "cooldownMs": 8000 }
            ]
            """);

        Assert.Equal(2, skills.Count);
        Assert.Equal(SkillWeight.Heavy, skills[1].Weight);
        Assert.Equal(8000, skills[1].CooldownMs);
    }

    [Fact]
    public void ListFromJson_ReadsStatusEffects()
    {
        var skill = SkillDefinition.ListFromJson("""
            [{ "id": "stun", "weight": "light", "range": 1, "minDamage": 0, "maxDamage": 0, "cooldownMs": 6000,
               "effects": [ { "status": "stunned", "durationMs": 2000 }, { "status": "offBalance", "durationMs": 4000 } ] }]
            """).Single();

        Assert.False(skill.DealsDamage);
        Assert.Equal(
            new[] { (StatusKind.Stunned, 2000), (StatusKind.OffBalance, 4000) },
            skill.Effects.Select(e => (e.Status, e.DurationMs)));
    }

    [Theory]
    [InlineData("""[{ "status": "stunned", "durationMs": 0 }]""")]
    [InlineData("""[{ "status": "stunned", "durationMs": 100 }, { "status": "stunned", "durationMs": 200 }]""")]
    [InlineData("""[ null ]""")]
    public void ListFromJson_RejectsInvalidEffects(string effects)
    {
        var json = $$"""[{ "id": "x", "weight": "light", "range": 1, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "effects": {{effects}} }]""";

        Assert.Throws<ArgumentException>(() => SkillDefinition.ListFromJson(json));
    }

    [Fact]
    public void ListFromJson_RejectsUnknownStatus()
    {
        Assert.Throws<JsonException>(() => SkillDefinition.ListFromJson("""
            [{ "id": "x", "weight": "light", "range": 1, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0,
               "effects": [ { "status": "frozen", "durationMs": 100 } ] }]
            """));
    }

    [Fact]
    public void ListFromJson_AcceptsRangedSkills()
    {
        var skills = SkillDefinition.ListFromJson("""
            [{ "id": "bolt", "weight": "light", "range": 6, "minDamage": 3, "maxDamage": 5, "cooldownMs": 0 }]
            """);

        Assert.Equal(6, skills.Single().Range);
    }

    [Theory]
    [InlineData("""[{ "id": "x", "weight": "medium", "range": 1, "minDamage": 1, "maxDamage": 1, "cooldownMs": 0 }]""")]
    [InlineData("""[{ "id": "x", "weight": 0, "range": 1, "minDamage": 1, "maxDamage": 1, "cooldownMs": 0 }]""")]
    [InlineData("""[{ "id": "x", "weight": "light", "range": 1, "minDamage": 1, "maxDamage": 1 }]""")]
    public void ListFromJson_RejectsMalformedSkills(string json)
    {
        Assert.Throws<JsonException>(() => SkillDefinition.ListFromJson(json));
    }

    [Theory]
    [InlineData("""[ null ]""")]
    [InlineData("""[{ "id": "nothing", "weight": "light", "range": 1, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0 }]""")]
    [InlineData("""[{ "id": "", "weight": "light", "range": 1, "minDamage": 1, "maxDamage": 1, "cooldownMs": 0 }]""")]
    [InlineData("""[{ "id": "x", "weight": "light", "range": 0, "minDamage": 1, "maxDamage": 1, "cooldownMs": 0 }]""")]
    [InlineData("""[{ "id": "x", "weight": "light", "range": -1, "minDamage": 1, "maxDamage": 1, "cooldownMs": 0 }]""")]
    [InlineData("""[{ "id": "x", "weight": "light", "range": 1, "minDamage": 5, "maxDamage": 4, "cooldownMs": 0 }]""")]
    [InlineData("""[{ "id": "x", "weight": "light", "range": 1, "minDamage": 1, "maxDamage": 1, "cooldownMs": -1 }]""")]
    [InlineData("""
        [{ "id": "x", "weight": "light", "range": 1, "minDamage": 1, "maxDamage": 1, "cooldownMs": 0 },
         { "id": "x", "weight": "heavy", "range": 1, "minDamage": 1, "maxDamage": 1, "cooldownMs": 0 }]
        """)]
    public void ListFromJson_RejectsInvalidValues(string json)
    {
        Assert.Throws<ArgumentException>(() => SkillDefinition.ListFromJson(json));
    }
}
