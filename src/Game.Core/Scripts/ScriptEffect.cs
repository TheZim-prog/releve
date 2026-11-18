namespace Game.Core.Scripts;

/// <summary>
/// Un effet de déclencheur. Pour l'instant, un seul type : un message (<see cref="Say"/>), dit par
/// <see cref="Speaker"/> s'il est donné. Un message <see cref="Freeze"/> fige le combat (plus de temps, plus
/// d'activation) jusqu'à ce qu'un joueur clique « Continuer » ; les effets suivants attendent jusque-là.
/// </summary>
public sealed record ScriptEffect
{
    /// <summary>Texte du message.</summary>
    public string? Say { get; init; }

    /// <summary>Qui parle (nom affiché), ou <c>null</c> pour une indication du jeu.</summary>
    public string? Speaker { get; init; }

    /// <summary>Le message fige le combat jusqu'à « Continuer ».</summary>
    public bool Freeze { get; init; }
}
