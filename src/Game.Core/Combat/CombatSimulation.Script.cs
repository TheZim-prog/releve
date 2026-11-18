using Game.Core.Scripts;

namespace Game.Core.Combat;

// Script de mission : déclencheurs (conditions → effets) et messages, qui peuvent figer le combat.
public sealed partial class CombatSimulation
{
    private enum TriggerState
    {
        Waiting,
        Running,
        Done,
    }

    private IReadOnlyList<ScriptTrigger> _script = Array.Empty<ScriptTrigger>();
    private TriggerState[] _triggerStates = Array.Empty<TriggerState>();

    // Pour chaque déclencheur, le rang de celui qu'il suit (condition « after »), ou -1.
    private int[] _followed = Array.Empty<int>();

    // Effets des déclencheurs partis, à jouer dans l'ordre : (déclencheur, rang de l'effet).
    private readonly Queue<(int Trigger, int Effect)> _scriptQueue = new();
    private int _messagesShown;

    // L'effet qui fige le combat (message figé ou actions imposées) : son déclencheur se termine avec lui, si c'était
    // son dernier effet.
    private (int Trigger, int Effect) _blockingEffect;

    // Pas de simulation commencé figé : ses commandes sont jugées figées jusqu'au bout.
    private bool _inFrozenStep;

    /// <summary>Le message qui fige le combat en attendant « Continuer », ou <c>null</c>.</summary>
    public ScriptMessage? FrozenMessage { get; private set; }

    /// <summary>
    /// Le script fige le combat : le temps ne s'écoule plus et personne n'agit, sauf les héros qui ont une action imposée
    /// (et seulement pour la faire).
    /// </summary>
    public bool IsTimeFrozen => FrozenMessage is not null || _impositions.Count > 0;

    private void SetUpScript(CombatSetup setup)
    {
        setup.ValidateScript();
        _script = setup.Script;
        _triggerStates = new TriggerState[_script.Count];
        var index = _script.Select((t, i) => (t.Id, i)).ToDictionary(p => p.Id, p => p.i);
        _followed = _script.Select(t => t.When?.After is { } after ? index[after] : -1).ToArray();
    }

    /// <summary>
    /// Les déclencheurs dont les conditions sont réunies partent (dans l'ordre du script), et leurs effets se jouent
    /// jusqu'au prochain message figé. Un déclencheur terminé peut en lancer un autre au même pas. Le script s'arrête
    /// avec le combat.
    /// </summary>
    private void RunScript()
    {
        if (Outcome is not null)
            return;
        bool started;
        do
        {
            started = false;
            for (var i = 0; i < _script.Count; i++)
            {
                if (_triggerStates[i] != TriggerState.Waiting || !ConditionsMet(i))
                    continue;
                _triggerStates[i] = TriggerState.Running;
                for (var effect = 0; effect < _script[i].Do.Count; effect++)
                    _scriptQueue.Enqueue((i, effect));
                started = true;
            }
            PlayQueuedEffects();
        }
        while (started && !IsTimeFrozen);
    }

    private bool ConditionsMet(int trigger)
    {
        if (_script[trigger].When is not { } when)
            return true;
        if (when.AtMs is { } atMs && CurrentTick < Rules.MillisecondsToTicks(atMs))
            return false;
        if (_followed[trigger] >= 0 && _triggerStates[_followed[trigger]] != TriggerState.Done)
            return false;
        // Une chute compte même si l'unité a été relevée depuis (les autres conditions ont pu tarder).
        if (when.UnitDown is { } tag && Tagged(tag) is not { HasFallen: true })
            return false;
        return true;
    }

    /// <summary>Joue les effets en attente, dans l'ordre, jusqu'à ce que l'un d'eux fige le combat.</summary>
    private void PlayQueuedEffects()
    {
        while (!IsTimeFrozen && _scriptQueue.TryDequeue(out var next))
        {
            var effect = _script[next.Trigger].Do[next.Effect];
            if (effect.Impose is { } imposed)
                Impose(imposed, effect.Speaker, effect.Say);
            else if (effect.Act is { } ordered)
                Act(ordered, effect.Speaker, effect.Say);
            else
            {
                var message = Show(effect.Speaker, effect.Say!, effect.Freeze);
                if (effect.Freeze)
                    FrozenMessage = message;
            }
            if (IsTimeFrozen)
                _blockingEffect = next;
            else
                FinishEffect(next);
        }
    }

    private ScriptMessage Show(string? speaker, string text, bool freezes)
    {
        var message = new ScriptMessage(++_messagesShown, speaker, text, freezes);
        _events.Add(new ScriptMessageShown(CurrentTick, message));
        return message;
    }

    /// <summary>Un effet est joué jusqu'au bout : son déclencheur est terminé si c'était le dernier.</summary>
    private void FinishEffect((int Trigger, int Effect) played)
    {
        if (played.Effect == _script[played.Trigger].Do.Count - 1)
            _triggerStates[played.Trigger] = TriggerState.Done;
    }

    /// <summary>
    /// « Continuer » : le message figé en cours se termine, le temps reprend (sauf si le script refige aussitôt). Sans
    /// effet si ce n'est plus lui qui fige le combat (un autre joueur a déjà continué).
    /// </summary>
    private void Continue(ContinueCommand command)
    {
        if (FrozenMessage is not { } message || message.Number != command.Message)
            return;
        FrozenMessage = null;
        _events.Add(new TimeResumed(CurrentTick, message.Number));
        FinishEffect(_blockingEffect);
    }
}
