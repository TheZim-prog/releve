namespace Game.Core.Combat;

/// <summary>
/// Effet renforcé d'une compétence sur les robots (données ; ex. l'IEM, dévastatrice contre eux) : ses états y
/// durent plus longtemps, et elle leur inflige des dégâts en plus.
/// </summary>
public sealed record RobotBonusDefinition
{
    /// <summary>Durée des états de la compétence sur un robot, en % de la normale (100 = inchangée).</summary>
    public required int StatusDurationPercent { get; init; }

    /// <summary>Dégâts ajoutés au coup sur un robot (0 = aucun).</summary>
    public int MinDamage { get; init; }

    public int MaxDamage { get; init; }

    public bool DealsDamage => MaxDamage > 0;

    public void Validate(string skillId)
    {
        if (StatusDurationPercent is < 100 or > 1000)
            throw new ArgumentException($"Skill '{skillId}' needs a robot status duration between 100 and 1000 %.");
        if (MinDamage < 0 || MaxDamage < MinDamage)
            throw new ArgumentException($"Skill '{skillId}' needs 0 <= robot minDamage <= robot maxDamage.");
    }
}
