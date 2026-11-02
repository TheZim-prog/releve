using Game.Core.Grids;

namespace Game.Core.Combat;

public readonly record struct UnitId(int Value)
{
    public override string ToString() => $"#{Value}";
}

/// <summary>Ce que devient une unité à 0 PV.</summary>
public enum DefeatOutcome
{
    /// <summary>À terre : reste sur sa case et l'occupe, jauge gelée à sa valeur (héros, relevable plus tard).</summary>
    Downed,

    /// <summary>Retirée de la grille (ennemi vaincu).</summary>
    Removed,
}

public enum UnitStatus
{
    Active,
    Downed,
    Removed,
}

/// <summary>Description d'une unité au début du combat. La liste des compétences est copiée.</summary>
/// <param name="Team">Les unités d'une même équipe sont alliées, les autres ennemies.</param>
/// <param name="Initiative">Points de jauge gagnés par tick.</param>
/// <param name="Mobility">Nombre maximal de cases par déplacement.</param>
/// <param name="InitialGauge">Jauge au début du combat, entre 0 et la capacité.</param>
public sealed record UnitSetup(
    UnitId Id,
    int Team,
    Cell Position,
    int Initiative,
    int Mobility,
    int MaxHp,
    IReadOnlyList<SkillDefinition> Skills,
    DefeatOutcome OnDefeat,
    int InitialGauge = 0)
{
    private readonly IReadOnlyList<SkillDefinition> _skills = CombatSetup.ReadOnlyCopy(Skills);

    public IReadOnlyList<SkillDefinition> Skills
    {
        get => _skills;
        init => _skills = CombatSetup.ReadOnlyCopy(value);
    }
}

/// <summary>État courant d'une unité. Seule la <see cref="CombatSimulation"/> le modifie.</summary>
public sealed class Unit
{
    private readonly int _gaugeCapacity;
    private readonly Dictionary<string, int> _cooldownEndTicks = new();
    private readonly SortedDictionary<StatusKind, int> _statusEndTicks = new();

    internal Unit(UnitSetup setup, int gaugeCapacity)
    {
        Id = setup.Id;
        Team = setup.Team;
        Position = setup.Position;
        Initiative = setup.Initiative;
        Mobility = setup.Mobility;
        MaxHp = setup.MaxHp;
        Hp = setup.MaxHp;
        Skills = setup.Skills;
        OnDefeat = setup.OnDefeat;
        Gauge = setup.InitialGauge;
        _gaugeCapacity = gaugeCapacity;
        StatusEndTicks = _statusEndTicks.AsReadOnly();
    }

    public UnitId Id { get; }
    public int Team { get; }
    public Cell Position { get; internal set; }
    public int Initiative { get; }
    public int Mobility { get; }
    public int MaxHp { get; }
    public int Hp { get; internal set; }
    public IReadOnlyList<SkillDefinition> Skills { get; }
    public DefeatOutcome OnDefeat { get; }
    public UnitStatus Status { get; internal set; } = UnitStatus.Active;
    public int Gauge { get; internal set; }

    public bool IsGaugeFull => Gauge >= _gaugeCapacity;

    /// <summary>
    /// Peut agir : debout, jauge pleine et pas étourdie. Le reste tant qu'elle n'a pas agi.
    /// </summary>
    public bool IsReady => Status == UnitStatus.Active && IsGaugeFull && !HasStatus(StatusKind.Stunned);

    /// <summary>États en cours, avec le tick où chacun se termine (ordre fixe).</summary>
    public IReadOnlyDictionary<StatusKind, int> StatusEndTicks { get; }

    public bool HasStatus(StatusKind status) => _statusEndTicks.ContainsKey(status);

    /// <summary>
    /// Reste de la division du remplissage ralenti (en centièmes de point de jauge), reporté au tick suivant
    /// pour que le ralentissement soit exact en moyenne, même avec une petite initiative.
    /// </summary>
    internal int FillRemainder { get; set; }

    internal void SetStatus(StatusKind status, int endTick) => _statusEndTicks[status] = endTick;

    internal void ClearStatus(StatusKind status) => _statusEndTicks.Remove(status);

    public SkillDefinition? FindSkill(string skillId) => Skills.FirstOrDefault(s => s.Id == skillId);

    /// <summary>Tick à partir duquel la compétence est de nouveau utilisable (0 si jamais utilisée).</summary>
    public int CooldownEndTick(string skillId) => _cooldownEndTicks.GetValueOrDefault(skillId);

    internal void StartCooldown(string skillId, int endTick) => _cooldownEndTicks[skillId] = endTick;
}
