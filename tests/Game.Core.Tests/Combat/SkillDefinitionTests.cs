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
