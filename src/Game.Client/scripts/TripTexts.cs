using Game.Core;
using Game.Core.World;

namespace Game.Client;

/// <summary>Les textes du trajet vers une quête (affichage minimal, à polir plus tard).</summary>
public static class TripTexts
{
    /// <summary>« jauge de départ 50 %, armure 10, puissance +2 » : le bonus (ou malus) gagné pour la quête, ou rien.</summary>
    public static string Bonus(QuestModifiers bonus)
    {
        ArgumentNullException.ThrowIfNull(bonus);
        var parts = new List<string>();
        if (bonus.StartGaugePercent > 0)
            parts.Add($"jauge de départ {bonus.StartGaugePercent} %");
        if (bonus.StartTempHp > 0)
            parts.Add($"armure {bonus.StartTempHp}");
        foreach (var (amount, stat) in new[] { (bonus.Hp, "PV"), (bonus.Power, "puissance"), (bonus.Initiative, "initiative") })
        {
            if (amount != 0)
                parts.Add($"{stat} {amount:+0;-0} point{(Math.Abs(amount) > 1 ? "s" : "")}");
        }
        return string.Join(", ", parts);
    }

    /// <summary>« Retenir la voiture — Rempart réussit : réputation +5, jauge de départ 50 % » : ce qu'a donné un événement.</summary>
    public static string Outcome(GameContent content, EventOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(outcome);
        var title = content.Events.TryGetValue(outcome.Event, out var worldEvent) ? worldEvent.Title : outcome.Event;
        if (outcome.Option is null)
            return $"{title} : le groupe a passé son chemin.";
        var test = outcome.Success is { } success ? $" — {outcome.TestedHero} {(success ? "réussit" : "échoue")}" : "";
        var effects = Effects(outcome.Effects);
        return $"{title} : {outcome.Option}{test}{(effects.Length > 0 ? $" : {effects}" : ".")}";
    }

    private static string Effects(EventEffects effects)
    {
        var parts = new List<string>();
        if (effects.Materials.Values.Sum() is var materials and not 0)
            parts.Add($"{materials:+0;-0} matériaux");
        if (effects.Reputation != 0)
            parts.Add($"réputation {effects.Reputation:+0;-0}");
        if (effects.HalfFalls > 0)
            parts.Add($"{CampaignTexts.Falls(effects.HalfFalls)} chute{(effects.HalfFalls > 2 ? "s" : "")}");
        if (Bonus(effects.Quest) is { Length: > 0 } bonus)
            parts.Add($"pour la quête : {bonus}");
        return string.Join(", ", parts);
    }
}
