namespace Game.Core.Tests;

public class DeterministicRandomTests
{
    [Fact]
    public void MatchesSplitMix64ReferenceOutput()
    {
        // Valeurs de référence de SplitMix64 pour la graine 0.
        var random = new DeterministicRandom(0);

        Assert.Equal(0xE220A8397B1DCDAFUL, random.NextUInt64());
        Assert.Equal(0x6E789E6AA1B965F4UL, random.NextUInt64());
        Assert.Equal(0x06C45D188009454FUL, random.NextUInt64());
    }

    [Fact]
    public void SameSeed_GivesSameSequence()
    {
        var a = new DeterministicRandom(123);
        var b = new DeterministicRandom(123);

        for (var i = 0; i < 100; i++)
            Assert.Equal(a.NextInt(-50, 50), b.NextInt(-50, 50));
    }

    [Fact]
    public void NextInt_StaysWithinBounds_AndCoversThem()
    {
        var random = new DeterministicRandom(42);
        var seen = new HashSet<int>();

        for (var i = 0; i < 1000; i++)
        {
            var value = random.NextInt(3, 8);
            Assert.InRange(value, 3, 8);
            seen.Add(value);
        }

        Assert.Equal(6, seen.Count);
    }

    [Fact]
    public void NextInt_HandlesDegenerateAndExtremeRanges()
    {
        var random = new DeterministicRandom(1);

        Assert.Equal(7, random.NextInt(7, 7));
        Assert.InRange(random.NextInt(int.MinValue, int.MaxValue), int.MinValue, int.MaxValue);
        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(2, 1));
    }
}
