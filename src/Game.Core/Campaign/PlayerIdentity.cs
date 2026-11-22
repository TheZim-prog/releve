namespace Game.Core.Campaign;

/// <summary>
/// L'identifiant d'un joueur : créé par son jeu au premier lancement et gardé sur son PC, il le reconnaît d'une session à
/// l'autre (son nom n'est qu'affiché). Lettres, chiffres et tirets, 64 caractères au plus.
/// </summary>
public static class PlayerIdentity
{
    public const int MaxLength = 64;

    /// <summary>Un nouvel identifiant, unique.</summary>
    public static string Create() => Guid.NewGuid().ToString("N");

    public static bool IsValid(string? uid) =>
        uid is { Length: > 0 and <= MaxLength } && uid.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    public static void Validate(string? uid)
    {
        if (!IsValid(uid))
            throw new ArgumentException($"Invalid player id '{uid}'.", nameof(uid));
    }
}
