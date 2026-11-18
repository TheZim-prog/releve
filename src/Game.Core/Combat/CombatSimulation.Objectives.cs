using Game.Core.Grids;
using Game.Core.Objectives;

namespace Game.Core.Combat;

// Objectifs de mission et issue du combat.
public sealed partial class CombatSimulation
{
    /// <summary>Camp des joueurs : sa chute est une défaite, et c'est lui que servent les objectifs.</summary>
    public const int PlayerTeam = 0;

    private readonly List<Objective> _objectives = new();

    /// <summary>Objectifs du combat, dans l'ordre du scénario.</summary>
    public IReadOnlyList<Objective> Objectives { get; private set; } = Array.Empty<Objective>();

    /// <summary>
    /// Victoire, défaite, ou <c>null</c> tant que le combat dure. Une fois fixée, elle ne change plus (la simulation
    /// peut encore tourner, mais les objectifs ne bougent plus).
    /// </summary>
    public bool? Outcome { get; private set; }

    /// <summary>Sorties de la poursuite (vide sans poursuite) : seul le vilain poursuivi s'enfuit par là.</summary>
    public IReadOnlySet<Cell> PursuitExits { get; private set; } = new HashSet<Cell>();

    /// <summary>Le vilain poursuivi (objectif Poursuite), ou <c>null</c> sans poursuite.</summary>
    public Unit? PursuedUnit { get; private set; }

    /// <summary>Les objets à neutraliser de toutes les courses contre la montre, posés sur la carte (cases d'objet de mission).</summary>
    public IReadOnlyList<Device> Devices { get; private set; } = Array.Empty<Device>();

    /// <summary>L'objet à neutraliser posé sur cette case, ou <c>null</c>.</summary>
    public Device? DeviceAt(Cell cell) => Devices.FirstOrDefault(d => d.Cell == cell);

    private void SetUpObjectives(CombatSetup setup)
    {
        setup.ValidateObjectives();
        _objectives.AddRange(setup.Objectives.Select(o => new Objective(o, Rules)));
        Objectives = _objectives.AsReadOnly();
        PursuitExits = setup.Objectives.SelectMany(o => o.Exits ?? Array.Empty<Cell>()).ToHashSet();
        PursuedUnit = _units.SingleOrDefault(u => u.IsPursued);
        Devices = _objectives.SelectMany(o => o.Devices).ToList().AsReadOnly();
        foreach (var device in Devices)
            _grid.SetTerrain(device.Cell, Terrain.Fixture);
    }

    /// <summary>Un héros travaille sur l'objet : une interaction de plus (au plus le nombre demandé).</summary>
    private void Operate(Unit unit, Device device)
    {
        device.Done++;
        _events.Add(new DeviceOperated(CurrentTick, unit.Id, device.Cell, device.Done, device.Definition.Interactions));
    }

    /// <summary>
    /// Une unité en fuite quitte le combat sur cette case : une sortie de la poursuite pour le vilain poursuivi, le bord
    /// de carte pour les autres.
    /// </summary>
    internal bool IsExitFor(Unit unit, Cell cell) => unit.IsPursued ? PursuitExits.Contains(cell) : _grid.IsEdge(cell);

    /// <summary>
    /// Fin de tick (et après le script, qui peut faire agir des unités) : défaite si tous les héros (hors alliés
    /// scénarisés) sont hors de combat (rien ne se réussit alors, même au même tick) ; sinon
    /// chaque objectif en cours peut être réussi ou raté, puis l'issue tombe : défaite si un objectif principal est raté,
    /// victoire quand tous les principaux sont réussis. À la fin, plus rien n'est en cours : à la victoire, les
    /// secondaires se règlent (cf. <see cref="ObjectiveRules.SettleAtVictory"/>) ; à la défaite, tout ce qui restait est
    /// raté. Un combat sans objectif (tests, outils) ne se termine jamais.
    /// </summary>
    private void UpdateObjectives()
    {
        if (Outcome is not null || _objectives.Count == 0)
            return;
        // Un allié du jeu qui n'agit que sur ordre du script (le télépathe du prologue) ne tient pas le combat à lui seul.
        if (!_units.Any(u => u.IsHero && u.IsStandingCombatant && u.Behavior != Behaviors.Scripted))
        {
            EndCombat(victory: false);
            return;
        }

        UpdateEach(ObjectiveRules.Evaluate);
        var main = _objectives.Where(o => !o.Definition.Secondary);
        if (main.Any(o => o.State == ObjectiveState.Failed))
            EndCombat(victory: false);
        else if (main.All(o => o.State == ObjectiveState.Completed))
            EndCombat(victory: true);
    }

    /// <summary>Chaque objectif en cours prend l'état que donne <paramref name="rule"/> ; un changement s'annonce.</summary>
    private void UpdateEach(Func<Objective, CombatSimulation, ObjectiveState> rule)
    {
        for (var i = 0; i < _objectives.Count; i++)
        {
            var objective = _objectives[i];
            if (objective.State != ObjectiveState.InProgress)
                continue;
            objective.State = rule(objective, this);
            if (objective.State == ObjectiveState.Completed)
                _events.Add(new ObjectiveCompleted(CurrentTick, i));
            else if (objective.State == ObjectiveState.Failed)
                _events.Add(new ObjectiveFailed(CurrentTick, i));
        }
    }

    /// <summary>Le combat est joué : les objectifs encore en cours se règlent, puis l'issue s'annonce.</summary>
    private void EndCombat(bool victory)
    {
        UpdateEach(victory ? ObjectiveRules.SettleAtVictory : (_, _) => ObjectiveState.Failed);
        Outcome = victory;
        _events.Add(new CombatEnded(CurrentTick, victory));
    }
}
