using System.Text.Json.Serialization;
using Game.Core.Combat;

namespace Game.Core.Network;

/// <summary>
/// Protocole entre clients et serveur. Les messages sont des records sérialisés en JSON (<see cref="NetJson"/>).
/// <para>
/// Déroulement : le client envoie <see cref="JoinRequest"/> ; au salon, chacun choisit un héros
/// (<see cref="PickHero"/>) puis quelqu'un lance (<see cref="StartCombatRequest"/>). En combat, le serveur diffuse
/// chaque tick joué avec les commandes appliquées (<see cref="TickPlayed"/>) : chaque client les rejoue sur sa propre
/// simulation, identique à celle du serveur.
/// </para>
/// </summary>
public static class Protocol
{
    /// <summary>
    /// Version de la poignée de main (<see cref="JoinRequest"/>, <see cref="JoinRefused"/>) : à changer si ces deux
    /// messages changent de forme, pour qu'un ancien jeu comprenne encore qu'il est refusé. Tout le reste (messages,
    /// simulation) est couvert par <see cref="CodeVersion"/>, sans rien à tenir à jour à la main.
    /// </summary>
    public const int Version = 3;

    /// <summary>
    /// Empreinte du code de Game.Core, calculée à la compilation (voir <c>Game.Core.csproj</c>) : deux jeux de même
    /// version de code et de même contenu jouent les mêmes combats. Change à la moindre modification des sources.
    /// </summary>
    public static string CodeVersion { get; } = ReadCodeVersion();

    private static string ReadCodeVersion()
    {
        using var stream = typeof(Protocol).Assembly.GetManifestResourceStream("CodeVersion")
            ?? throw new InvalidOperationException("Game.Core was built without its CodeVersion resource (see Game.Core.csproj).");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }

    public const int DefaultPort = 7777;

    public const int MaxNameLength = 20;

    /// <summary>Commandes d'un joueur en attente du prochain tick, au plus (seule la dernière par unité compte vraiment).</summary>
    public const int MaxQueuedCommandsPerPlayer = 16;
}

// --- Client → serveur ---

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(JoinRequest), "join")]
[JsonDerivedType(typeof(PickHero), "pickHero")]
[JsonDerivedType(typeof(StartCombatRequest), "start")]
[JsonDerivedType(typeof(SendCommand), "command")]
[JsonDerivedType(typeof(SetPause), "pause")]
[JsonDerivedType(typeof(RecruitHero), "recruit")]
[JsonDerivedType(typeof(ChooseQuest), "chooseQuest")]
[JsonDerivedType(typeof(DeployHero), "deployHero")]
public abstract record ClientMessage;

/// <summary>
/// Rejoindre la partie. <paramref name="PlayerUid"/> : l'identifiant du joueur, créé par son jeu et gardé sur son PC
/// (cf. <see cref="Campaign.PlayerIdentity"/>) ; il le reconnaît d'une session à l'autre (la campagne lui rend ses
/// héros) et le reprend en plein combat s'il était déconnecté (reconnexion). Le nom n'est qu'affiché. Le serveur
/// n'accepte que son propre protocole, son propre code (<see cref="Protocol.CodeVersion"/>) et son propre contenu
/// (<see cref="GameContent.Version"/>).
/// </summary>
public sealed record JoinRequest(string Name, string PlayerUid, int ProtocolVersion, string CodeVersion, string ContentVersion)
    : ClientMessage;

/// <summary>Au salon : choisir un héros, ou le laisser (<c>null</c>).</summary>
public sealed record PickHero(UnitId? Unit) : ClientMessage;

/// <summary>Au salon : lancer le combat (chaque joueur doit avoir choisi un héros).</summary>
public sealed record StartCombatRequest : ClientMessage;

/// <summary>En combat : une commande pour son héros (ou une unité qu'il domine).</summary>
public sealed record SendCommand(CombatCommand Command) : ClientMessage;

/// <summary>En combat : mettre en pause ou relancer (n'importe quel joueur, pour tout le monde).</summary>
public sealed record SetPause(bool Paused) : ClientMessage;

/// <summary>Au salon, avec une campagne : recruter un héros à l'académie (cf. <see cref="Campaign.Academy"/>).</summary>
public sealed record RecruitHero(Campaign.Recruit Recruit) : ClientMessage;

