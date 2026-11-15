namespace Game.Core.Combat;

/// <summary>
/// Zone qu'une compétence laisse au sol sur ses cases (données) : un écran de fumée, un pare-tirs, un sol qui pose un
/// état à qui y entre, ou un piège. Elle touche tout le monde, son lanceur compris, sauf réglage contraire.
/// </summary>
public sealed record GroundZoneDefinition
{
    /// <summary>Durée en millisecondes de combat. 0 : jusqu'à son déclenchement (un piège seulement).</summary>
    public int DurationMs { get; init; }

    /// <summary>Fumée : aucune ligne de vue ne passe par ses cases (au contact, on se voit toujours).</summary>
    public bool BlocksSight { get; init; }

    /// <summary>Pare-tirs : arrête les attaques à distance qui passent par ses cases (pas les pouvoirs mentaux ni le soutien).</summary>
    public bool BlocksShots { get; init; }

    /// <summary>Qui la zone touche : tout le monde (par défaut) ou les ennemis de son lanceur. Jamais les seuls alliés.</summary>
    public ZoneTargets Affects { get; init; } = ZoneTargets.All;

    private readonly IReadOnlyList<StatusEffect> _effects = Array.Empty<StatusEffect>();

    /// <summary>États posés à qui entre dans la zone (et à qui s'y trouve quand elle apparaît). Liste copiée.</summary>
    public IReadOnlyList<StatusEffect> Effects
    {
        get => _effects;
        init => _effects = value is null ? null! : CombatSetup.ReadOnlyCopy(value);
    }

    /// <summary>Les états sont reposés tant que l'unité reste dans la zone (ex. Verglas), au lieu d'une seule fois à l'entrée.</summary>
    public bool Renews { get; init; }

    /// <summary>
    /// Piège : une seule case, invisible pour l'IA ; le premier ennemi qui y entre le déclenche (dégâts, états) et le
    /// fait disparaître. Un seul par lanceur : en poser un autre remplace l'ancien.
    /// </summary>
    public bool IsTrap { get; init; }

    /// <summary>Dégâts d'un piège à son déclenchement (0 = aucun).</summary>
    public int MinDamage { get; init; }

    public int MaxDamage { get; init; }

    /// <summary>
    /// Piège explosif : les dégâts touchent toutes les unités de ce carré autour du piège, au lieu de celle qui l'a
    /// déclenché seulement.
    /// </summary>
    public ZoneDefinition? Blast { get; init; }

    public bool DealsDamage => MaxDamage > 0;

    public void Validate(string skillId)
    {
        if (!Enum.IsDefined(Affects) || Affects == ZoneTargets.Allies)
            throw new ArgumentException($"Skill '{skillId}': a ground zone affects everyone or the enemies of its caster.");
        if (DurationMs < 0 || (DurationMs == 0 && !IsTrap))
            throw new ArgumentException($"Skill '{skillId}': a ground zone needs a duration (only a trap can last until triggered).");
        if (Effects is null || Effects.Any(e => e is null))
            throw new ArgumentException($"Skill '{skillId}' has a null ground effect.");
        foreach (var effect in Effects)
            effect.Validate(skillId);
        if (Effects.Select(e => e.Status).Distinct().Count() != Effects.Count)
            throw new ArgumentException($"Skill '{skillId}': its ground zone applies the same status twice.");
        if (Effects.Any(e => e.Status is StatusKind.Confused or StatusKind.Dominated))
            throw new ArgumentException($"Skill '{skillId}': a ground zone cannot confuse or dominate.");
        if (Renews && (Effects.Count == 0 || IsTrap))
            throw new ArgumentException($"Skill '{skillId}': only the statuses of a lasting ground zone can be renewed.");
        if (MinDamage < 0 || MaxDamage < MinDamage)
            throw new ArgumentException($"Skill '{skillId}': its ground zone needs 0 <= minDamage <= maxDamage.");

        if (IsTrap)
        {
            if (Affects != ZoneTargets.Enemies || BlocksSight || BlocksShots || (!DealsDamage && Effects.Count == 0))
                throw new ArgumentException($"Skill '{skillId}': a trap is triggered by enemies, does damage or applies a status, and nothing else.");
            Blast?.Validate(skillId);
            if (Blast is not null && (Blast.Shape != ZoneShape.Square || Blast.Affects != ZoneTargets.All || !DealsDamage))
                throw new ArgumentException($"Skill '{skillId}': the blast of a trap is a damaging square that hits everyone.");
        }
        else if (DealsDamage || Blast is not null)
        {
            throw new ArgumentException($"Skill '{skillId}': only a trap deals damage.");
        }
        else if (!BlocksSight && !BlocksShots && Effects.Count == 0)
        {
            throw new ArgumentException($"Skill '{skillId}': its ground zone does nothing.");
        }
    }
}
