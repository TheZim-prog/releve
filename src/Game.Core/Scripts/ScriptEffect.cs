namespace Game.Core.Scripts;

/// <summary>
/// Un effet de déclencheur, de l'un de ces trois types :
/// <list type="bullet">
/// <item>un message (<see cref="Say"/>, dit par <see cref="Speaker"/> s'il est donné) ; s'il <see cref="Freeze"/>, il
/// fige le combat (plus de temps, plus d'activation) jusqu'à ce qu'un joueur clique « Continuer » ;</item>
/// <item>des actions imposées (<see cref="Impose"/>, au plus une par héros, faites en même temps) : le combat est figé,
/// ces héros ont la jauge pleine et ne peuvent faire que l'action demandée ; le temps reprend quand toutes sont faites
/// (ou abandonnées, devenues impossibles). <see cref="Say"/> sert alors de consigne, affichée jusque-là ;</item>
/// <item>des actions ordonnées (<see cref="Act"/>, au plus une par unité) : chaque unité fait la sienne aussitôt, sans
/// attendre sa jauge ni la recharge de la compétence (pas si elle est hors de combat, étourdie ou en train de charger) ;
/// sa jauge repart ensuite comme après toute action. <see cref="Say"/> est alors une réplique, dite juste avant ;</item>
/// <item>le verrou (<see cref="Lock"/>), mis ou levé, seul dans son effet.</item>
/// </list>
/// Les effets suivants attendent qu'un effet qui fige le combat soit terminé.
/// </summary>
public sealed record ScriptEffect
{
    /// <summary>Texte du message, consigne des actions imposées, ou réplique des actions ordonnées.</summary>
    public string? Say { get; init; }

    /// <summary>Qui parle (nom affiché), ou <c>null</c> pour une indication du jeu.</summary>
    public string? Speaker { get; init; }

    /// <summary>Le message fige le combat jusqu'à « Continuer » (un message seul, pas avec des actions).</summary>
    public bool Freeze { get; init; }

    /// <summary>Actions imposées à des héros, ou <c>null</c>.</summary>
    public IReadOnlyList<ScriptAction>? Impose { get; init; }

    /// <summary>Actions ordonnées à des unités, faites aussitôt, ou <c>null</c>.</summary>
    public IReadOnlyList<ScriptAction>? Act { get; init; }

    /// <summary>
    /// Met (<c>true</c>) ou lève (<c>false</c>) le verrou du tutoriel, ou <c>null</c>. Verrouillé, le temps s'écoule mais
    /// les héros ne font que leurs actions imposées, et les unités du jeu (ennemis, civils, alliés) que les ordres du
    /// script ; les gadgets agissent comme d'habitude, et ce qui ne se décide pas (attaques chargées déjà lancées,
    /// attaques d'opportunité et autres réactions) suit son cours. Civils et fuyards, qui ne prennent pas d'ordres,
    /// restent sur place jusqu'à la levée.
    /// </summary>
    public bool? Lock { get; init; }
}