/// <summary>Au QG (salon d'une campagne) : choisir la quête que le groupe va jouer (n'importe quel joueur), ou aucune.</summary>
public sealed record ChooseQuest(string? QuestId) : ClientMessage;

/// <summary>Au QG : partir en quête avec l'un de ses héros vivants, ou rester au QG (<c>null</c>).</summary>
public sealed record DeployHero(string? HeroId) : ClientMessage;

// --- Serveur → client ---

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Welcome), "welcome")]
[JsonDerivedType(typeof(JoinRefused), "joinRefused")]
[JsonDerivedType(typeof(LobbyState), "lobby")]
[JsonDerivedType(typeof(CombatState), "combat")]
[JsonDerivedType(typeof(TickPlayed), "tick")]
[JsonDerivedType(typeof(PauseChanged), "pauseChanged")]
[JsonDerivedType(typeof(CombatOver), "combatOver")]
[JsonDerivedType(typeof(CombatAborted), "combatAborted")]
[JsonDerivedType(typeof(RequestRefused), "refused")]
[JsonDerivedType(typeof(RecruitRefused), "recruitRefused")]
public abstract record ServerMessage;

/// <summary>Le joueur est accepté sous l'identifiant <paramref name="PlayerId"/>.</summary>
public sealed record Welcome(int PlayerId) : ServerMessage;

public enum JoinRefusal
{
    ProtocolMismatch,

    /// <summary>Le code du jeu diffère de celui du serveur (autre version du jeu) : les simulations divergeraient.</summary>
    CodeMismatch,

    /// <summary>Le contenu (<c>data/</c>) diffère de celui du serveur : les simulations divergeraient.</summary>
    ContentMismatch,

    InvalidName,

    /// <summary>Un autre joueur de la partie (connecté, ou absent du combat en cours) porte déjà ce nom.</summary>
    NameTaken,

    /// <summary>Un combat est en cours et ce joueur n'y joue pas.</summary>
    CombatInProgress,

    /// <summary>L'identifiant du joueur est absent ou malformé.</summary>
    InvalidIdentity,

    /// <summary>Ce joueur est déjà connecté (une ancienne connexion que le serveur n'a pas encore vue partir).</summary>
    AlreadyConnected,

    /// <summary>La campagne du serveur n'a pas pu être sauvegardée (disque plein, fichier protégé) : voir son journal.</summary>
    CampaignUnavailable,
}

public sealed record JoinRefused(JoinRefusal Reason) : ServerMessage;

/// <summary>
/// Un joueur, son héros éventuel (l'unité qu'il joue), s'il est connecté (en combat, un déconnecté garde sa place), et,
/// au QG, le héros de campagne qu'il déploie (<paramref name="Deployed"/>, ou <c>null</c> : il reste au QG).
/// </summary>
public sealed record PlayerInfo(int Id, string Name, bool Connected, UnitId? Hero, string? Deployed = null);

/// <summary>
/// Les joueurs, envoyé à chaque changement (arrivée, départ, choix de héros, fin de combat), au salon comme en combat ;
/// <paramref name="Campaign"/> : la campagne du serveur, ou <c>null</c> s'il n'en tient pas.
/// </summary>
public sealed record LobbyState(string ScenarioId, IReadOnlyList<PlayerInfo> Players, CampaignInfo? Campaign = null) : ServerMessage;

/// <summary>
/// Ce que le QG montre de la campagne : la réputation du groupe et son palier, le résultat de la dernière quête
/// (<c>null</c> avant la première depuis le lancement du serveur), les héros des joueurs (vus par le destinataire :
/// <see cref="HeroInfo.Mine"/> marque les siens), le panneau de quêtes et celle qui est choisie, et le pot commun.
/// </summary>
public sealed record CampaignInfo
{
    public required int Reputation { get; init; }
    public required string Tier { get; init; }
    public QuestResult? LastQuest { get; init; }
    public IReadOnlyList<HeroInfo> Heroes { get; init; } = Array.Empty<HeroInfo>();
    public IReadOnlyList<QuestInfo> Board { get; init; } = Array.Empty<QuestInfo>();
    public string? ChosenQuest { get; init; }
    public IReadOnlyDictionary<string, int> Materials { get; init; } = new Dictionary<string, int>();
}

/// <summary>
/// Ce qu'a rapporté une quête : son nom, son issue, son bilan de réputation, l'XP reçue par chacun de ses héros, et les
/// matériaux versés au pot commun.
/// </summary>
public sealed record QuestResult(string Quest, bool Victory, int Reputation, int Xp, IReadOnlyDictionary<string, int> Materials);

