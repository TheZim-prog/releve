using Game.Core.Combat;

namespace Game.Core.Network;

/// <summary>Héros <paramref name="Unit"/> joué par le joueur <paramref name="Player"/>.</summary>
public sealed record HeroAssignment(UnitId Unit, int Player);

/// <summary>
/// Tout ce qu'il faut pour que le serveur et chaque client construisent exactement le même combat (avec le même
/// contenu) : le scénario, et qui joue quel héros. Les héros que personne ne joue sont confiés à l'IA de base.
/// </summary>
public sealed record CombatLaunch(string ScenarioId, IReadOnlyList<HeroAssignment> Heroes)
{
    /// <summary>Camp des joueurs dans les scénarios.</summary>
    public const int PlayerTeam = 0;

    public CombatSetup BuildSetup(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var setup = Scenario(content, ScenarioId).ToSetup(content.Rules, content.Units);
        var played = Heroes.Select(h => h.Unit).ToHashSet();
        var units = setup.Units
            .Select(u => IsPlayableHero(u) && !played.Contains(u.Id)
                ? u with { Definition = u.Definition with { Behavior = Behaviors.Basic } }
                : u)
            .ToList();
        return setup with { Units = units };
    }

    /// <summary>Héros jouables du scénario (unités du camp des joueurs sans IA propre, hors civils), dans l'ordre.</summary>
    public static IReadOnlyList<UnitSetup> PlayableHeroes(GameContent content, string scenarioId)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Scenario(content, scenarioId).ToSetup(content.Rules, content.Units).Units.Where(IsPlayableHero).ToList();
    }

    /// <summary>
    /// Le joueur du héros <paramref name="hero"/> peut-il donner des ordres à <paramref name="unit"/> : son héros, ou une
    /// unité qu'il domine (s'il est confus, ou une domination finie : la simulation refusera).
    /// </summary>
    public static bool IsCommandedBy(Unit unit, UnitId hero)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return unit.Id == hero || unit.DominatedBy == hero;
    }

    /// <summary>Issue du combat libre : victoire quand il ne reste que le camp des joueurs, défaite quand il n'y est plus.</summary>
    public static bool? Outcome(CombatSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var teams = simulation.TeamsStillFighting();
        return !teams.Contains(PlayerTeam) ? false : teams.Count == 1 ? true : null;
    }

    private static bool IsPlayableHero(UnitSetup unit) =>
        unit.Team == PlayerTeam && unit.Definition.Behavior is null && !unit.Definition.IsCivilian;

    private static ScenarioDefinition Scenario(GameContent content, string scenarioId) =>
        content.Scenarios.TryGetValue(scenarioId, out var scenario)
            ? scenario
            : throw new ArgumentException($"Unknown scenario '{scenarioId}'.", nameof(scenarioId));
}
