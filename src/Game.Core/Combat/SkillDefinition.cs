namespace Game.Core.Combat;

/// <summary>Poids d'une action : détermine d'où repart la jauge (voir <see cref="CombatRules"/>).</summary>
public enum SkillWeight
{
    Light,
    Heavy,
}

/// <summary>Mouvement spécial du lanceur pendant sa compétence (sans attaque d'opportunité).</summary>
public enum SelfMoveType
{
    /// <summary>
    /// Percée : vise une case alignée ; course en ligne droite sur <see cref="SkillDefinition.Range"/> cases à travers
    /// les unités (pas les murs ni les obstacles bas), arrêt sur la dernière case libre, et frappe tout ce qui est traversé.
    /// </summary>
    Pierce,

    /// <summary>Saut : vise une case libre à portée, par-dessus unités et obstacles bas (pas les murs) ; la zone frappe autour de l'atterrissage.</summary>
    Leap,

    /// <summary>Charge : vise un ennemi aligné à portée ; fonce et s'arrête contre la première unité de la ligne, qui prend le coup.</summary>
    Rush,
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

    /// <summary>
    /// Compétence offensive : vise un ennemi ou une zone qui n'est pas réservée aux alliés. Seules celles-ci posent
    /// des états nuisibles, sont bloquées par un dôme et sont utilisées par l'IA de base pour attaquer.
    /// </summary>
    public bool IsOffensive => Target == SkillTargeting.Enemy && Zone?.Affects != ZoneTargets.Allies;

    /// <summary>Cible d'une compétence sans zone : un ennemi (par défaut), un allié, ou le lanceur lui-même (portée 0).</summary>
    public SkillTargeting Target { get; init; } = SkillTargeting.Enemy;

    /// <summary>Part de la jauge (en % de la capacité) remplie instantanément chez la cible (0 = aucune).</summary>
    public int GaugeGainPercent { get; init; }

    /// <summary>Déplacement forcé de la cible, après les dégâts et les états (facultatif).</summary>
    public ForcedMoveDefinition? ForcedMove { get; init; }

    /// <summary>Soin : PV rendus à chaque cible debout, sans dépasser son maximum (0 = pas de soin).</summary>
    public int MinHeal { get; init; }

    public int MaxHeal { get; init; }

    public bool Heals => MaxHeal > 0;

    /// <summary>
    /// Sauvetage (ex. Sauvetage éclair) : vise un allié <b>à terre</b> à <see cref="Range"/> cases à pied et une case
    /// libre à ce rayon au plus du lanceur, en vue ; l'allié y est posé, le lanceur finit à côté. <c>null</c> : pas un sauvetage.
    /// </summary>
    public int? RescueDropRadius { get; init; }

    public bool IsRescue => RescueDropRadius is not null;

    /// <summary>Pouvoir mental (Paralysie, Confusion, Domination) : les robots y sont insensibles.</summary>
    public bool IsMental { get; init; }

    /// <summary>Mouvement spécial du lanceur (Percée, Saut, Charge), ou <c>null</c>.</summary>
    public SelfMoveType? SelfMove { get; init; }

    /// <summary>
    /// Invocation (ex. tourelle, balise) : la compétence vise une case libre à portée et en vue, et y pose une unité
    /// de ce type, de l'équipe du lanceur. Elle remplace celle du même type déjà posée par le même lanceur.
    /// </summary>
    public UnitDefinition? Summon { get; init; }

    /// <summary>
    /// Dôme : au lieu de frapper, la compétence (zone carrée) pose un dôme sur ses cases. Il bloque la prochaine
    /// attaque entière contre l'équipe du lanceur à l'intérieur, et disparaît alors, ou à la prochaine action du lanceur.
    /// </summary>
    public bool CreatesDome { get; init; }

    /// <summary>
    /// Exécution : si les PV de la cible sont au plus à ce % de son maximum, elle est achevée au lieu de prendre les
    /// dégâts normaux (sauf un boss). 0 = pas d'exécution.
    /// </summary>
    public int ExecuteAtOrBelowPercent { get; init; }

    /// <summary>Effet renforcé sur les robots (ex. IEM : états plus longs, dégâts en plus), ou <c>null</c>.</summary>
    public RobotBonusDefinition? VsRobots { get; init; }

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
        if (Effects.Any(e => e.Status.IsHarmful() != IsOffensive))
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

        if (ExecuteAtOrBelowPercent is < 0 or > 99)
            throw new ArgumentException($"Skill '{Id}' needs an execution threshold between 0 and 99 %.");
        if (ExecuteAtOrBelowPercent > 0 && (Target != SkillTargeting.Enemy || Zone is not null || !DealsDamage
                                            || SelfMove is SelfMoveType.Pierce or SelfMoveType.Leap))
            throw new ArgumentException($"Skill '{Id}': an execution is a damaging attack on one enemy.");

        if (CreatesDome && (Zone?.Shape != ZoneShape.Square || IsCharged || DealsDamage || Effects.Count > 0
                            || GaugeGainPercent > 0 || ForcedMove is not null || ExecuteAtOrBelowPercent > 0))
            throw new ArgumentException($"Skill '{Id}': a dome is an instant square zone that does nothing else.");

