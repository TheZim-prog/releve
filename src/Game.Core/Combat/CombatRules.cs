namespace Game.Core.Combat;

/// <summary>
/// Paramètres d'équilibrage du combat, chargés depuis <c>data/combat-rules.json</c>.
/// Toutes les valeurs sont des entiers (simulation déterministe).
/// </summary>
public sealed record CombatRules
{
    /// <summary>Fréquence de la simulation. Sert à convertir les durées en ticks.</summary>
    public required int TicksPerSecond { get; init; }

    /// <summary>Valeur d'une jauge pleine.</summary>
    public required int GaugeCapacity { get; init; }

    /// <summary>Pourcentage de la capacité dont repart la jauge après un déplacement seul.</summary>
    public required int MoveOnlyRestartPercent { get; init; }

    /// <summary>Idem après une action légère (avec ou sans déplacement).</summary>
    public required int LightActionRestartPercent { get; init; }

    /// <summary>Idem après une action lourde (avec ou sans déplacement).</summary>
    public required int HeavyActionRestartPercent { get; init; }

    /// <summary>Vitesse de remplissage de la jauge d'une unité déséquilibrée, en % de la normale.</summary>
    public required int OffBalanceFillPercent { get; init; }

    /// <summary>Vitesse de remplissage de la jauge d'une unité accélérée, en % de la normale (Hypervitesse : 300).</summary>
    public required int HastedFillPercent { get; init; }

    /// <summary>Dégâts infligés par une unité surpuissante, en % des dégâts normaux.</summary>
    public required int EmpoweredDamagePercent { get; init; }

    /// <summary>Dégâts subis par une unité vulnérable, en % des dégâts normaux.</summary>
    public required int ExposedDamageTakenPercent { get; init; }

    /// <summary>Durée de la vulnérabilité posée par le passif Point faible, en millisecondes de combat.</summary>
    public required int WeakPointExposedMs { get; init; }

    /// <summary>Durée des états néfastes posés par une unité qui a le passif Emprise, en % de la durée normale.</summary>
    public required int GripDurationPercent { get; init; }

    /// <summary>Attaques d'opportunité en plus par cycle pour une unité qui a le passif Vigilance.</summary>
    public required int VigilanceExtraOpportunityAttacks { get; init; }

    /// <summary>Remplissage de la jauge (en %) à partir duquel une unité IA fixe son intention.</summary>
    public required int IntentionThresholdPercent { get; init; }

    /// <summary>PV d'un allié à terre relevé (action « interagir »), en % de ses PV max (au moins 1 PV).</summary>
    public required int ReviveHpPercent { get; init; }

    /// <summary>Poids de l'action « relever » (reprise de jauge du releveur, comme une compétence).</summary>
    public required SkillWeight ReviveWeight { get; init; }

    /// <summary>Part des dégâts qu'absorbe un obstacle bas pour l'unité à couvert derrière lui, en %.</summary>
    public required int CoverDamageReductionPercent { get; init; }

    /// <summary>PV d'un obstacle bas : il encaisse les dégâts qu'il absorbe et devient du sol à 0.</summary>
    public required int LowObstacleHp { get; init; }

    /// <summary>Ce qu'ajoute un point de stat mis dans les PV, en % des PV de base du type d'unité.</summary>
    public required int HpPercentPerStatPoint { get; init; }

    /// <summary>Ce qu'ajoute un point de stat mis dans la puissance, en points de % (la puissance de base vaut 100 %).</summary>
    public required int PowerPercentPerStatPoint { get; init; }

    /// <summary>Ce qu'ajoute un point de stat mis dans l'initiative, en % de l'initiative de base du type d'unité.</summary>
    public required int InitiativePercentPerStatPoint { get; init; }

    public static CombatRules FromJson(string json)
    {
        var rules = DataJson.Deserialize<CombatRules>(json);
        rules.Validate();
        return rules;
    }

    /// <summary>Valeur de jauge correspondant à <paramref name="percent"/> % de la capacité.</summary>
    public int GaugeAt(int percent) => (int)((long)GaugeCapacity * percent / 100);

    /// <summary>PV max d'une unité : ceux de son type, augmentés par ses points de stats (arrondi au plus proche).</summary>
    public int MaxHpWith(int baseMaxHp, StatPoints points) => WithPoints(baseMaxHp, HpPercentPerStatPoint, points.Hp);

