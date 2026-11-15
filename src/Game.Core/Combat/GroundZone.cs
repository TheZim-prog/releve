using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>Zone au sol en cours (fumée, pare-tirs, verglas, piège…), posée par la compétence d'une unité.</summary>
public sealed class GroundZone
{
    private readonly HashSet<Cell> _cellSet;

    internal GroundZone(int id, Unit owner, SkillDefinition skill, IReadOnlyList<Cell> cells, int? endTick)
    {
        Id = id;
        Owner = owner.Id;
        Team = owner.Team;
        Skill = skill;
        Cells = cells;
        EndTick = endTick;
        _cellSet = cells.ToHashSet();
    }

    /// <summary>Numéro de la zone dans ce combat (dans l'ordre de pose).</summary>
    public int Id { get; }

    public UnitId Owner { get; }

    /// <summary>Camp du lanceur quand il l'a posée : c'est lui qui dit qui sont ses « ennemis ».</summary>
    public int Team { get; }

    public SkillDefinition Skill { get; }

    public GroundZoneDefinition Definition => Skill.Ground!;

    public IReadOnlyList<Cell> Cells { get; }

    /// <summary>Tick où elle disparaît, ou <c>null</c> pour un piège qui attend d'être déclenché.</summary>
    public int? EndTick { get; }

    public bool Contains(Cell cell) => _cellSet.Contains(cell);
}
