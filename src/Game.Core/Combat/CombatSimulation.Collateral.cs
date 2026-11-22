using Game.Core.Grids;

namespace Game.Core.Combat;

// Dégâts collatéraux (docs/design.md 3.4) : le décor que cassent les héros et les civils touchés, relevés pendant le
// combat pour le bilan de réputation de la mission (Reputation.ReputationReport). Un coup « des héros » vient du camp
// des joueurs au moment où il porte : un héros, un gadget, ou un ennemi dominé. Le reste (ennemis, décor qui s'effondre)
// est du fait des ennemis.
public sealed partial class CombatSimulation
{
    private readonly HashSet<UnitId> _civiliansHurtByHeroes = new();
    private readonly HashSet<UnitId> _civiliansLostToHeroes = new();

    /// <summary>Obstacles bas du décor détruits par les héros (pas les obstacles dressés pour un temps, comme la glace).</summary>
    public int SceneryDestroyedByHeroes { get; private set; }

    /// <summary>Le civil a perdu des PV par un coup des héros (qu'il ait survécu ou non).</summary>
    public bool WasHurtByHeroes(UnitId civilian) => _civiliansHurtByHeroes.Contains(civilian);

    /// <summary>Le civil est perdu (tombé à 0 PV) par un coup des héros.</summary>
    public bool WasLostToHeroes(UnitId civilian) => _civiliansLostToHeroes.Contains(civilian);

    /// <summary>Le coup vient du camp des joueurs ; jamais d'une structure : le décor qui s'effondre est du fait des ennemis.</summary>
    private static bool IsHeroesDoing(Unit source) => source.Team == PlayerTeam && !source.IsStructure;

    /// <summary>
    /// L'obstacle bas de <paramref name="cell"/> est détruit par <paramref name="source"/> : un dégât collatéral si ce sont
    /// les héros et que c'est du vrai décor (un obstacle dressé pour un temps, comme un bloc de glace, ne compte pas).
    /// </summary>
    private void NoteSceneryDestroyed(Unit source, Cell cell)
    {
        if (IsHeroesDoing(source) && !_raisedObstacles.Exists(o => o.Cell == cell))
            SceneryDestroyedByHeroes++;
    }

    /// <summary>Un civil vient de perdre des PV par un coup de <paramref name="source"/> (et peut-être d'être perdu).</summary>
    private void NoteCivilianHarmed(Unit source, Unit civilian)
    {
        if (!IsHeroesDoing(source))
            return;
        _civiliansHurtByHeroes.Add(civilian.Id);
        if (civilian.Status == UnitStatus.Removed)
            _civiliansLostToHeroes.Add(civilian.Id);
    }
}
