namespace Game.Core.Combat;

/// <summary>
/// Comportement d'une unité IA. Il ne décide que quoi faire : ses commandes passent par la même validation
/// que celles des joueurs. Les règles communes (quand fixer l'intention, quand la changer) sont dans la simulation.
/// </summary>
internal interface IUnitBehavior
{
    /// <summary>Nouvelle intention, ou <c>null</c> s'il n'y a plus d'ennemi.</summary>
    Intention? ChooseIntention(CombatSimulation sim, Unit unit);

    /// <summary>Activation qui exécute l'intention (déplacement compris), ou <c>null</c> si elle est devenue impossible.</summary>
    ActivateCommand? Execute(CombatSimulation sim, Unit unit, Intention intention);

    /// <summary>Déplacement seul vers la cible de l'intention, ou <c>null</c> si rien ne rapproche.</summary>
    ActivateCommand? Approach(CombatSimulation sim, Unit unit, Intention intention);
}
