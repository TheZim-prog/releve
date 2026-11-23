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
[JsonDerivedType(typeof(AcceptQuest), "acceptQuest")]
[JsonDerivedType(typeof(JoinGroup), "joinGroup")]
[JsonDerivedType(typeof(LeaveGroup), "leaveGroup")]
[JsonDerivedType(typeof(DeployHero), "deployHero")]
[JsonDerivedType(typeof(SpendStatPoint), "spendStatPoint")]
[JsonDerivedType(typeof(LearnSkill), "learnSkill")]
[JsonDerivedType(typeof(EquipHero), "equipHero")]
[JsonDerivedType(typeof(TravelTo), "travelTo")]
[JsonDerivedType(typeof(ResolveEvent), "resolveEvent")]
[JsonDerivedType(typeof(WaitHalfDay), "waitHalfDay")]
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

/// <summary>
/// Au salon : lancer le combat (chaque joueur doit avoir choisi un héros). Au QG : le chef lance la quête de son groupe
/// (chacun de ses joueurs doit y déployer un héros).
/// </summary>
public sealed record StartCombatRequest : ClientMessage;

/// <summary>En combat : une commande pour son héros (ou une unité qu'il domine).</summary>
public sealed record SendCommand(CombatCommand Command) : ClientMessage;

/// <summary>En combat : mettre en pause ou relancer (n'importe quel joueur, pour tout le monde).</summary>
public sealed record SetPause(bool Paused) : ClientMessage;

/// <summary>Au salon, avec une campagne : recruter un héros à l'académie (cf. <see cref="Campaign.Academy"/>).</summary>
public sealed record RecruitHero(Campaign.Recruit Recruit) : ClientMessage;

/// <summary>
/// Au QG (salon d'une campagne) : accepter une quête de son panneau ; le joueur devient le chef d'un nouveau groupe (il
/// lance, et valide en route), que d'autres peuvent rejoindre (cf. docs/design.md 4.3). La quête est alors prise.
/// </summary>
public sealed record AcceptQuest(string QuestId) : ClientMessage;

/// <summary>Au QG : rejoindre ce groupe, tant qu'il n'est pas parti et qu'il reste de la place.</summary>
public sealed record JoinGroup(int GroupId) : ClientMessage;

/// <summary>Au QG : quitter son groupe avant le départ ; si c'est le chef, le groupe se dissout et la quête revient au panneau.</summary>
public sealed record LeaveGroup : ClientMessage;

/// <summary>Au QG : partir en quête avec l'un de ses héros vivants, ou rester au QG (<c>null</c>).</summary>
public sealed record DeployHero(string? HeroId) : ClientMessage;

/// <summary>À l'académie : mettre un point de stats gagné dans cette stat (cf. <see cref="Campaign.Training"/>).</summary>
public sealed record SpendStatPoint(string HeroId, Campaign.StatKind Stat) : ClientMessage;

/// <summary>À l'académie : apprendre cette case de l'arbre avec un point de compétence gagné.</summary>
public sealed record LearnSkill(string HeroId, string Skill) : ClientMessage;

/// <summary>À l'académie : équiper ces compétences (ou variantes), dans l'ordre des boutons, et ce passif.</summary>
public sealed record EquipHero(string HeroId, IReadOnlyList<string> Equipped, string? Passive) : ClientMessage;

/// <summary>En route vers une quête (le joueur qui valide, cf. <see cref="TripInfo.Validator"/>) : aller à ce lieu voisin.</summary>
public sealed record TravelTo(string Place) : ClientMessage;

/// <summary>
/// En route (le joueur qui valide) : choisir l'option de l'événement qui attend, par son rang parmi
/// <see cref="EventInfo.Options"/>, ou passer son chemin (<c>null</c>).
/// </summary>
public sealed record ResolveEvent(int? Option) : ClientMessage;

/// <summary>
/// Au QG : le joueur attend une demi-journée (sa date avance, ses héros se reposent), par exemple quand son panneau est
/// vide (cf. docs/design.md 4.7).
/// </summary>
public sealed record WaitHalfDay : ClientMessage;

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
[JsonDerivedType(typeof(TrainingRefused), "trainingRefused")]
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
/// au QG, le héros de campagne qu'il déploie (<paramref name="Deployed"/>, ou <c>null</c> : il reste au QG) et sa date
/// (<paramref name="Hour"/>, en heures depuis le début de la campagne).
/// </summary>
public sealed record PlayerInfo(int Id, string Name, bool Connected, UnitId? Hero, string? Deployed = null, int Hour = 0);

/// <summary>
/// Les joueurs, envoyé à chaque changement (arrivée, départ, choix de héros, fin de combat), au salon comme en combat ;
/// <paramref name="Campaign"/> : la campagne du serveur, ou <c>null</c> s'il n'en tient pas.
/// </summary>
public sealed record LobbyState(string ScenarioId, IReadOnlyList<PlayerInfo> Players, CampaignInfo? Campaign = null) : ServerMessage;

/// <summary>
/// Ce que le QG montre de la campagne, vu par son destinataire : la réputation et son palier, sa dernière quête
/// (<c>null</c> avant la première depuis le lancement du serveur), les héros des joueurs (<see cref="HeroInfo.Mine"/>
/// marque les siens), son panneau de quêtes, les groupes formés et le sien, et le pot commun.
/// </summary>
public sealed record CampaignInfo
{
    public required int Reputation { get; init; }
    public required string Tier { get; init; }

