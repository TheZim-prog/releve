namespace Game.Core.Combat;

/// <summary>
/// Chaîne (données) : après sa première cible, le coup rebondit sur l'ennemi le plus proche pas encore touché, à
/// <see cref="BounceRange"/> cases au plus du précédent, et ainsi de suite jusqu'à <see cref="MaxTargets"/> cibles.
/// Une chaîne à pied (une ruée) emmène le lanceur au contact de chaque cible ; sinon, le coup saute d'une cible à
/// l'autre (ex. Chaîne d'éclairs).
/// </summary>
public sealed record ChainDefinition
{
    /// <summary>Nombre de cibles au plus, la première comprise (au moins 2).</summary>
    public required int MaxTargets { get; init; }

    /// <summary>
    /// Distance en cases au plus entre deux cibles ; à pied, c'est aussi le nombre de pas au plus pour aller au contact
    /// de la suivante.
    /// </summary>
    public required int BounceRange { get; init; }

    public void Validate(string skillId)
    {
        if (MaxTargets < 2)
            throw new ArgumentException($"Skill '{skillId}': a chain hits at least 2 targets.");
        if (BounceRange < 1)
            throw new ArgumentException($"Skill '{skillId}': a chain needs a bounce range of at least 1.");
    }
}
