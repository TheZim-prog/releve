using Game.Core.World;

namespace Game.Core.Network;

/// <summary>
/// Le trajet en cours vers une quête (cf. <see cref="Travel"/>), tel que le QG le montre : la quête et son lieu, le lieu
/// où est le groupe et le chemin parcouru, les crochets faits (la gravité), le bonus gagné pour la quête, le joueur qui
/// valide les pas et les choix (<see cref="Validator"/>), les lieux où il peut aller, et l'événement qui attend un choix
/// (ce qu'a donné le dernier est dans <see cref="CampaignInfo.LastEvent"/>).
/// </summary>
public sealed record TripInfo
{
    public required string Quest { get; init; }
    public required string Site { get; init; }
    public required string Current { get; init; }
    public required IReadOnlyList<string> Path { get; init; }
    public int Gravity { get; init; }
    public QuestModifiers Modifiers { get; init; } = QuestModifiers.None;

    /// <summary>Le joueur qui décide (cf. <see cref="TravelTo"/>, <see cref="ResolveEvent"/>), ou <c>null</c> si personne n'est là.</summary>
    public int? Validator { get; init; }

    public IReadOnlyList<string> Moves { get; init; } = Array.Empty<string>();
    public EventInfo? Event { get; init; }
}

/// <summary>L'événement qui attend un choix, et ses options possibles, dans l'ordre (leur rang est celui de <see cref="ResolveEvent"/>).</summary>
public sealed record EventInfo(string Id, IReadOnlyList<OptionInfo> Options);

/// <summary>Une option possible ; <paramref name="Difficulty"/> : le libellé de son test (pas de pourcentage), ou <c>null</c> sans test.</summary>
public sealed record OptionInfo(string Label, Campaign.QuestDifficulty? Difficulty);
