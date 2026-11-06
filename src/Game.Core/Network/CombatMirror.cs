using Game.Core.Combat;

namespace Game.Core.Network;

/// <summary>
/// Le combat côté client : une simulation identique à celle du serveur, construite depuis un <see cref="CombatState"/>
/// (début, ou reconnexion : le journal est rejoué) puis avancée par les <see cref="TickPlayed"/>. Elle ne décide de
/// rien : les commandes du joueur partent au serveur, et ne s'appliquent ici que quand il les renvoie.
/// </summary>
public sealed class CombatMirror
{
    // Commandes envoyées au serveur, pas encore revenues dans un tick : pour afficher tout de suite les ordres donnés.
    private readonly List<CombatCommand> _sent = new();

    public CombatMirror(GameContent content, CombatState state)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        Launch = state.Launch;
        Simulation = CombatSimulation.Replay(state.Launch.BuildSetup(content), state.Log, state.Tick);
    }

    public CombatLaunch Launch { get; }

    public CombatSimulation Simulation { get; }

    /// <summary>
    /// Joue le tick reçu ; renvoie ses événements. Un tick manquant ou en double, ou un état différent de celui du
    /// serveur après le tick (code ou contenu différents), est une désynchronisation : <see cref="DesyncException"/>
    /// (le client doit alors se reconnecter pour rejouer le journal du serveur).
    /// </summary>
    public IReadOnlyList<CombatEvent> Apply(TickPlayed tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        if (tick.Tick != Simulation.CurrentTick + 1)
            throw new DesyncException($"Received tick {tick.Tick}, expected {Simulation.CurrentTick + 1}.");
        foreach (var command in tick.Commands)
        {
            Simulation.Submit(command);
            _sent.Remove(command);
        }
        var events = Simulation.Tick();
        if (Simulation.Checksum() != tick.Checksum)
            throw new DesyncException($"State differs from the server's after tick {tick.Tick}.");
        return events;
    }

    /// <summary>Une commande vient d'être envoyée au serveur (elle comptera dans <see cref="PlannedOrders"/>).</summary>
    public void Sent(CombatCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _sent.Add(command);
    }

    /// <summary>Le serveur a refusé une commande : on oublie celles en route plutôt que d'afficher un ordre fantôme.</summary>
    public void ForgetSent() => _sent.Clear();

    /// <summary>Ordres à l'avance : ceux de la simulation, plus les ordres et annulations envoyés mais pas encore revenus.</summary>
    public IReadOnlyList<ActivateCommand> PlannedOrders() => Simulation.PlannedOrders(_sent);

    /// <summary>Joueur qui commande ce héros, ou <c>null</c> (IA, ennemi…).</summary>
    public int? PlayerOf(UnitId hero) => Launch.Heroes.FirstOrDefault(h => h.Unit == hero)?.Player;

    /// <summary>Héros du joueur, ou <c>null</c>.</summary>
    public UnitId? HeroOf(int player) => Launch.Heroes.FirstOrDefault(h => h.Player == player)?.Unit;
}

/// <summary>La simulation du client ne suit plus celle du serveur.</summary>
public sealed class DesyncException(string message) : Exception(message);