    /// <summary>Initiative d'une unité : celle de son type, augmentée par ses points de stats (arrondi au plus proche).</summary>
    public int InitiativeWith(int baseInitiative, StatPoints points) =>
        WithPoints(baseInitiative, InitiativePercentPerStatPoint, points.Initiative);

    /// <summary>Puissance d'une unité, en % : 100 sans point, puis ce qu'ajoutent ses points de stats.</summary>
    public int PowerPercentWith(StatPoints points) => 100 + PowerPercentPerStatPoint * points.Power;

    private static int WithPoints(int baseValue, int percentPerPoint, int points) =>
        (int)(((long)baseValue * (100 + (long)percentPerPoint * points) + 50) / 100);

    /// <summary>Durée en ticks, arrondie au tick supérieur.</summary>
    public int MillisecondsToTicks(int milliseconds) =>
        (int)(((long)milliseconds * TicksPerSecond + 999) / 1000);

    public void Validate()
    {
        if (TicksPerSecond <= 0)
            throw new ArgumentException($"{nameof(TicksPerSecond)} must be positive.");
        if (GaugeCapacity <= 0)
            throw new ArgumentException($"{nameof(GaugeCapacity)} must be positive.");
        ValidatePercent(MoveOnlyRestartPercent, nameof(MoveOnlyRestartPercent));
        ValidatePercent(LightActionRestartPercent, nameof(LightActionRestartPercent));
        ValidatePercent(HeavyActionRestartPercent, nameof(HeavyActionRestartPercent));
        ValidatePercent(OffBalanceFillPercent, nameof(OffBalanceFillPercent), max: 100);
        ValidatePercent(IntentionThresholdPercent, nameof(IntentionThresholdPercent), max: 100);
        ValidatePercent(ReviveHpPercent, nameof(ReviveHpPercent), max: 100, min: 1);
        if (!Enum.IsDefined(ReviveWeight))
            throw new ArgumentException($"{nameof(ReviveWeight)} is unknown.");
        ValidatePercent(CoverDamageReductionPercent, nameof(CoverDamageReductionPercent), max: 100); // 0 : pas de couverture
        if (LowObstacleHp <= 0)
            throw new ArgumentException($"{nameof(LowObstacleHp)} must be positive.");
        // Renforcements : au moins 100 % (sinon ce serait un affaiblissement), au plus x10 pour rester raisonnable.
        if (HastedFillPercent is < 100 or > 1000)
            throw new ArgumentException($"{nameof(HastedFillPercent)} must be between 100 and 1000.");
        if (EmpoweredDamagePercent is < 100 or > 1000)
            throw new ArgumentException($"{nameof(EmpoweredDamagePercent)} must be between 100 and 1000.");
        if (ExposedDamageTakenPercent is < 100 or > 1000)
            throw new ArgumentException($"{nameof(ExposedDamageTakenPercent)} must be between 100 and 1000.");
        if (WeakPointExposedMs <= 0)
            throw new ArgumentException($"{nameof(WeakPointExposedMs)} must be positive.");
        if (GripDurationPercent is < 100 or > 1000)
            throw new ArgumentException($"{nameof(GripDurationPercent)} must be between 100 and 1000.");
        if (VigilanceExtraOpportunityAttacks is < 1 or > 10)
            throw new ArgumentException($"{nameof(VigilanceExtraOpportunityAttacks)} must be between 1 and 10.");
        ValidatePercent(HpPercentPerStatPoint, nameof(HpPercentPerStatPoint), max: 100); // 0 : le point n'apporte rien
        ValidatePercent(PowerPercentPerStatPoint, nameof(PowerPercentPerStatPoint), max: 100);
        ValidatePercent(InitiativePercentPerStatPoint, nameof(InitiativePercentPerStatPoint), max: 100);
    }

    // Pour les reprises de jauge, 100 % est exclu (max 99 par défaut) : l'unité serait de nouveau
    // prête aussitôt et pourrait agir plusieurs fois dans le même tick.
    private static void ValidatePercent(int value, string name, int max = 99, int min = 0)
    {
        if (value < min || value > max)
            throw new ArgumentException($"{name} must be between {min} and {max}.");
    }
}
