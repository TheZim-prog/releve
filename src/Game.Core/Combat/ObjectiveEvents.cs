using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>L'objectif n° <paramref name="Objective"/> (rang dans <see cref="CombatSimulation.Objectives"/>) est réussi.</summary>
public sealed record ObjectiveCompleted(int Tick, int Objective) : CombatEvent(Tick);

/// <summary>L'objectif n° <paramref name="Objective"/> (rang dans <see cref="CombatSimulation.Objectives"/>) est raté.</summary>
public sealed record ObjectiveFailed(int Tick, int Objective) : CombatEvent(Tick);

/// <summary>
/// <paramref name="Unit"/> travaille sur l'objet de la case <paramref name="Cell"/> (course contre la montre) : il en est à
/// <paramref name="Done"/> interactions sur <paramref name="Needed"/>.
/// </summary>
public sealed record DeviceOperated(int Tick, UnitId Unit, Cell Cell, int Done, int Needed) : CombatEvent(Tick);

/// <summary>Le combat est joué : victoire ou défaite (<see cref="CombatSimulation.Outcome"/>).</summary>
public sealed record CombatEnded(int Tick, bool Victory) : CombatEvent(Tick);
