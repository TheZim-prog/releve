using Game.Core.Progression;
using Game.Core.Reputation;

namespace Game.Core.Campaign;

/// <summary>
/// L'état d'une campagne, ce que la sauvegarde garde d'une session à l'autre (docs/design.md sections 1, 2 et 4) : les
/// joueurs et leurs héros, le pot commun, la réputation du groupe, l'avancée de l'histoire. Immuable : chaque changement
/// donne un nouvel état (<c>with</c>), que le serveur sauvegarde.
/// </summary>
public sealed record CampaignState
{
    /// <summary>Version du format de sauvegarde ; une sauvegarde d'un autre format est refusée.</summary>
    public const int CurrentFormat = 2;

    public int Format { get; init; } = CurrentFormat;

    /// <summary>Les joueurs, dans l'ordre où ils sont arrivés.</summary>
    public IReadOnlyList<CampaignPlayer> Players { get; init; } = Array.Empty<CampaignPlayer>();

    /// <summary>Les héros des joueurs (un duo chacun, cf. 2.1), morts compris (pour le mémorial).</summary>
    public IReadOnlyList<CampaignHero> Heroes { get; init; } = Array.Empty<CampaignHero>();

    /// <summary>Le pot commun : quantité de chaque matériau (cf. 4.6), par identifiant.</summary>
    public IReadOnlyDictionary<string, int> Materials { get; init; } = new Dictionary<string, int>();

    /// <summary>La réputation du groupe, dans les bornes de l'échelle (cf. 3.4 et 4.4).</summary>
    public int Reputation { get; init; }

    /// <summary>Le chapitre de l'histoire en cours : 0 pour le prologue (cf. 6).</summary>
    public int Chapter { get; init; }

    /// <summary>Les points d'intérêt de la carte découverts en route : ils restent connus (cf. 4).</summary>
    public IReadOnlyList<string> KnownPois { get; init; } = Array.Empty<string>();

    /// <summary>Les points d'intérêt déjà visités : épuisés, ils ne donnent plus rien (mais on peut y passer).</summary>
    public IReadOnlyList<string> ExhaustedPois { get; init; } = Array.Empty<string>();

    /// <summary>Une nouvelle campagne : personne encore, la réputation de départ, le prologue.</summary>
    public static CampaignState New(ReputationScale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        return new CampaignState { Reputation = scale.Start };
    }

    public CampaignPlayer? PlayerOf(string uid) => Players.FirstOrDefault(p => p.Uid == uid);

    /// <summary>
    /// Le joueur de cet identifiant arrive : ajouté s'il est nouveau (au premier jour), renommé s'il a changé de nom.
    /// Renvoie le même état si rien ne change.
    /// </summary>
    public CampaignState WithPlayer(string uid, string name)
    {
        PlayerIdentity.Validate(uid);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (PlayerOf(uid) is not { } known)
            return this with { Players = Players.Append(new CampaignPlayer(uid, name, Hour: 0)).ToList() };
        return known.Name == name
            ? this
            : this with { Players = Players.Select(p => p.Uid == uid ? p with { Name = name } : p).ToList() };
    }

    /// <summary>Le bilan de réputation d'une mission s'ajoute à celle du groupe, dans les bornes de l'échelle.</summary>
    public CampaignState WithMissionReport(ReputationReport report, ReputationScale scale)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(scale);
        return this with { Reputation = scale.Clamp(Reputation + report.Total) };
    }
}

/// <summary>
/// Un joueur de la campagne : son identifiant (créé par son jeu, gardé sur son PC), son nom affiché, et sa date
/// personnelle en heures depuis le début de la campagne (cf. 4.7 : chaque joueur avance à son rythme).
/// </summary>
public sealed record CampaignPlayer(string Uid, string Name, int Hour);

/// <summary>
/// Un héros de joueur : sa fiche (archétype, niveau, points, compétences, cf. 2.6 et 2.7), son expérience, ses chutes
/// en demi-chutes (cf. 1.4 : une chute secourue par un coéquipier compte pour moitié ; trop de chutes, et il meurt
/// vraiment), les quêtes que le groupe a terminées sans lui depuis sa dernière récupération (le repos), et s'il est mort.
/// </summary>
public sealed record CampaignHero
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Identifiant du joueur à qui il appartient.</summary>
    public required string Owner { get; init; }

    public required HeroSheet Sheet { get; init; }

    public int Xp { get; init; }

    /// <summary>Ses chutes non récupérées, en demi-chutes (une chute = 2, une chute secourue = 1).</summary>
    public int HalfFalls { get; init; }

    /// <summary>Quêtes terminées par le groupe sans lui depuis sa dernière récupération de chute.</summary>
    public int QuestsRested { get; init; }

    public bool IsDead { get; init; }
}
