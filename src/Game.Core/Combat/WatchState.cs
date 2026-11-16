using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>Surveillance en cours (Tir de couverture, Protection rapprochée) : la zone surveillée, ou l'allié protégé.</summary>
public sealed class WatchState
{
    private readonly HashSet<Cell>? _cellSet;

    internal WatchState(UnitId owner, SkillDefinition skill, IReadOnlyList<Cell>? cells, UnitId? ally)
    {
        Owner = owner;
        Skill = skill;
        Cells = cells;
        Ally = ally;
        _cellSet = cells?.ToHashSet();
    }

    public UnitId Owner { get; }

    public SkillDefinition Skill { get; }

    /// <summary>Cases surveillées, ou <c>null</c> pour une surveillance d'allié.</summary>
    public IReadOnlyList<Cell>? Cells { get; }

    /// <summary>Allié protégé, ou <c>null</c> pour une surveillance de zone.</summary>
    public UnitId? Ally { get; }

    public bool Covers(Cell cell) => _cellSet?.Contains(cell) == true;
}