    /// <summary>La date du destinataire, en heures depuis le début de la campagne (cf. <see cref="Campaign.Calendar"/>).</summary>
    public int Hour { get; init; }
    public QuestResult? LastQuest { get; init; }
    public IReadOnlyList<HeroInfo> Heroes { get; init; } = Array.Empty<HeroInfo>();
    public IReadOnlyList<QuestInfo> Board { get; init; } = Array.Empty<QuestInfo>();
    /// <summary>Les groupes : ceux qui se forment au QG, et ceux qui sont en route ou en combat.</summary>
    public IReadOnlyList<GroupInfo> Groups { get; init; } = Array.Empty<GroupInfo>();

    /// <summary>Le groupe du destinataire, ou <c>null</c>.</summary>
    public int? MyGroup { get; init; }
    public IReadOnlyDictionary<string, int> Materials { get; init; } = new Dictionary<string, int>();

    /// <summary>Les points d'intérêt de la carte découverts, et ceux déjà visités (épuisés).</summary>
    public IReadOnlyList<string> KnownPois { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ExhaustedPois { get; init; } = Array.Empty<string>();

    /// <summary>Le trajet de son groupe, ou <c>null</c> (au QG, ou en combat).</summary>
    public TripInfo? Trip { get; init; }

    /// <summary>
    /// Ce qu'a donné le dernier événement du trajet, ou <c>null</c> (aucun depuis le dernier pas) ; il reste affiché si le
    /// trajet s'est arrêté là (le dernier héros y est mort).
    /// </summary>
    public World.EventOutcome? LastEvent { get; init; }
}

/// <summary>
/// Ce qu'a rapporté une quête : son nom, son issue, son bilan de réputation, l'XP reçue par chacun de ses héros, les
/// matériaux versés au pot commun, et les héros qui y sont morts (trop de chutes).
/// </summary>
public sealed record QuestResult(string Quest, bool Victory, int Reputation, int Xp, IReadOnlyDictionary<string, int> Materials)
{
    public IReadOnlyList<string> Deaths { get; init; } = Array.Empty<string>();
}

/// <summary>Une quête du panneau, telle que le QG la montre ; <paramref name="Slots"/> : combien de héros peuvent y partir.</summary>
public sealed record QuestInfo(string Id, string Name, string Description, Campaign.QuestDifficulty Difficulty, int Xp,
    IReadOnlyDictionary<string, int> Materials, int Slots, int DurationHours = 0);

/// <summary>
/// Un héros de la campagne, tel que le QG le montre ; <paramref name="Owner"/> : le nom de son joueur. Son XP, celle
/// du niveau suivant (<c>null</c> au maximum), les points gagnés en montant de niveau, à dépenser à l'académie, et,
/// pour les siens seulement, sa fiche complète (<paramref name="Sheet"/> : points, cases apprises, équipement). Ses chutes
/// non récupérées et celles qui le tueraient, en demi-chutes (cf. <see cref="Campaign.Injuries"/>).
/// </summary>
public sealed record HeroInfo(string Id, string Name, string Archetype, int Level, string Owner, bool Mine, bool IsDead,
    int Xp = 0, int? NextLevelXp = null, int StatPointsToSpend = 0, int SkillPointsToSpend = 0, Progression.HeroSheet? Sheet = null,
    int HalfFalls = 0, int HalfFallsToDeath = 0);

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

    /// <summary>Le joueur n'est dans aucun groupe (pour le quitter, ou lancer sa quête).</summary>
    NotInGroup,

    /// <summary>Le joueur est déjà dans un groupe : il doit d'abord le quitter.</summary>
    AlreadyInGroup,

    /// <summary>Ce groupe n'existe pas, ou il est déjà parti.</summary>
    UnknownGroup,

    /// <summary>Seul le chef du groupe (celui qui a accepté la quête) la lance.</summary>
    NotTheLeader,

    /// <summary>Un autre groupe a déjà accepté cette quête.</summary>
    QuestTaken,

    /// <summary>Ce héros n'est pas à ce joueur (ou n'existe pas).</summary>
    NotYourHero,

    /// <summary>Ce héros ne peut pas partir (mort).</summary>
    HeroUnavailable,

    /// <summary>Plus de héros que la quête n'en accueille (le groupe est complet).</summary>
    TooManyHeroes,

    /// <summary>Aucun trajet en cours.</summary>
    NotTravelling,

    /// <summary>En route, seul le joueur qui valide décide (cf. <see cref="TripInfo.Validator"/>).</summary>
    NotTheValidator,

    /// <summary>Ce lieu n'est pas un pas permis (pas voisin, déjà parcouru, cul-de-sac, ou un événement attend).</summary>
    NotAMove,

    /// <summary>Aucun événement n'attend, ou cette option n'existe pas.</summary>
    UnknownOption,
}

public sealed record RequestRefused(RequestRefusal Reason) : ServerMessage;

/// <summary>Le recrutement demandé à l'académie est impossible, pour cette raison.</summary>
public sealed record RecruitRefused(Campaign.RecruitRefusal Reason) : ServerMessage;

/// <summary>L'entraînement demandé à l'académie est impossible, pour cette raison.</summary>
public sealed record TrainingRefused(Campaign.TrainingRefusal Reason) : ServerMessage;
