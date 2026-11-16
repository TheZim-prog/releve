namespace Game.Core.Combat;

/// <summary>
/// Effet renforcé d'une compétence sur les robots (données ; ex. l'IEM, dévastatrice contre eux) : ses états y
/// durent plus longtemps, elle leur inflige des dégâts en plus, et peut leur poser des états qu'elle ne pose à
/// personne d'autre (ex. Démontage : un simple coup, sauf contre un robot, qu'il étourdit).
/// </summary>
public sealed record RobotBonusDefinition
{
    /// <summary>Durée des états de la compétence sur un robot, en % de la normale (100 = inchangée).</summary>
    public required int StatusDurationPercent { get; init; }

    /// <summary>Dégâts ajoutés au coup sur un robot (0 = aucun).</summary>
    public int MinDamage { get; init; }

    public int MaxDamage { get; init; }

    public bool DealsDamage => MaxDamage > 0;

    /// <summary>Jauge perdue en plus par un robot, en % de la capacité (0 = aucune).</summary>
    public int GaugeLossPercent { get; init; }

    private readonly IReadOnlyList<StatusEffect> _effects = Array.Empty<StatusEffect>();

    /// <summary>
    /// États posés sur un robot seulement, en plus de ceux de la compétence (facultatif), pour la durée écrite ici
    /// (<see cref="StatusDurationPercent"/> ne les concerne pas). Liste copiée.
    /// </summary>
    public IReadOnlyList<StatusEffect> Effects
    {
        get => _effects;
        init => _effects = value is null ? null! : CombatSetup.ReadOnlyCopy(value);
    }

    /// <summary>
    /// Les états du robot (<see cref="Effects"/>) remplacent ceux de la compétence au lieu de s'y ajouter (ex. Piratage :
    /// un robot est commandé, un autre ennemi seulement désarmé).
    /// </summary>
    public bool ReplacesEffects { get; init; }

    public void Validate(string skillId)
    {
        if (StatusDurationPercent is < 100 or > 1000)
            throw new ArgumentException($"Skill '{skillId}' needs a robot status duration between 100 and 1000 %.");
        if (MinDamage < 0 || MaxDamage < MinDamage)
            throw new ArgumentException($"Skill '{skillId}' needs 0 <= robot minDamage <= robot maxDamage.");
        if (GaugeLossPercent is < 0 or > 100)
            throw new ArgumentException($"Skill '{skillId}' needs a robot gauge loss between 0 and 100 %.");
        // Confus ou dominé sont permis : un robot piraté, pas un pouvoir mental (la compétence n'en est pas un, voir SkillDefinition).
        StatusEffect.ValidateAgainst(Effects, skillId, "robot");
        if (ReplacesEffects && Effects.Count == 0)
            throw new ArgumentException($"Skill '{skillId}': robot effects that replace the skill's need at least one status.");
    }
}
