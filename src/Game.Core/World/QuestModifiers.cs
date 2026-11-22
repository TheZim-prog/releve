using Game.Core.Combat;

namespace Game.Core.World;

/// <summary>
/// Un bonus (ou malus) gagné en route pour la quête en cours (un événement) : il vaut pour chaque héros déployé, au
/// début du combat. Une jauge de départ (en % ; la plus haute l'emporte), une armure (PV temporaires qui tiennent tout le
/// combat), et des points de stats en plus ou en moins (un malus n'en retire jamais plus que le héros n'en a).
/// </summary>
public sealed record QuestModifiers
{
    public static readonly QuestModifiers None = new();

    public int StartGaugePercent { get; init; }
    public int StartTempHp { get; init; }
    public int Hp { get; init; }
    public int Power { get; init; }
    public int Initiative { get; init; }

    /// <summary>Deux bonus réunis : les points s'ajoutent, la jauge de départ la plus haute l'emporte.</summary>
    public QuestModifiers Plus(QuestModifiers other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new QuestModifiers
        {
            StartGaugePercent = Math.Max(StartGaugePercent, other.StartGaugePercent),
            StartTempHp = StartTempHp + other.StartTempHp,
            Hp = Hp + other.Hp,
            Power = Power + other.Power,
            Initiative = Initiative + other.Initiative,
        };
    }

    public void Validate()
    {
        if (StartGaugePercent is < 0 or > 100 || StartTempHp < 0)
            throw new ArgumentException("A quest bonus needs a start gauge between 0 and 100 % and no negative armor.");
    }

    /// <summary>Le héros déployé, au début du combat, avec ce bonus.</summary>
    public UnitSetup ApplyTo(UnitSetup hero, CombatRules rules)
    {
        ArgumentNullException.ThrowIfNull(hero);
        ArgumentNullException.ThrowIfNull(rules);
        var points = hero.Points ?? StatPoints.None;
        return hero with
        {
            InitialGauge = Math.Max(hero.InitialGauge, rules.GaugeAt(StartGaugePercent)),
            StartTempHp = hero.StartTempHp + StartTempHp,
            Points = points with
            {
                Hp = Math.Max(0, points.Hp + Hp),
                Power = Math.Max(0, points.Power + Power),
                Initiative = Math.Max(0, points.Initiative + Initiative),
            },
        };
    }
}
