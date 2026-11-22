using Game.Core.Combat;
using Game.Core.Progression;

namespace Game.Core.Network;

/// <summary>Héros <paramref name="Unit"/> joué par le joueur <paramref name="Player"/>.</summary>
public sealed record HeroAssignment(UnitId Unit, int Player);

/// <summary>
/// Tout ce qu'il faut pour que le serveur et chaque client construisent exactement le même combat (avec le même
/// contenu) : le scénario, les fiches des héros de campagne déployés (<paramref name="Deployed"/>, placés après les
/// unités du scénario : un client n'a pas la campagne), et qui joue quel héros. Les héros que personne ne joue sont
/// confiés à l'IA de base. Une quête arrive aussi avec sa gravité (<see cref="Gravity"/>, <see cref="GravityLevel"/>
/// niveaux : les crochets faits en route) et le bonus gagné en route pour ses héros (<see cref="Modifiers"/>).
/// </summary>
public sealed record CombatLaunch(string ScenarioId, IReadOnlyList<HeroAssignment> Heroes, IReadOnlyList<HeroSheet>? Deployed = null)
{
    public World.GravityStep? Gravity { get; init; }

    public int GravityLevel { get; init; }

    public World.QuestModifiers? Modifiers { get; init; }

    public CombatSetup BuildSetup(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var setup = content.SetupFor(ScenarioId, Deployed);
        if (Modifiers is { } modifiers && Deployed is { Count: > 0 })
        {
            var scenario = content.Scenarios[ScenarioId];
            var heroes = Enumerable.Range(0, Deployed.Count).Select(scenario.DeployedUnitId).ToHashSet();
            setup = setup with { Units = setup.Units.Select(u => heroes.Contains(u.Id) ? modifiers.ApplyTo(u, content.Rules) : u).ToList() };
        }
        if (Gravity is { } gravity)
            setup = gravity.Apply(setup, GravityLevel, content.Units);
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
        return content.SetupFor(scenarioId).Units.Where(IsPlayableHero).ToList();
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

    private static bool IsPlayableHero(UnitSetup unit) =>
        unit.Team == CombatSimulation.PlayerTeam && unit.Definition.Behavior is null && !unit.Definition.IsCivilian;
}
