using Game.Core.Grids;

namespace Game.Core.Combat;

// Vagues de renforts : des unités qui arrivent en cours de combat.
public sealed partial class CombatSimulation
{
    // Les vagues dans l'ordre d'arrivée (à égalité, l'ordre du scénario), et le nombre déjà arrivées.
    private IReadOnlyList<WaveSetup> _waves = Array.Empty<WaveSetup>();
    private int _wavesArrived;

    /// <summary>Nombre de vagues de renforts du combat.</summary>
    public int WaveCount => _waves.Count;

    /// <summary>Nombre de vagues déjà arrivées (ou passées, si aucune de leurs unités n'a trouvé de place).</summary>
    public int WavesArrived => _wavesArrived;

    /// <summary>Des ennemis doivent encore arriver : une vague à venir en amène.</summary>
    public bool EnemiesStillToCome => _waves.Skip(_wavesArrived).Any(w => w.BringsEnemies);

    /// <summary>Les définitions des renforts sont valides (leur placement est vérifié avec les objectifs, cf. MissionValidator).</summary>
    private void SetUpWaves(IReadOnlyList<WaveSetup> waves)
    {
        foreach (var unit in waves.SelectMany(w => w.Units))
        {
            ArgumentNullException.ThrowIfNull(unit.Definition);
            unit.Definition.Validate();
            unit.Definition.ValidateAgainst(Rules);
        }
        _waves = waves.OrderBy(w => w.AtMs).ToList(); // tri stable : à égalité, l'ordre donné
    }

    /// <summary>
    /// Début de tick : les vagues dont l'heure est venue arrivent. Chaque unité reçoit l'identifiant suivant, et arrive
    /// sur sa case ou, prise, sur la case libre la plus proche à pied (aucune : elle n'arrive pas). Si le chef de son camp
    /// est déjà tombé, elle fuit ou se rend aussitôt, comme ses camarades.
    /// </summary>
    private void BringReinforcements()
    {
        while (_wavesArrived < _waves.Count && Rules.MillisecondsToTicks(_waves[_wavesArrived].AtMs) <= CurrentTick)
        {
            var wave = _waves[_wavesArrived++];
            var arrived = new List<Unit>();
            foreach (var setup in wave.Units)
            {
                if (NearestFreeCell(setup.Position) is not { } cell)
                    continue;
                var unit = new Unit(setup with { Id = NextUnitId(), Position = cell }, Rules);
                _units.Add(unit);
                arrived.Add(unit);
            }
            if (arrived.Count == 0)
                continue;
            _events.Add(new WaveArrived(CurrentTick, _wavesArrived, arrived.Select(u => u.Id).ToList()));
            foreach (var unit in arrived)
            {
                if (_units.FirstOrDefault(u => u.FellAsLeader && u.OriginalTeam == unit.OriginalTeam) is { MinionsOnFall: { } reaction })
                    ReactToFallenLeader(unit, reaction);
            }
        }
    }

    /// <summary>L'identifiant d'une unité qui arrive en cours de combat : celui qui suit le plus grand existant (déterministe).</summary>
    private UnitId NextUnitId() => new(_units.Max(u => u.Id.Value) + 1);

    /// <summary>
    /// La case libre (praticable, sans unité) la plus proche à pied de <paramref name="cell"/>, elle-même d'abord : jamais
    /// derrière un mur ni dans une poche fermée.
    /// </summary>
    private Cell? NearestFreeCell(Cell cell) =>
        Pathfinder.ReachableCells(_grid, cell, int.MaxValue).Keys
            .Where(c => UnitAt(c) is null)
            .Select(c => (Cell?)c)
            .FirstOrDefault();
}
