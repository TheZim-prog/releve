namespace Game.Core.Combat;

/// <summary>
/// Ce qu'un coup fait à une cible donnée, une fois réglés les cas particuliers de la cible, en un seul endroit :
/// un robot adverse reçoit l'effet renforcé de la compétence (dégâts et états en plus, états plus longs, ou ses états à
/// la place : Piratage) ; un boss qu'une domination ne peut pas prendre est secoué (ses dégâts et ses états à la place
/// de ceux de la compétence). Une domination n'est jamais un coup renforcé contre un robot (pouvoir mental).
/// </summary>
/// <param name="Damage">Fourchette de dégâts du coup, ou <c>null</c>.</param>
/// <param name="ExtraDamage">Dégâts tirés en plus (contre un robot), ou <c>null</c>.</param>
/// <param name="Effects">États du coup, posés pour <paramref name="DurationPercent"/> % de leur durée.</param>
/// <param name="ExtraEffects">États posés en plus (contre un robot), pour leur durée écrite.</param>
/// <param name="GaugeLossPercent">Jauge que perd un adversaire touché, en % de la capacité.</param>
internal readonly record struct HitProfile(
    (int Min, int Max)? Damage,
    (int Min, int Max)? ExtraDamage,
    IReadOnlyList<StatusEffect> Effects,
    int DurationPercent,
    IReadOnlyList<StatusEffect> ExtraEffects,
    int GaugeLossPercent)
{
    public bool DealsDamage => Damage is not null || ExtraDamage is not null;

    /// <param name="robot">L'effet renforcé de la compétence, si la cible est un robot adverse.</param>
    /// <param name="boss">Ce qu'elle fait à un boss, si la cible en est un.</param>
    public static HitProfile For(SkillDefinition skill, RobotBonusDefinition? robot, BossShakeDefinition? boss)
    {
        if (boss is not null)
        {
            return new HitProfile(boss.DealsDamage ? (boss.MinDamage, boss.MaxDamage) : null, null, boss.Effects, 100,
                Array.Empty<StatusEffect>(), skill.GaugeLossPercent);
        }
        return new HitProfile(
            skill.DealsDamage ? (skill.MinDamage, skill.MaxDamage) : null,
            robot is { DealsDamage: true } ? (robot.MinDamage, robot.MaxDamage) : null,
            robot is { ReplacesEffects: true } ? Array.Empty<StatusEffect>() : skill.Effects,
            robot?.StatusDurationPercent ?? 100,
            robot?.Effects ?? Array.Empty<StatusEffect>(),
            skill.GaugeLossPercent + (robot?.GaugeLossPercent ?? 0));
    }
}
