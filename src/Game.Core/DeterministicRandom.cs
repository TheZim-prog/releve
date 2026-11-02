namespace Game.Core;

/// <summary>
/// Générateur pseudo-aléatoire à graine (SplitMix64). Implémenté ici plutôt que <see cref="Random"/>,
/// dont l'algorithme n'est pas garanti stable d'une version de .NET à l'autre : une même graine
/// doit toujours donner la même suite, sur toutes les machines (serveur, clients, replays).
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(ulong seed) => _state = seed;

    public ulong NextUInt64()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Entier uniforme entre <paramref name="minInclusive"/> et <paramref name="maxInclusive"/>, bornes comprises.</summary>
    public int NextInt(int minInclusive, int maxInclusive)
    {
        if (minInclusive > maxInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxInclusive), "Max must be greater than or equal to min.");

        var range = (ulong)((long)maxInclusive - minInclusive) + 1;
        // Rejet des tirages au-delà du dernier multiple complet de range : pas de biais du modulo.
        var limit = ulong.MaxValue - ((ulong.MaxValue % range) + 1) % range;
        ulong value;
        do
        {
            value = NextUInt64();
        } while (value > limit);

        return (int)(minInclusive + (long)(value % range));
    }
}
