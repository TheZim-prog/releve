using Game.Core.Grids;
using Game.Core.Scripts;

namespace Game.Core.Combat;

// Actions imposées par le script (tutoriel) : le combat est figé, des héros ont chacun une seule action permise.
public sealed partial class CombatSimulation
{
    // Actions imposées pas encore faites, dans l'ordre du script.
    private readonly List<Imposition> _impositions = new();

    // Jauge de chaque héros avant qu'on la remplisse pour son action imposée : rendue si l'action est abandonnée.
    private readonly Dictionary<UnitId, int> _gaugesBeforeImposition = new();

    /// <summary>Actions imposées pas encore faites (vide d'ordinaire). Tant qu'il y en a, le combat est figé.</summary>
    public IReadOnlyList<Imposition> ImposedActions => _impositions.AsReadOnly();

    /// <summary>La consigne des actions imposées en cours (affichée jusqu'à ce qu'elles soient faites), ou <c>null</c>.</summary>
    public ScriptMessage? Instruction { get; private set; }

    /// <summary>L'action imposée à cette unité, ou <c>null</c>.</summary>
    public Imposition? ImposedOn(UnitId unit) => _impositions.Find(i => i.Unit == unit);

    /// <summary>
    /// Impose ces actions, avec leur consigne : chaque héros debout reçoit la sienne, jauge pleine, et perd son ordre à
    /// l'avance (un héros à terre, ou dont la cible n'est pas là, n'en reçoit pas : elle est abandonnée). Puis celles
    /// déjà impossibles sont abandonnées, et un héros joué par l'IA fait la sienne aussitôt.
    /// </summary>
    private void Impose(IReadOnlyList<ImposedAction> actions, string? speaker, string? say)
    {
        foreach (var action in actions)
        {
            if (Tagged(action.Unit) is not { } unit)
                continue; // pas (encore) sur la carte
            var target = action.Target is { } tag ? Tagged(tag)?.Id : null;
            if (unit.Status != UnitStatus.Active || (action.Target is not null && target is null))
            {
                _events.Add(new ImposedActionEnded(CurrentTick, unit.Id, Done: false));
                continue;
            }
            if (_orders.RemoveAll(o => o.Unit == unit.Id) > 0)
                _events.Add(new OrderCancelled(CurrentTick, unit.Id));
            _gaugesBeforeImposition[unit.Id] = unit.Gauge;
            SetGauge(unit, Rules.GaugeCapacity);
            _impositions.Add(new Imposition(unit.Id, action.Skill, target, action.Cell, action.MoveTo));
            _events.Add(new ActionImposed(CurrentTick, unit.Id));
        }
        // La consigne n'a de sens que si une action au moins est imposée.
        if (_impositions.Count > 0 && say is not null)
            Instruction = Show(speaker, say, freezes: false);
        SettleImpositions();
    }

    private Unit? Tagged(string tag) => _units.Find(u => u.Tag == tag);

    /// <summary>Un héros vient de faire son action imposée : les autres sont revues, et le script reprend s'il n'en reste plus.</summary>
    private void CompleteImposition(Imposition imposition)
    {
        EndImposition(imposition, done: true);
        SettleImpositions();
        if (_impositions.Count == 0)
            FinishEffect(_blockingEffect);
    }

    /// <summary>
    /// Les actions imposées devenues impossibles sont abandonnées (un héros confus ou dominé ne peut pas obéir), et les
    /// héros joués par l'IA font la leur, jusqu'à ce que plus rien ne change (une action peut en rendre une autre
    /// impossible). Plus aucune : la consigne disparaît.
    /// </summary>
    private void SettleImpositions()
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var imposition in _impositions.ToList())
            {
                var unit = FindUnit(imposition.Unit)!;
                var plan = ControllerOf(unit) is null || RunsItsOwnAi(unit) ? FirstPlanFor(imposition, unit) : null;
                if (plan is null)
                {
                    EndImposition(imposition, done: false);
                    changed = true;
                }
                else if (RunsItsOwnAi(unit))
                {
                    ApplyActivation(plan);
                    EndImposition(imposition, done: true);
                    changed = true;
                }
            }
        }
        while (changed && _impositions.Count > 0);
        if (_impositions.Count == 0)
            Instruction = null;
    }

    /// <summary>L'unité est jouée par sa propre IA (un allié du jeu), pas par une confusion ou un dominateur.</summary>
    private bool RunsItsOwnAi(Unit unit) =>
        unit.Behavior is not null && unit.DominatedBy is null && !unit.HasStatus(StatusKind.Confused) && !unit.IsFleeing;

    /// <summary>L'action est faite ou abandonnée ; abandonnée, le héros retrouve la jauge qu'il avait avant.</summary>
    private void EndImposition(Imposition imposition, bool done)
    {
        _impositions.Remove(imposition);
        _gaugesBeforeImposition.Remove(imposition.Unit, out var gauge);
        if (!done && FindUnit(imposition.Unit) is { Status: UnitStatus.Active } unit && unit.Gauge == Rules.GaugeCapacity)
            unit.Gauge = gauge;
        _events.Add(new ImposedActionEnded(CurrentTick, imposition.Unit, done));
    }

    /// <summary>
    /// Une activation valide qui respecte l'action imposée, ou <c>null</c> s'il n'y en a aucune : sur place d'abord, puis
    /// depuis chaque case atteignable (dans l'ordre du calcul des chemins, déterministe) quand le déplacement est libre.
    /// </summary>
    private ActivationPlan? FirstPlanFor(Imposition imposition, Unit unit)
    {
        if (unit.Status != UnitStatus.Active)
            return null;
        var use = imposition.Skill is { } skill ? new SkillUse(skill, imposition.Target, imposition.Cell) : null;
        foreach (var moveTo in Destinations(imposition, unit))
        {
            if (ValidateActivation(new ActivateCommand(unit.Id, moveTo, use)).Plan is { } plan)
                return plan;
        }
        return null;
    }

    private IEnumerable<Cell?> Destinations(Imposition imposition, Unit unit)
    {
        if (imposition.MoveTo is { } imposed)
        {
            yield return imposed;
            yield break;
        }
        yield return null;
        foreach (var cell in ReachableCells(unit.Id).Keys)
        {
            if (cell != unit.Position)
                yield return cell;
        }
    }
}
