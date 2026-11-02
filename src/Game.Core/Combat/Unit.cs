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

/// <summary>
/// Une unité au début du combat : son type (<paramref name="Definition"/>, issu des données) et son placement.
/// </summary>
/// <param name="Team">Les unités d'une même équipe sont alliées, les autres ennemies.</param>
/// <param name="InitialGauge">Jauge au début du combat, entre 0 et la capacité.</param>
public sealed record UnitSetup(UnitId Id, int Team, Cell Position, UnitDefinition Definition, int InitialGauge = 0);

/// <summary>
/// Intention d'une unité IA, fixée avant sa jauge pleine : la compétence prévue (<c>null</c> si aucune n'est
/// disponible : elle se rapprochera seulement) et sa cible. Cachée par défaut côté joueurs.
/// </summary>
public sealed record Intention(string? SkillId, UnitId Target);

/// <summary>
/// Attaque en cours de charge : cible (attaque ciblée) ou cases fixées au sol (zone), et tick de déclenchement.
/// Exactement l'un de <paramref name="Target"/> et <paramref name="Zone"/> est renseigné.
/// </summary>
public sealed record PendingCharge(SkillDefinition Skill, UnitId? Target, IReadOnlyList<Cell>? Zone, int ReleaseTick);

/// <summary>État courant d'une unité. Seule la <see cref="CombatSimulation"/> le modifie.</summary>
public sealed class Unit
{
    private readonly int _gaugeCapacity;
    private readonly Dictionary<string, int> _cooldownEndTicks = new();
    private readonly SortedDictionary<StatusKind, int> _statusEndTicks = new();

    internal Unit(UnitSetup setup, int gaugeCapacity)
    {
        var definition = setup.Definition;
        Id = setup.Id;
        Team = setup.Team;
        Position = setup.Position;
        Definition = definition;
        Initiative = definition.Initiative;
        Mobility = definition.Mobility;
        MaxHp = definition.MaxHp;
        Hp = definition.MaxHp;
        Skills = definition.Skills;
        OnDefeat = definition.OnDefeat;
        Gauge = setup.InitialGauge;
        BasicAttack = definition.BasicAttack is null ? null : FindSkill(definition.BasicAttack);
        OpportunityAttacksPerCycle = definition.OpportunityAttacksPerCycle;
        OpportunityAttacksLeft = definition.OpportunityAttacksPerCycle;
        Behavior = definition.Behavior;
        _gaugeCapacity = gaugeCapacity;
        StatusEndTicks = _statusEndTicks.AsReadOnly();
    }

    public UnitId Id { get; }
    public int Team { get; }
    public Cell Position { get; internal set; }

    /// <summary>Type de l'unité (données) : nom, stats et compétences de départ.</summary>
    public UnitDefinition Definition { get; }

    public int Initiative { get; }
    public int Mobility { get; }
    public int MaxHp { get; }
    public int Hp { get; internal set; }
    public IReadOnlyList<SkillDefinition> Skills { get; }
    public DefeatOutcome OnDefeat { get; }
    public UnitStatus Status { get; internal set; } = UnitStatus.Active;
    public int Gauge { get; internal set; }

    /// <summary>Attaque utilisée pour les attaques d'opportunité (<c>null</c> : aucune).</summary>
    public SkillDefinition? BasicAttack { get; }

    public int OpportunityAttacksPerCycle { get; }

    /// <summary>Attaques d'opportunité restantes ; rechargées quand l'unité rejoue.</summary>
    public int OpportunityAttacksLeft { get; internal set; }

    public bool IsGaugeFull => Gauge >= _gaugeCapacity;

    /// <summary>
    /// Peut agir : debout, jauge pleine, pas étourdie, pas en train de charger. Le reste tant qu'elle n'a pas agi.
    /// </summary>
    public bool IsReady => Status == UnitStatus.Active && IsGaugeFull && !HasStatus(StatusKind.Stunned) && Charge is null;

    /// <summary>Attaque en cours de charge, ou <c>null</c>.</summary>
    public PendingCharge? Charge { get; internal set; }

    /// <summary>Comportement d'IA, ou <c>null</c> pour une unité commandée par un joueur.</summary>
    public string? Behavior { get; }

    /// <summary>Intention en cours (unités IA), ou <c>null</c>.</summary>
    public Intention? Intention { get; internal set; }

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
