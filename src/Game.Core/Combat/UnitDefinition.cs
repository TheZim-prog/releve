using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>
/// Type d'unité décrit en données (un héros, un ennemi), avec ses compétences. Un combat en place des instances
/// sur la grille avec <see cref="ToSetup"/>.
/// </summary>
public sealed record UnitDefinition
{
    private readonly IReadOnlyList<SkillDefinition> _skills = Array.Empty<SkillDefinition>();

    public required string Id { get; init; }

    /// <summary>Nom affiché.</summary>
    public required string Name { get; init; }

    public required int MaxHp { get; init; }

    /// <summary>Points de jauge gagnés par tick.</summary>
    public required int Initiative { get; init; }

    /// <summary>Nombre maximal de cases par déplacement.</summary>
    public required int Mobility { get; init; }

    public required DefeatOutcome OnDefeat { get; init; }

    /// <summary>
    /// Identifiant de l'attaque de base parmi <see cref="Skills"/>, utilisée pour les attaques d'opportunité
    /// (à écrire dans les données, même <c>null</c> : l'unité n'en fait alors pas).
    /// </summary>
    public required string? BasicAttack { get; init; }

    /// <summary>Attaques d'opportunité disponibles entre deux activations.</summary>
    public required int OpportunityAttacksPerCycle { get; init; }

    /// <summary>Comportement d'IA (<see cref="Behaviors"/>), ou <c>null</c> pour une unité jouée (à écrire explicitement).</summary>
    public required string? Behavior { get; init; }

    /// <summary>Compétences (liste copiée).</summary>
    public required IReadOnlyList<SkillDefinition> Skills
    {
        get => _skills;
        init => _skills = value is null ? null! : CombatSetup.ReadOnlyCopy(value);
    }

    /// <summary>
    /// Passif équipé, s'il change une règle (facultatif). Un seul emplacement de passif (docs/design.md 2.3).
    /// Pour le prototype, le passif du colosse (plus d'attaques d'opportunité) est porté par ses stats.
    /// </summary>
    public PassiveKind? Passive { get; init; }

    /// <summary>Boss : ne peut pas être exécuté (ni, plus tard, dominé). Faux par défaut.</summary>
    public bool IsBoss { get; init; }

    /// <summary>Instance de cette unité pour un combat.</summary>
    public UnitSetup ToSetup(UnitId id, int team, Cell position, int initialGauge = 0) => new(id, team, position, this, initialGauge);

    public static IReadOnlyList<UnitDefinition> ListFromJson(string json) =>
        DataJson.ValidateList(DataJson.Deserialize<List<UnitDefinition?>>(json), u => u.Validate(), u => u.Id, "unit");

    /// <summary>Vérifie la définition seule (stats, compétences, attaque de base, comportement).</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new ArgumentException("Unit id is required.");
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException($"Unit '{Id}' needs a name.");
        if (Skills is null)
            throw new ArgumentException($"Unit '{Id}' has no skill list.");
        if (Initiative < 0 || Mobility < 0)
            throw new ArgumentException($"Unit '{Id}' has negative initiative or mobility.");
        if (MaxHp <= 0)
            throw new ArgumentException($"Unit '{Id}' needs positive max HP.");
        if (!Enum.IsDefined(OnDefeat))
            throw new ArgumentException($"Unit '{Id}' has an unknown defeat outcome.");
        if (OpportunityAttacksPerCycle < 0)
            throw new ArgumentException($"Unit '{Id}' has a negative number of opportunity attacks.");
        if (Behavior is not null && !Behaviors.IsKnown(Behavior))
            throw new ArgumentException($"Unit '{Id}' has an unknown behavior '{Behavior}'.");
        if (Passive is { } passive && !Enum.IsDefined(passive))
            throw new ArgumentException($"Unit '{Id}' has an unknown passive.");

        try
        {
            SkillDefinition.ValidateAll(Skills);
        }
        catch (ArgumentException e)
        {
            throw new ArgumentException($"Unit '{Id}': {e.Message}", e);
        }

        if (BasicAttack is not null)
        {
            var basic = Skills.FirstOrDefault(s => s.Id == BasicAttack)
                ?? throw new ArgumentException($"Unit '{Id}': basic attack '{BasicAttack}' is not one of its skills.");
            // Elle sert aux attaques d'opportunité, instantanées et sur un seul ennemi.
            if (basic.IsCharged || basic.Zone is not null || basic.Target != SkillTargeting.Enemy || basic.ForcedMove is not null)
                throw new ArgumentException($"Unit '{Id}': basic attack '{basic.Id}' must be instant, on one enemy, without zone or forced move.");
        }
    }

    /// <summary>
    /// Vérifie que la définition est compatible avec les règles du combat (à appeler dès que données et règles
    /// sont chargées ensemble ; la simulation le refait à la mise en place).
    /// </summary>
    public void ValidateAgainst(CombatRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        // Au-delà, la jauge se remplirait en moins d'un tick ; borner évite aussi tout débordement.
        if (Initiative > rules.GaugeCapacity)
            throw new ArgumentException($"Unit '{Id}' has an initiative above the gauge capacity ({rules.GaugeCapacity}).");
    }
}
