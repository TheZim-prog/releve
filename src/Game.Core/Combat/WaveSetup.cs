namespace Game.Core.Combat;

/// <summary>
/// Une vague de renforts : des unités qui arrivent <paramref name="AtMs"/> ms après le début du combat, jauge comme
/// indiqué dans leur <see cref="UnitSetup"/>. Leur identifiant leur est donné à l'arrivée (celui du setup est ignoré),
/// et une unité dont la case est prise arrive sur la case libre la plus proche.
/// </summary>
public sealed record WaveSetup(int AtMs, IReadOnlyList<UnitSetup> Units)
{
    private readonly IReadOnlyList<UnitSetup> _units = CombatSetup.ReadOnlyCopy(Units);

    public IReadOnlyList<UnitSetup> Units
    {
        get => _units;
        init => _units = CombatSetup.ReadOnlyCopy(value);
    }

    /// <summary>La vague amène au moins un adversaire du camp des joueurs (ni civil).</summary>
    public bool BringsEnemies => Units.Any(u => u.Team != CombatSimulation.PlayerTeam && !u.Definition.IsCivilian);
}
