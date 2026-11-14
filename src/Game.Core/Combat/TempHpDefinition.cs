namespace Game.Core.Combat;

/// <summary>
/// PV temporaires donnés par une compétence de soutien (données) : ils encaissent les dégâts avant les vrais PV, puis
/// disparaissent. Pas de cumul : en recevoir de nouveaux remplace ceux qu'on avait, sauf s'il en restait davantage.
/// Les soins ne les remplissent pas.
/// Le montant est soit une part des PV actuels du lanceur (Peau d'acier, Égide), soit tiré dans une fourchette et
/// multiplié par sa puissance, comme un soin (Balise bouclier).
/// </summary>
public sealed record TempHpDefinition
{
    /// <summary>Montant en % des PV actuels du lanceur au moment du lancement (0 = montant tiré entre <see cref="Min"/> et <see cref="Max"/>).</summary>
    public int PercentOfCasterHp { get; init; }

    public int Min { get; init; }
    public int Max { get; init; }

    /// <summary>Durée en millisecondes de combat, après laquelle ce qu'il en reste disparaît.</summary>
    public required int DurationMs { get; init; }

    public void Validate(string skillId)
    {
        var fromCasterHp = PercentOfCasterHp != 0;
        if (fromCasterHp == (Max != 0 || Min != 0))
            throw new ArgumentException($"Skill '{skillId}': temporary HP are either a percentage of the caster's HP, or a min-max amount.");
        if (fromCasterHp ? PercentOfCasterHp is < 1 or > 1000 : Min < 1 || Max < Min)
            throw new ArgumentException($"Skill '{skillId}' needs a temporary HP percentage between 1 and 1000, or 1 <= min <= max.");
        if (DurationMs <= 0)
            throw new ArgumentException($"Skill '{skillId}': temporary HP need a positive duration.");
    }
}
