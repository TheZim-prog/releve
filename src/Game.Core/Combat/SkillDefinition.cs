namespace Game.Core.Combat;

/// <summary>Poids d'une action : détermine d'où repart la jauge (voir <see cref="CombatRules"/>).</summary>
public enum SkillWeight
{
    Light,
    Heavy,
}

/// <summary>Qui une compétence sans zone peut viser.</summary>
public enum SkillTargeting
{
    Enemy,

    /// <summary>Un allié, jamais le lanceur lui-même (ex. Ordre tactique). Ne fait pas de dégâts.</summary>
    Ally,

    /// <summary>Le lanceur lui-même (ex. Hypervitesse) : pas de cible à donner, portée 0, renforcements seulement.</summary>
    Self,

    /// <summary>
    /// N'importe quelle unité debout sauf le lanceur, alliée ou ennemie (ex. Télékinésie) : déplacement forcé seulement.
    /// </summary>
    Any,
}

/// <summary>
/// Compétence (attaque de base comprise), décrite en données : sur une unité (ennemie, ou alliée pour le
/// soutien) ou sur une zone ; au contact ou à distance (ligne de vue) ; instantanée ou chargée.
/// </summary>
public sealed record SkillDefinition
{
    public required string Id { get; init; }
    public required SkillWeight Weight { get; init; }

    /// <summary>Portée en cases (diagonale = 1). Au-delà de 1, il faut une ligne de vue.</summary>
    public required int Range { get; init; }

    public required int MinDamage { get; init; }
    public required int MaxDamage { get; init; }

    /// <summary>Temps de recharge, en millisecondes de combat (0 = aucun).</summary>
    public required int CooldownMs { get; init; }

    private readonly IReadOnlyList<StatusEffect> _effects = Array.Empty<StatusEffect>();

    /// <summary>États appliqués à la cible si elle est encore debout après les dégâts (facultatif). Liste copiée.</summary>
    public IReadOnlyList<StatusEffect> Effects
    {
        get => _effects;
        init => _effects = value is null ? null! : CombatSetup.ReadOnlyCopy(value);
    }

    /// <summary>Une compétence à 0 dégât maximum n'en inflige pas (contrôle pur) : aucun tirage, aucun événement.</summary>
    public bool DealsDamage => MaxDamage > 0;

    /// <summary>
    /// Durée de charge en millisecondes de combat (0 = instantanée). Pendant la charge, le lanceur est immobilisé.
    /// </summary>
    public int ChargeMs { get; init; }

    /// <summary>Zone d'effet (facultative). Avec une zone, la compétence vise une case ; sinon, une unité (voir <see cref="Target"/>).</summary>
    public ZoneDefinition? Zone { get; init; }

    public bool IsCharged => ChargeMs > 0;

    /// <summary>Cible d'une compétence sans zone : un ennemi (par défaut), un allié, ou le lanceur lui-même (portée 0).</summary>
    public SkillTargeting Target { get; init; } = SkillTargeting.Enemy;

    /// <summary>Part de la jauge (en % de la capacité) remplie instantanément chez la cible (0 = aucune).</summary>
    public int GaugeGainPercent { get; init; }

    /// <summary>Déplacement forcé de la cible, après les dégâts et les états (facultatif).</summary>
    public ForcedMoveDefinition? ForcedMove { get; init; }

    public static IReadOnlyList<SkillDefinition> ListFromJson(string json) =>
        ValidateAll(DataJson.Deserialize<List<SkillDefinition?>>(json));

