using Game.Core.Campaign;
using Game.Core.Combat;

namespace Game.Core.Network;

// Les combats en cours : un seul en combat libre (tout le monde y est), un par groupe parti en quête au QG d'une
// campagne. Chacun a sa simulation, ses commandes en attente et sa pause ; ses messages ne vont qu'à ses joueurs.
public sealed partial class ServerSession
{
    private readonly List<CombatRoom> _rooms = new();

    /// <summary>La simulation qui fait foi du premier combat en cours (le combat libre), ou <c>null</c>.</summary>
    public CombatSimulation? Simulation => _rooms.FirstOrDefault()?.Simulation;

    /// <summary>Le premier combat en cours est en pause.</summary>
    public bool IsPaused => _rooms.FirstOrDefault()?.Paused ?? false;

    /// <summary>Les simulations qui font foi, une par combat en cours.</summary>
    public IReadOnlyList<CombatSimulation> Combats => _rooms.Select(r => r.Simulation).ToList();

    /// <summary>
    /// Un combat en cours : son lancement, sa simulation, les commandes du prochain tick et sa pause ; au QG, le groupe
    /// qui le joue et ce qu'il faudra en garder (<see cref="Fight"/>).
    /// </summary>
    private sealed class CombatRoom(CombatLaunch launch, CombatSetup setup, Party? party, QuestFight? fight)
    {
        public CombatLaunch Launch { get; } = launch;
        public CombatSimulation Simulation { get; } = new(setup);
        public List<(int Player, CombatCommand Command)> Queue { get; } = new();
        public bool Paused { get; set; }
        public int? PausedBy { get; set; }
        public Party? Party { get; } = party;
        public QuestFight? Fight { get; } = fight;
    }

    /// <summary>
    /// Une quête en combat : sa définition, ses héros de campagne et leurs unités, et les joueurs partis (même ceux dont
    /// le héros est mort en route : ils avancent de sa durée).
    /// </summary>
    private sealed record QuestFight(QuestDefinition Quest, IReadOnlyList<(string Hero, UnitId Unit)> Deployed, IReadOnlyList<string> Party);

    /// <summary>Le combat de ce joueur : le combat libre, ou celui de son groupe ; <c>null</c> s'il n'est dans aucun.</summary>
    private CombatRoom? RoomOf(Player player) =>
        IsCampaign ? PartyOf(player.Uid)?.Room : _rooms.FirstOrDefault();

    /// <summary>Les joueurs d'un combat : tout le monde en combat libre, ceux de son groupe au QG.</summary>
    private IEnumerable<Player> Members(CombatRoom room) =>
        room.Party is { } party ? _players.Where(p => party.Members.Contains(p.Uid)) : _players;

    private void SendTo(CombatRoom room, ServerMessage message)
    {
        foreach (var player in Members(room).ToList())
            player.Peer?.Send(message);
    }

    /// <summary>Le combat commence : ses joueurs le reçoivent ; un joueur parti en route garde sa place, et le combat l'attend.</summary>
    private void Open(CombatRoom room)
    {
        _rooms.Add(room);
        if (room.Party is { } party)
            party.Room = room;
        SendTo(room, new CombatState(room.Launch, Array.Empty<LoggedCommand>(), 0, Paused: false, PausedBy: null));
        if (Members(room).Any(p => p.Hero is not null && p.Peer is null))
            SetPaused(room, true, byPlayer: null);
        // Au QG, les unités jouées viennent d'être données : tout le monde doit savoir qui joue quoi.
        if (IsCampaign)
            BroadcastLobby();
    }

    /// <summary>Un joueur revient dans son combat : tout le combat, à rejouer ; une pause de déconnexion se lève quand tout le monde est là.</summary>
    private void Rejoin(Player player, CombatRoom room)
    {
        player.Peer!.Send(new CombatState(room.Launch, room.Simulation.CommandLog.ToList(), room.Simulation.Step, room.Paused, room.PausedBy));
        if (room.Paused && room.PausedBy is null && Members(room).All(p => p.Hero is null || p.Peer is not null))
            SetPaused(room, false, byPlayer: null);
    }

