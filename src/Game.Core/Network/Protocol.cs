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
    /// <summary>À changer à chaque modification incompatible des messages.</summary>
    public const int Version = 1;

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
public abstract record ClientMessage;

/// <summary>
/// Rejoindre la partie. Un nom déjà connu dont le joueur est déconnecté en plein combat le reprend (reconnexion).
/// </summary>
public sealed record JoinRequest(string Name, int ProtocolVersion, string ContentVersion) : ClientMessage;

/// <summary>Au salon : choisir un héros, ou le laisser (<c>null</c>).</summary>
public sealed record PickHero(UnitId? Unit) : ClientMessage;

/// <summary>Au salon : lancer le combat (chaque joueur doit avoir choisi un héros).</summary>
public sealed record StartCombatRequest : ClientMessage;

/// <summary>En combat : une commande pour son héros (ou une unité qu'il domine).</summary>
public sealed record SendCommand(CombatCommand Command) : ClientMessage;

/// <summary>En combat : mettre en pause ou relancer (n'importe quel joueur, pour tout le monde).</summary>
public sealed record SetPause(bool Paused) : ClientMessage;

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
public abstract record ServerMessage;

/// <summary>Le joueur est accepté sous l'identifiant <paramref name="PlayerId"/>.</summary>
public sealed record Welcome(int PlayerId) : ServerMessage;

public enum JoinRefusal
{
    ProtocolMismatch,

    /// <summary>Le contenu (<c>data/</c>) diffère de celui du serveur : les simulations divergeraient.</summary>
    ContentMismatch,

    InvalidName,

    /// <summary>Un joueur connecté porte déjà ce nom.</summary>
    NameTaken,

    /// <summary>Un combat est en cours et ce nom n'y joue pas.</summary>
    CombatInProgress,
}

public sealed record JoinRefused(JoinRefusal Reason) : ServerMessage;

/// <summary>Un joueur, son héros éventuel, et s'il est connecté (en combat, un déconnecté garde sa place).</summary>
public sealed record PlayerInfo(int Id, string Name, bool Connected, UnitId? Hero);

/// <summary>Les joueurs, envoyé à chaque changement (arrivée, départ, choix de héros), au salon comme en combat.</summary>
public sealed record LobbyState(string ScenarioId, IReadOnlyList<PlayerInfo> Players) : ServerMessage;

/// <summary>
/// Le combat : son lancement, les commandes déjà jouées et le tick atteint (vide et 0 au début ; à rejouer à la
/// reconnexion), et la pause. <paramref name="PausedBy"/> : joueur qui l'a mise, ou <c>null</c> (déconnexion).
/// </summary>
public sealed record CombatState(CombatLaunch Launch, IReadOnlyList<LoggedCommand> Log, int Tick, bool Paused, int? PausedBy)
    : ServerMessage;

/// <summary>
/// Le tick <paramref name="Tick"/> est joué, avec ces commandes soumises juste avant, dans cet ordre.
/// <paramref name="Checksum"/> : empreinte de l'état du serveur après ce tick, pour détecter une désynchronisation.
/// </summary>
public sealed record TickPlayed(int Tick, IReadOnlyList<CombatCommand> Commands, ulong Checksum) : ServerMessage;

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
}

public sealed record RequestRefused(RequestRefusal Reason) : ServerMessage;
