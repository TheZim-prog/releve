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

    /// <summary>
    /// Compétence offensive : vise un ennemi ou une zone qui n'est pas réservée aux alliés. Seules celles-ci posent
    /// des états nuisibles, sont bloquées par un dôme et sont utilisées par l'IA de base pour attaquer.
    /// </summary>
    public bool IsOffensive => Target == SkillTargeting.Enemy && Zone?.Affects != ZoneTargets.Allies;

    /// <summary>Cible d'une compétence sans zone : un ennemi (par défaut), un allié, ou le lanceur lui-même (portée 0).</summary>
    public SkillTargeting Target { get; init; } = SkillTargeting.Enemy;

    /// <summary>Part de la jauge (en % de la capacité) remplie instantanément chez la cible (0 = aucune).</summary>
    public int GaugeGainPercent { get; init; }

    /// <summary>Part de la jauge (en % de la capacité) que perd chaque ennemi touché (0 = aucune) ; moins sur un boss.</summary>
    public int GaugeLossPercent { get; init; }

    /// <summary>Le coup interrompt l'attaque que sa cible est en train de charger (ex. Contretemps).</summary>
    public bool InterruptsCharge { get; init; }

    /// <summary>Déplacement forcé de la cible, après les dégâts et les états (facultatif).</summary>
    public ForcedMoveDefinition? ForcedMove { get; init; }

    /// <summary>Soin : PV rendus à chaque cible debout, sans dépasser son maximum (0 = pas de soin).</summary>
    public int MinHeal { get; init; }

    public int MaxHeal { get; init; }

    public bool Heals => MaxHeal > 0;

    /// <summary>
    /// Provocation : chaque ennemi touché doit viser le lanceur pendant ce nombre de ses prochaines activations
    /// (0 = pas de provocation).
    /// </summary>
    public int TauntActions { get; init; }

    /// <summary>PV temporaires donnés à chaque cible (soutien), ou <c>null</c>.</summary>
    public TempHpDefinition? TempHp { get; init; }

    /// <summary>Le coup ignore les protections de sa cible : ni dôme ni PV temporaires ne l'arrêtent (ex. Frappe précise).</summary>
    public bool IgnoresProtections { get; init; }

    /// <summary>
    /// Prise en tenaille : dégâts en % des dégâts normaux quand un allié du lanceur est lui aussi au contact de la
    /// cible (0 = pas de bonus).
    /// </summary>
    public int FlankDamagePercent { get; init; }

    /// <summary>
    /// Sauvetage (ex. Sauvetage éclair) : vise un allié <b>à terre</b> à <see cref="Range"/> cases à pied et une case
    /// libre à ce rayon au plus du lanceur, en vue ; l'allié y est posé, le lanceur finit à côté. <c>null</c> : pas un sauvetage.
    /// </summary>
    public int? RescueDropRadius { get; init; }

    public bool IsRescue => RescueDropRadius is not null;

    /// <summary>
    /// Transport : le « sauvetage » emmène un allié <b>debout</b> (pas à terre, pas un civil), sans rien changer à ses
    /// chutes ; mêmes règles de dépôt.
    /// </summary>
    public bool CarriesStandingAlly { get; init; }

    /// <summary>
    /// Le lanceur se déplace en l'utilisant (mouvement spécial, sauvetage, permutation) : impossible quand il est immobilisé.
    /// </summary>
    public bool MovesCaster => SelfMove is not null || IsRescue || ForcedMove?.Type == ForcedMoveType.Swap || SummonLeftBehind;

    /// <summary>Pouvoir mental (Paralysie, Confusion, Domination) : les robots y sont insensibles.</summary>
    public bool IsMental { get; init; }

    /// <summary>Mouvement spécial du lanceur (Percée, Saut, Charge, Ruée…), ou <c>null</c>.</summary>
    public SelfMoveType? SelfMove { get; init; }

    /// <summary>Garde donnée au lanceur (Parade, Déviation, Maître d'armes), ou <c>null</c>.</summary>
    public GuardDefinition? Guard { get; init; }

    /// <summary>Surveillance d'une zone ou d'un allié (Tir de couverture, Protection rapprochée), ou <c>null</c>.</summary>
    public WatchDefinition? Watch { get; init; }

    /// <summary>Chaîne : le coup rebondit sur les ennemis proches (ex. Frappe en chaîne), ou <c>null</c>.</summary>
    public ChainDefinition? Chain { get; init; }

    /// <summary>Réglages du mouvement spécial (facultatifs).</summary>
    public SelfMoveOptions? SelfMoveOptions { get; init; }

    /// <summary>Les réglages du mouvement spécial, ou ceux par défaut.</summary>
    internal SelfMoveOptions MoveOptions => SelfMoveOptions ?? Combat.SelfMoveOptions.None;

    /// <summary>
    /// Invocation (ex. tourelle, balise) : la compétence vise une case libre à portée et en vue, et y pose une unité
    /// de ce type, de l'équipe du lanceur. Elle remplace celle du même type déjà posée par le même lanceur.
    /// </summary>
    public UnitDefinition? Summon { get; init; }

    /// <summary>
    /// L'invocation est laissée derrière soi (ex. Image rémanente) : la case visée est celle où le lanceur va, à pied
    /// (<see cref="Range"/> pas au plus, sans attaque d'opportunité) ; l'unité apparaît sur la case qu'il quitte.
    /// </summary>
    public bool SummonLeftBehind { get; init; }

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

    /// <summary>Zone laissée au sol sur les cases de la compétence (fumée, pare-tirs, verglas, piège), ou <c>null</c>.</summary>
    public GroundZoneDefinition? Ground { get; init; }

    /// <summary>Obstacle dressé pour un temps sur chaque case libre de la zone (ex. Mur de glace), ou <c>null</c>.</summary>
    public RaisedObstacleDefinition? Raises { get; init; }

    /// <summary>La zone dissipe les écrans de fumée qu'elle touche (ex. Tourbillon).</summary>
    public bool ClearsSmoke { get; init; }

    /// <summary>
    /// La compétence fait quelque chose aux unités qu'elle touche (dégâts, soin, état, jauge, déplacement…). Faux pour
    /// celles qui n'agissent que sur le terrain (fumée, mur de glace) : elles ne « touchent » personne.
    /// </summary>
    public bool HasHitEffect =>
        DealsDamage || Effects.Count > 0 || GaugeGainPercent > 0 || ForcedMove is not null || Heals || TempHp is not null
        || TauntActions > 0 || GaugeLossPercent > 0 || InterruptsCharge
        || VsRobots is { DealsDamage: true } or { Effects.Count: > 0 } or { GaugeLossPercent: > 0 };

    /// <summary>Nom affiché (facultatif ; à défaut, l'identifiant).</summary>
    public string? Name { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;

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
        // Un carré peut être centré sur le lanceur (portée 0) ; une ligne ou un mur visent une case voisine au moins ;
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
        // Seule une projection va avec une zone, centrée sur le lanceur : chaque unité touchée est repoussée loin de lui.
        if (ForcedMove is not null && Zone is not null
            && (ForcedMove.Type != ForcedMoveType.Push || !IsOffensive || Zone.Shape != ZoneShape.Square || Range != 0))
            throw new ArgumentException($"Skill '{Id}': only a push goes with a zone, an offensive square around the caster (range 0).");
        if (ForcedMove is { IsLinear: true } && Target != SkillTargeting.Enemy)
            throw new ArgumentException($"Skill '{Id}': a push or a throw targets an enemy.");
        // Un renversement passe la cible par-dessus le lanceur : elle est à son contact.
        if (ForcedMove?.Type == ForcedMoveType.Throw && (Range != 1 || SelfMove is not null))
            throw new ArgumentException($"Skill '{Id}': a throw targets an adjacent enemy (range 1), without a self move.");
        if (ForcedMove?.Type is ForcedMoveType.Place or ForcedMoveType.Swap && IsCharged)
            throw new ArgumentException($"Skill '{Id}': a placement or a swap cannot be charged.");
        if (ForcedMove?.Type == ForcedMoveType.Swap && Target != SkillTargeting.Ally)
            throw new ArgumentException($"Skill '{Id}': a swap targets an ally.");
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

        // Perte de jauge, interruption de charge : sur des ennemis.
        if (GaugeLossPercent is < 0 or > 100 || ((GaugeLossPercent > 0 || InterruptsCharge) && !IsOffensive))
            throw new ArgumentException($"Skill '{Id}': a gauge loss (0 to 100 %) or a charge interruption only hits enemies.");

        // Provocation : sur des ennemis.
        if (TauntActions is < 0 or > 10 || (TauntActions > 0 && (!IsOffensive || Zone?.Affects == ZoneTargets.All)))
            throw new ArgumentException($"Skill '{Id}': a taunt lasts 1 to 10 actions and only hits enemies.");

        // PV temporaires : un soutien (sur un allié, sur soi, ou une zone réservée aux alliés), jamais une attaque.
        TempHp?.Validate(Id);
        if (TempHp is not null && (IsOffensive || Target == SkillTargeting.Any || IsRescue || Summon is not null))
            throw new ArgumentException($"Skill '{Id}': temporary HP go to an ally, the caster, or an allies-only zone.");
        // Ignorer les protections, ou frapper plus fort en tenaille : un coup qui fait des dégâts à un seul ennemi.
        if ((IgnoresProtections || FlankDamagePercent != 0)
            && (!IsOffensive || Zone is not null || !DealsDamage || SelfMove is SelfMoveType.Pierce or SelfMoveType.Leap))
            throw new ArgumentException($"Skill '{Id}': ignoring protections or flanking belongs to a damaging attack on one enemy.");
        if (FlankDamagePercent != 0 && (FlankDamagePercent is < 100 or > 1000 || Range != 1))
            throw new ArgumentException($"Skill '{Id}': a flank bonus is between 100 and 1000 %, on an attack at range 1.");

        // Invocation : une case visée, rien d'autre ; l'unité posée agit seule (IA, immobile ou non), sans attaque d'opportunité.
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
            if (Summon.Behavior is null || Summon.BasicAttack is not null || Summon.OnDefeat != DefeatOutcome.Removed)
                throw new ArgumentException(
                    $"Skill '{Id}': a summoned unit acts on its own, makes no opportunity attack and is removed when destroyed.");
        }

        if (SummonLeftBehind && Summon is null)
            throw new ArgumentException($"Skill '{Id}': only a summon can be left behind.");

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
            if (Target != SkillTargeting.Enemy || IsCharged || Summon is not null || CreatesDome
                || Heals || GaugeGainPercent > 0 || Range < 1)
                throw new ArgumentException($"Skill '{Id}': a self move is an instant attack in range, without summon or support.");
            // Seuls une ruée ou un bond, qui finissent au contact de leur cible, peuvent ensuite la repousser.
            if (ForcedMove is not null
                && (ForcedMove.Type != ForcedMoveType.Push || selfMove is not (SelfMoveType.Dash or SelfMoveType.Pounce)))
                throw new ArgumentException($"Skill '{Id}': only a dash or a pounce can also push its target.");
            // La cible repoussée pourrait finir sur la case où il doit revenir.
            if (ForcedMove is not null && MoveOptions.ReturnsToStart)
                throw new ArgumentException($"Skill '{Id}': a dash that returns to its start does not push its target.");
            if ((selfMove == SelfMoveType.Leap) != (Zone?.Shape == ZoneShape.Square)
                || (selfMove != SelfMoveType.Leap && Zone is not null))
                throw new ArgumentException($"Skill '{Id}': a leap needs a square zone, the other self moves none.");
            // Le coup d'un repli est un coup au contact.
            if (selfMove == SelfMoveType.Retreat && Range != 1)
                throw new ArgumentException($"Skill '{Id}': a retreat strikes an adjacent enemy (range 1).");
            MoveOptions.Validate(Id, selfMove);
        }
        else if (SelfMoveOptions is not null)
        {
            throw new ArgumentException($"Skill '{Id}' has self move options but no self move.");
        }

        // Sauvetage : sur un allié (à terre), rien d'autre (zone, charge et dégâts sont déjà exclus pour un allié).
        if (IsRescue && (Target != SkillTargeting.Ally || RescueDropRadius < 1 || Heals || Effects.Count > 0
                         || GaugeGainPercent > 0 || ForcedMove is not null || SelfMove is not null || Summon is not null || CreatesDome))
            throw new ArgumentException($"Skill '{Id}': a rescue targets a downed ally, drops it within a radius of at least 1, and does nothing else.");
        if (CarriesStandingAlly && !IsRescue)
            throw new ArgumentException($"Skill '{Id}': carrying a standing ally needs a drop radius (rescueDropRadius).");

        // Contre les robots : un coup offensif (un robot n'est jamais un allié visé par du soutien), pas mental (ils y
        // sont insensibles).
        VsRobots?.Validate(Id);
        if (VsRobots is not null && (!IsOffensive || IsMental || CreatesDome || Summon is not null))
            throw new ArgumentException($"Skill '{Id}': a robot bonus belongs to an offensive, non-mental attack.");
        // Un bonus qui ne change rien est une erreur de données : des états à allonger, ou des dégâts.
        if (VsRobots is not null && !VsRobots.DealsDamage && VsRobots.Effects.Count == 0 && VsRobots.GaugeLossPercent == 0
            && (VsRobots.StatusDurationPercent == 100 || Effects.Count == 0))
            throw new ArgumentException($"Skill '{Id}': its robot bonus has no effect (no damage, no status of its own or to lengthen).");
        // Un état que la compétence pose déjà à tout le monde se rallonge (statusDurationPercent), il ne se répète pas.
        if (VsRobots is not null && VsRobots.Effects.Any(r => Effects.Any(e => e.Status == r.Status)))
            throw new ArgumentException($"Skill '{Id}': a robot effect repeats a status of the skill (lengthen it instead).");

        // Chaîne : une attaque instantanée sur un ennemi, sans zone ; à pied (une ruée) ou à distance.
        Chain?.Validate(Id);
        if (Chain is not null
            && (Target != SkillTargeting.Enemy || Zone is not null || IsCharged || ForcedMove is not null || Summon is not null
                || SelfMove is not (null or SelfMoveType.Dash) || MoveOptions.DamagePercentPerCell != 0
                || ExecuteAtOrBelowPercent > 0 || FlankDamagePercent != 0 || (!DealsDamage && Effects.Count == 0)
                || IsMental || Range < 1))
            throw new ArgumentException($"Skill '{Id}': a chain is an instant, non-mental attack on enemies in range, on foot (dash) or from afar, without zone or forced move.");

        // Sur le terrain : une zone au sol, un obstacle dressé, la fumée dissipée. Toujours sur les cases d'une zone.
        Ground?.Validate(Id);
        Raises?.Validate(Id);
        if ((Ground is not null || Raises is not null || ClearsSmoke)
            && (Zone is null || !IsOffensive || SelfMove is not null || CreatesDome))
            throw new ArgumentException($"Skill '{Id}': what a skill leaves on the ground needs a zone, without self move or dome.");
        // Un piège : une seule case, posée tout de suite (une case choisie pendant une charge pourrait se remplir).
        if (Ground is { IsTrap: true } && (Zone is not { Shape: ZoneShape.Square, Size: 0 } || IsCharged || Range < 1))
            throw new ArgumentException($"Skill '{Id}': a trap is laid at once on a single cell in range.");
        if (Raises is not null && (HasHitEffect || Ground is not null || ClearsSmoke || IsCharged || Range < 1))
            throw new ArgumentException($"Skill '{Id}': raising obstacles is instant, in range, and does nothing else.");

        // Garde : pour le lanceur, posée par une compétence sur lui ou par une zone autour de lui (ex. Maître d'armes).
        Guard?.Validate(Id);
        if (Guard is not null && (IsCharged || !(Target == SkillTargeting.Self || (Zone is not null && Range == 0 && SelfMove is null))))
            throw new ArgumentException($"Skill '{Id}': a guard is raised at once by a skill on its caster, or by a zone around it.");
        // Surveillance : une zone (offensive) ou un allié, et rien d'autre ; c'est le tir qui fait l'effet.
        Watch?.Validate(Id);
        if (Watch is not null
            && (IsCharged || HasHitEffect || Guard is not null || Ground is not null || Raises is not null || ClearsSmoke || CreatesDome
                || Summon is not null || SelfMove is not null || IsRescue || Chain is not null
                || !(Zone is { Shape: ZoneShape.Square } && IsOffensive || Zone is null && Target == SkillTargeting.Ally)))
            throw new ArgumentException($"Skill '{Id}': a watch covers a square zone or an ally, at once, and does nothing else.");

        if (!HasHitEffect && !CreatesDome && Summon is null && !IsRescue && Ground is null && Raises is null && Guard is null && Watch is null)
            throw new ArgumentException($"Skill '{Id}' has no effect (no damage, status, gauge gain, forced move or ground effect).");
    }
}