    private void TickRoom(CombatRoom room)
    {
        if (room.Paused)
            return;
        var commands = room.Queue.ToList();
        room.Queue.Clear();
        var simulation = room.Simulation;
        try
        {
            foreach (var (_, command) in commands)
                simulation.Submit(command);
            simulation.Tick();
        }
        catch
        {
            // Bug de la simulation : son état est à moitié changé, plus rien ne serait fiable. Le combat est abandonné.
            EndCombat(room, new CombatAborted());
            throw;
        }
        SendTo(room, new TickPlayed(simulation.Step, commands.Select(c => c.Command).ToList(), simulation.Checksum()));

        if (simulation.Outcome is { } victory)
        {
            // Une sauvegarde de campagne qui échoue (disque plein…) ne doit pas bloquer le combat fini : il se termine,
            // et l'erreur remonte au serveur, qui la journalise.
            try
            {
                if (room.Fight is { } fight)
                    RecordInCampaign(fight, simulation);
            }
            finally
            {
                EndCombat(room, new CombatOver(victory));
            }
        }
    }

    /// <summary>
    /// Fin d'un combat : ses joueurs reviennent au salon (au QG, la quête est jouée : son groupe se dissout ; les unités
    /// jouées ne valent plus rien, les héros déployés le restent) ; les absents qui ne sont plus attendus nulle part s'en
    /// vont.
    /// </summary>
    private void EndCombat(CombatRoom room, ServerMessage end)
    {
        SendTo(room, end);
        var members = Members(room).ToList();
        _rooms.Remove(room);
        if (room.Party is { } party)
            _parties.Remove(party);
        if (IsCampaign)
        {
            foreach (var player in members)
                player.Hero = null;
        }
        ForgetTheAbsent();
        BroadcastLobby();
    }

    private void Command(Player player, CombatCommand command)
    {
        if (RoomOf(player) is not { } room)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotInCombat));
            return;
        }
        var simulation = room.Simulation;
        if (Normalize(simulation, command) is not { } valid)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.UnknownCommand));
            return;
        }
        // Son héros, ou une unité qu'il domine (une domination finie ou un héros à terre : la simulation tranchera).
        // « Continuer » un message du script ne vise aucune unité : n'importe quel joueur du combat le peut.
        var unit = simulation.FindUnit(valid.Unit);
        if (valid is not ContinueCommand
            && (player.Hero is not { } hero || unit is null || !CombatLaunch.IsCommandedBy(unit, hero)))
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotYourUnit));
            return;
        }
        if (room.Queue.Count(q => q.Player == player.Id) >= Protocol.MaxQueuedCommandsPerPlayer)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.TooManyCommands));
            return;
        }
        room.Queue.Add((player.Id, valid));
    }

    /// <summary>
    /// La commande reconstruite à partir de ses seuls champs utiles (l'unité d'un ordre est celle de son activation,
    /// quoi que dise le message), ou <c>null</c> si elle est inconnue ou malformée : la simulation ne doit jamais
    /// recevoir de quoi la faire planter.
    /// </summary>
    private static CombatCommand? Normalize(CombatSimulation simulation, CombatCommand? command)
    {
        var activation = command switch
        {
            ActivateCommand a => a,
            OrderCommand o => o.Activation,
            _ => null,
        };
        if (command is CancelOrderCommand cancel)
            return new CancelOrderCommand(cancel.Unit);
        if (command is ContinueCommand resume)
            return new ContinueCommand(resume.Message);
        if (activation is null || (activation.Skill is { } skill && skill.SkillId is null)
            || (activation.Interaction is { } interaction && !Enum.IsDefined(interaction.Kind)))
            return null;
        try
        {
            _ = simulation.Check(activation, ignoreTiming: true);
        }
        catch (Exception e) when (e is ArgumentException or NullReferenceException or InvalidOperationException)
        {
            return null;
        }
        var clean = new ActivateCommand(activation.Unit, activation.MoveTo, activation.Skill, activation.Interaction);
        return command is OrderCommand ? new OrderCommand(clean) : clean;
    }

    private void Pause(Player player, bool paused)
    {
        if (RoomOf(player) is not { } room)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotInCombat));
            return;
        }
        // Mettre en pause pendant une pause de déconnexion en fait une pause de joueur : elle ne sera plus levée
        // d'elle-même au retour du déconnecté.
        if (paused ? !room.Paused || room.PausedBy is null : room.Paused)
            SetPaused(room, paused, paused ? player.Id : null);
    }

    private void SetPaused(CombatRoom room, bool paused, int? byPlayer)
    {
        room.Paused = paused;
        room.PausedBy = paused ? byPlayer : null;
        SendTo(room, new PauseChanged(paused, room.PausedBy));
    }
}
