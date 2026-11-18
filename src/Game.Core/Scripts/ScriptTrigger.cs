namespace Game.Core.Scripts;

/// <summary>
/// Un déclencheur du script de mission : quand toutes ses conditions (<see cref="When"/>) sont réunies, ses effets
/// (<see cref="Do"/>) se jouent dans l'ordre, une seule fois. Il est <em>terminé</em> quand tous ses effets le sont (un
/// message figé l'est quand un joueur a cliqué « Continuer ») : c'est ce qu'attend un autre déclencheur qui le suit
/// (<see cref="ScriptCondition.After"/>).
/// </summary>
public sealed record ScriptTrigger
{
    /// <summary>Identifiant, unique dans le script, pour qu'un autre déclencheur puisse le suivre.</summary>
    public required string Id { get; init; }

    /// <summary>Conditions, toutes requises ; sans elles, le déclencheur part dès le premier tick.</summary>
    public ScriptCondition? When { get; init; }

    /// <summary>Effets, joués dans l'ordre (au moins un).</summary>
    public required IReadOnlyList<ScriptEffect> Do { get; init; }
}
