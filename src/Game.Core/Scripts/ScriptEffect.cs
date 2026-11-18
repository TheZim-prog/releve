namespace Game.Core.Scripts;

/// <summary>
/// Un effet de déclencheur, de l'un de ces deux types :
/// <list type="bullet">
/// <item>un message (<see cref="Say"/>, dit par <see cref="Speaker"/> s'il est donné) ; s'il <see cref="Freeze"/>, il
/// fige le combat (plus de temps, plus d'activation) jusqu'à ce qu'un joueur clique « Continuer » ;</item>
/// <item>des actions imposées (<see cref="Impose"/>, au plus une par héros, faites en même temps) : le combat est figé,
/// ces héros ont la jauge pleine et ne peuvent faire que l'action demandée ; le temps reprend quand toutes sont faites
/// (ou abandonnées, devenues impossibles). <see cref="Say"/> sert alors de consigne, affichée jusque-là.</item>
/// </list>
/// Les effets suivants attendent qu'un effet qui fige le combat soit terminé.
/// </summary>
public sealed record ScriptEffect
{
    /// <summary>Texte du message, ou consigne des actions imposées.</summary>
    public string? Say { get; init; }

    /// <summary>Qui parle (nom affiché), ou <c>null</c> pour une indication du jeu.</summary>
    public string? Speaker { get; init; }

    /// <summary>Le message fige le combat jusqu'à « Continuer ».</summary>
    public bool Freeze { get; init; }

    /// <summary>Actions imposées à des héros, ou <c>null</c>.</summary>
    public IReadOnlyList<ImposedAction>? Impose { get; init; }
}