        // Soin : sur un allié, sur soi, ou dans une zone qui ne touche que les alliés ; jamais avec des dégâts.
        if (MinHeal < 0 || MaxHeal < MinHeal)
            throw new ArgumentException($"Skill '{Id}' needs 0 <= minHeal <= maxHeal.");
        var supportZone = Zone?.Affects == ZoneTargets.Allies;
        if (Heals && (DealsDamage || (Zone is null ? Target is SkillTargeting.Enemy or SkillTargeting.Any : !supportZone)))
            throw new ArgumentException($"Skill '{Id}': a heal targets an ally, its caster, or an allies-only zone, without damage.");
        // (Ses états sont déjà vérifiés bénéfiques plus haut, puisqu'elle n'est pas offensive.)
        if (supportZone && (DealsDamage || CreatesDome))
            throw new ArgumentException($"Skill '{Id}': an allies-only zone can only heal or give beneficial statuses.");

        // Invocation : une case visée, rien d'autre ; l'unité posée est une unité IA immobile, sans attaque d'opportunité.
        if (Summon is not null)
        {
            if (Zone is not null || IsCharged || DealsDamage || Heals || Effects.Count > 0 || GaugeGainPercent > 0
                || ForcedMove is not null || CreatesDome || ExecuteAtOrBelowPercent > 0 || Range < 1)
                throw new ArgumentException($"Skill '{Id}': a summon targets a cell in range and does nothing else.");
            try
            {
                Summon.Validate();
            }
            catch (ArgumentException e)
            {
                throw new ArgumentException($"Skill '{Id}': {e.Message}", e);
            }
            if (Summon.Behavior is null || Summon.Mobility != 0 || Summon.BasicAttack is not null
                || Summon.OnDefeat != DefeatOutcome.Removed)
                throw new ArgumentException(
                    $"Skill '{Id}': a summoned unit acts on its own, does not move, makes no opportunity attack and is removed when destroyed.");
        }

        // Pouvoirs mentaux : offensifs. La domination (un changement de camp) vise une seule unité.
        if (IsMental && !IsOffensive)
            throw new ArgumentException($"Skill '{Id}': a mental power targets enemies.");
        if (!IsMental && Effects.Any(e => e.Status is StatusKind.Confused or StatusKind.Dominated))
            throw new ArgumentException($"Skill '{Id}': confusion and domination are mental powers (isMental).");
        if (Effects.Any(e => e.Status == StatusKind.Dominated) && (Zone is not null || SelfMove is not null))
            throw new ArgumentException($"Skill '{Id}': a domination targets a single unit.");

        // Mouvements spéciaux : offensifs, instantanés, sans autre mouvement ; seul le saut a une zone (carrée).
        if (SelfMove is { } selfMove)
        {
            if (!Enum.IsDefined(selfMove))
                throw new ArgumentException($"Skill '{Id}' has an unknown self move.");
            if (Target != SkillTargeting.Enemy || IsCharged || ForcedMove is not null || Summon is not null || CreatesDome
                || Heals || GaugeGainPercent > 0 || Range < 1)
                throw new ArgumentException($"Skill '{Id}': a self move is an instant attack in range, without other moves or support.");
            if ((selfMove == SelfMoveType.Leap) != (Zone?.Shape == ZoneShape.Square)
                || (selfMove != SelfMoveType.Leap && Zone is not null))
                throw new ArgumentException($"Skill '{Id}': a leap needs a square zone, a pierce or a rush none.");
        }

        // Sauvetage : sur un allié (à terre), rien d'autre (zone, charge et dégâts sont déjà exclus pour un allié).
        if (IsRescue && (Target != SkillTargeting.Ally || RescueDropRadius < 1 || Heals || Effects.Count > 0
                         || GaugeGainPercent > 0 || ForcedMove is not null || SelfMove is not null || Summon is not null || CreatesDome))
            throw new ArgumentException($"Skill '{Id}': a rescue targets a downed ally, drops it within a radius of at least 1, and does nothing else.");

        // Contre les robots : un coup offensif (un robot n'est jamais un allié visé par du soutien), pas mental (ils y
        // sont insensibles).
        VsRobots?.Validate(Id);
        if (VsRobots is not null && (!IsOffensive || IsMental || CreatesDome || Summon is not null))
            throw new ArgumentException($"Skill '{Id}': a robot bonus belongs to an offensive, non-mental attack.");
        // Un bonus qui ne change rien est une erreur de données : des états à allonger, ou des dégâts.
        if (VsRobots is not null && !VsRobots.DealsDamage && (VsRobots.StatusDurationPercent == 100 || Effects.Count == 0))
            throw new ArgumentException($"Skill '{Id}': its robot bonus has no effect (no damage, no status to lengthen).");

        if (!DealsDamage && Effects.Count == 0 && GaugeGainPercent == 0 && ForcedMove is null && !CreatesDome
            && !Heals && Summon is null && !IsRescue && VsRobots is not { DealsDamage: true })
            throw new ArgumentException($"Skill '{Id}' has no effect (no damage, status, gauge gain or forced move).");
    }
}
