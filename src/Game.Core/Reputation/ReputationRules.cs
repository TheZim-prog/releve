using Game.Core.Loading;

namespace Game.Core.Reputation;

/// <summary>
/// Ce que vaut chaque fait d'une mission pour la réputation du groupe (docs/design.md 3.4 et 4.4) : des malus pour les
/// dégâts collatéraux, des bonus pour les civils sauvés et les objectifs secondaires réussis. En données
/// (<c>reputation.json</c>).
/// </summary>
public sealed record ReputationRules
{
    /// <summary>Par obstacle bas du décor détruit par les héros (malus, négatif ou nul).</summary>
    public required int SceneryDestroyed { get; init; }

    /// <summary>Par civil blessé par les héros, qui a survécu (malus).</summary>
    public required int CivilianHurtByHeroes { get; init; }

    /// <summary>Par civil perdu par la faute des héros (malus).</summary>
    public required int CivilianLostToHeroes { get; init; }

    /// <summary>Par civil perdu par la faute des ennemis : on n'a pas su le protéger (malus).</summary>
    public required int CivilianLostToEnemies { get; init; }

    /// <summary>Par civil sauvé (évacué, ou hors de danger à la victoire) sans avoir été blessé par les héros (bonus).</summary>
    public required int CivilianSaved { get; init; }

    /// <summary>Par objectif secondaire réussi (bonus ; un secondaire raté ne coûte rien).</summary>
    public required int SecondaryObjectiveCompleted { get; init; }

    /// <summary>L'échelle de la réputation du groupe, qui cumule les bilans.</summary>
    public required ReputationScale Scale { get; init; }

    public static ReputationRules FromJson(string json)
    {
        var rules = DataJson.Deserialize<ReputationRules>(json);
        rules.Validate();
        return rules;
    }

    public void Validate()
    {
        if (SceneryDestroyed > 0 || CivilianHurtByHeroes > 0 || CivilianLostToHeroes > 0 || CivilianLostToEnemies > 0)
            throw new ArgumentException("Reputation penalties (scenery destroyed, civilians hurt or lost) cannot be positive.");
        if (CivilianSaved < 0 || SecondaryObjectiveCompleted < 0)
            throw new ArgumentException("Reputation bonuses (civilians saved, secondary objectives) cannot be negative.");
        if (Scale is null)
            throw new ArgumentException("Reputation rules need a scale.");
        Scale.Validate();
    }
}
