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

    /// <summary>Remplissage de la jauge (en %) à partir duquel une unité IA fixe son intention.</summary>
    public required int IntentionThresholdPercent { get; init; }

    public static CombatRules FromJson(string json)
    {
        var rules = DataJson.Deserialize<CombatRules>(json);
        rules.Validate();
        return rules;
    }

    /// <summary>Valeur de jauge correspondant à <paramref name="percent"/> % de la capacité.</summary>
    public int GaugeAt(int percent) => (int)((long)GaugeCapacity * percent / 100);

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
    }

    // Pour les reprises de jauge, 100 % est exclu (max 99 par défaut) : l'unité serait de nouveau
    // prête aussitôt et pourrait agir plusieurs fois dans le même tick.
    private static void ValidatePercent(int value, string name, int max = 99)
    {
        if (value < 0 || value > max)
            throw new ArgumentException($"{name} must be between 0 and {max}.");
    }
}