    /// <summary>Valide chaque compétence de la liste et l'unicité des identifiants.</summary>
    public static IReadOnlyList<SkillDefinition> ValidateAll(IEnumerable<SkillDefinition?> skills) =>
        DataJson.ValidateList(skills, s => s.Validate(), s => s.Id, "skill");

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new ArgumentException("Skill id is required.");
        if (!Enum.IsDefined(Weight))
            throw new ArgumentException($"Skill '{Id}' has an unknown weight.");
        // Un carré peut être centré sur le lanceur (portée 0) ; une ligne part vers une case voisine au moins ;
        // une compétence sur soi a une portée 0 ; une compétence ciblée vise une autre unité.
        var validRange = Target == SkillTargeting.Self ? Range == 0 : Range >= (Zone?.Shape == ZoneShape.Square ? 0 : 1);
        if (!validRange)
            throw new ArgumentException($"Skill '{Id}' has an invalid range {Range}.");
        if (ChargeMs < 0)
            throw new ArgumentException($"Skill '{Id}' has a negative charge time.");
        Zone?.Validate(Id);
        if (MinDamage < 0 || MaxDamage < MinDamage)
            throw new ArgumentException($"Skill '{Id}' needs 0 <= minDamage <= maxDamage.");
        if (CooldownMs < 0)
            throw new ArgumentException($"Skill '{Id}' has a negative cooldown.");
        if (Effects is null || Effects.Any(e => e is null))
            throw new ArgumentException($"Skill '{Id}' has a null effect.");
        foreach (var effect in Effects)
            effect.Validate(Id);
        if (Effects.Select(e => e.Status).Distinct().Count() != Effects.Count)
            throw new ArgumentException($"Skill '{Id}' applies the same status twice.");
        if (!Enum.IsDefined(Target))
            throw new ArgumentException($"Skill '{Id}' has an unknown target kind.");
        if (GaugeGainPercent is < 0 or > 100)
            throw new ArgumentException($"Skill '{Id}' needs a gauge gain between 0 and 100 %.");
        // Sur un allié ou sur soi : pas de zone, pas de charge (la cible changerait d'état avant le déclenchement),
        // pas de dégâts, et seulement des états bénéfiques. Sur un ennemi : seulement des états nuisibles.
        if (Target != SkillTargeting.Enemy && (Zone is not null || IsCharged || DealsDamage))
            throw new ArgumentException($"Skill '{Id}' targets an ally or its caster: no zone, charge or damage allowed.");
        if (Effects.Any(e => e.Status.IsHarmful() != (Target == SkillTargeting.Enemy)))
            throw new ArgumentException($"Skill '{Id}': harmful statuses go on enemies, beneficial ones on allies or self.");
        if (GaugeGainPercent > 0 && Target != SkillTargeting.Ally)
            throw new ArgumentException($"Skill '{Id}': only a skill targeting an ally can fill a gauge.");

        // Déplacements forcés : une projection vise un ennemi ; une télékinésie n'est pas chargée (la cible bougerait
        // avant le déclenchement) ; une compétence sur « n'importe qui » ne fait que déplacer.
        ForcedMove?.Validate(Id);
        if (ForcedMove is not null && Zone is not null)
            throw new ArgumentException($"Skill '{Id}': a forced move needs a single target, not a zone.");
        // Au contact, la direction lanceur → cible est l'une des 8 directions : la poussée va bien en ligne droite.
        if (ForcedMove?.Type == ForcedMoveType.Push && (Target != SkillTargeting.Enemy || Range != 1))
            throw new ArgumentException($"Skill '{Id}': a push targets an adjacent enemy (range 1).");
        if (ForcedMove?.Type == ForcedMoveType.Place && IsCharged)
            throw new ArgumentException($"Skill '{Id}': a placement cannot be charged.");
        if (Target == SkillTargeting.Any && (ForcedMove?.Type != ForcedMoveType.Place || Effects.Count > 0 || GaugeGainPercent > 0))
            throw new ArgumentException($"Skill '{Id}' targets anyone: it can only place its target.");

        if (!DealsDamage && Effects.Count == 0 && GaugeGainPercent == 0 && ForcedMove is null)
            throw new ArgumentException($"Skill '{Id}' has no effect (no damage, status, gauge gain or forced move).");
    }
}