/// <summary>Une quête du panneau, telle que le QG la montre ; <paramref name="Slots"/> : combien de héros peuvent y partir.</summary>
public sealed record QuestInfo(string Id, string Name, string Description, Campaign.QuestDifficulty Difficulty, int Xp,
    IReadOnlyDictionary<string, int> Materials, int Slots);

/// <summary>
/// Un héros de la campagne, tel que le QG le montre ; <paramref name="Owner"/> : le nom de son joueur. Son XP, celle
/// du niveau suivant (<c>null</c> au maximum), et les points gagnés en montant de niveau, à dépenser à l'académie.
/// </summary>
public sealed record HeroInfo(string Id, string Name, string Archetype, int Level, string Owner, bool Mine, bool IsDead,
    int Xp = 0, int? NextLevelXp = null, int StatPointsToSpend = 0, int SkillPointsToSpend = 0);

/// <summary>
/// Le combat : son lancement, les commandes déjà jouées et le pas de simulation atteint (vide et 0 au début ; à rejouer
/// à la reconnexion), et la pause. <paramref name="PausedBy"/> : joueur qui l'a mise, ou <c>null</c> (déconnexion).
/// </summary>
public sealed record CombatState(CombatLaunch Launch, IReadOnlyList<LoggedCommand> Log, int Step, bool Paused, int? PausedBy)
    : ServerMessage;

/// <summary>
/// Le pas de simulation <paramref name="Step"/> est joué (cf. <see cref="CombatSimulation.Step"/> : un tick de combat,
/// sauf quand le script fige le combat), avec ces commandes soumises juste avant, dans cet ordre.
/// <paramref name="Checksum"/> : empreinte de l'état du serveur après ce pas, pour détecter une désynchronisation.
/// </summary>
public sealed record TickPlayed(int Step, IReadOnlyList<CombatCommand> Commands, ulong Checksum) : ServerMessage;

/// <summary>Pause mise ou levée ; <paramref name="ByPlayer"/> : <c>null</c> pour une pause automatique (déconnexion).</summary>
public sealed record PauseChanged(bool Paused, int? ByPlayer) : ServerMessage;

/// <summary>Combat terminé ; retour au salon (les choix de héros sont gardés).</summary>
public sealed record CombatOver(bool Victory) : ServerMessage;

/// <summary>Combat abandonné sur une erreur du serveur ; retour au salon.</summary>
public sealed record CombatAborted : ServerMessage;

public enum RequestRefusal
{
    NotJoined,
    NotInLobby,
    NotInCombat,
    NotAHero,
    HeroTaken,

    /// <summary>Lancement impossible : un joueur n'a pas encore choisi de héros.</summary>
    HeroesMissing,

    /// <summary>Commande pour une unité que le joueur ne commande pas.</summary>
    NotYourUnit,

    /// <summary>Commande inconnue ou malformée.</summary>
    UnknownCommand,

    /// <summary>Trop de commandes en attente du prochain tick (pendant une pause, par exemple).</summary>
    TooManyCommands,

    /// <summary>Ce serveur ne tient pas de campagne (ou son contenu n'a pas d'arbres de compétences).</summary>
    NoCampaign,

    /// <summary>La campagne n'a pas pu être sauvegardée (disque plein, fichier protégé) : rien n'a changé.</summary>
    CampaignUnavailable,

    /// <summary>Quête inconnue du panneau.</summary>
    UnknownQuest,

    /// <summary>Lancement impossible : aucune quête choisie.</summary>
    NoQuestChosen,

    /// <summary>Ce héros n'est pas à ce joueur (ou n'existe pas).</summary>
    NotYourHero,

    /// <summary>Ce héros ne peut pas partir (mort).</summary>
    HeroUnavailable,

    /// <summary>Lancement impossible : personne ne part en quête.</summary>
    NoHeroDeployed,

    /// <summary>Lancement impossible : plus de héros que la quête n'en accueille.</summary>
    TooManyHeroes,
}

public sealed record RequestRefused(RequestRefusal Reason) : ServerMessage;

/// <summary>Le recrutement demandé à l'académie est impossible, pour cette raison.</summary>
public sealed record RecruitRefused(Campaign.RecruitRefusal Reason) : ServerMessage;
