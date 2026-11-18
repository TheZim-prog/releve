using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Objectives;

namespace Game.Core.Tests;

/// <summary>
/// Raccourcis pour essayer les compétences du contenu réel : les unités sont montées ici, sans points de stats, donc
/// rien ne dépend des héros d'essai de data/heroes/trial.json, qui sont faits pour être modifiés.
/// </summary>
internal static class ContentTestKit
{
    public static readonly GameContent Content = GameContent.Load(Path.Combine(AppContext.BaseDirectory, "data"));
    public static readonly CombatRules Rules = Content.Rules;

    /// <summary>
    /// Un combat sur un terrain vide. L'unité 1 est le lanceur : un archétype de héros (équipe 0) avec la compétence
    /// essayée, jauge pleine. Les autres sont des unités du contenu, immobiles et sans IA.
    /// </summary>
    public static CombatSimulation Fight(string caster, string skill, (int X, int Y) at, params (string Unit, int Team, int X, int Y)[] others) =>
        Fight(withAi: false, caster, skill, at, others);

    /// <summary>Le même combat, où les autres unités gardent leur IA (une provocation n'a d'effet que sur une IA).</summary>
    public static CombatSimulation FightAgainstAi(string caster, string skill, (int X, int Y) at, params (string Unit, int Team, int X, int Y)[] others) =>
        Fight(withAi: true, caster, skill, at, others);

    private static CombatSimulation Fight(bool withAi, string caster, string skill, (int X, int Y) at, (string Unit, int Team, int X, int Y)[] others)
    {
        var archetype = Content.Units[caster];
        var hero = archetype with { Skills = archetype.Skills.Append(Content.Skills[skill]).ToList() };
        var units = new List<UnitSetup> { hero.ToSetup(new UnitId(1), 0, new Cell(at.X, at.Y), Rules.GaugeCapacity) };
        units.AddRange(others.Select((o, i) =>
            (withAi ? Content.Units[o.Unit] : Content.Units[o.Unit] with { Behavior = null }).ToSetup(new UnitId(i + 2), o.Team, new Cell(o.X, o.Y))));
        return new CombatSimulation(new CombatSetup(Rules, new Grid(9, 5), units, Seed: 1));
    }

    /// <summary>Le lanceur utilise sa compétence ; les événements jusqu'à son effet (attaque chargée comprise).</summary>
    public static IReadOnlyList<CombatEvent> Use(CombatSimulation sim, string skill, int? target = null, (int X, int Y)? cell = null)
    {
        var use = new SkillUse(skill, target is { } t ? new UnitId(t) : null, cell is { } c ? new Cell(c.X, c.Y) : null);
        sim.Submit(new ActivateCommand(new UnitId(1), null, use));
        var events = sim.Tick().ToList();
        Assert.DoesNotContain(events, e => e is CommandRejected);
        while (sim.FindUnit(new UnitId(1))!.Charge is not null)
            events.AddRange(sim.Tick());
        return events;
    }

    public static int[] Affected(IEnumerable<CombatEvent> events, StatusKind status) =>
        events.OfType<StatusApplied>().Where(e => e.Status == status).Select(e => e.Unit.Value).Order().ToArray();

    public static int[] Damaged(IEnumerable<CombatEvent> events) =>
        events.OfType<UnitDamaged>().Select(e => e.Unit.Value).Order().ToArray();

    public static int Ticks(int milliseconds) => Rules.MillisecondsToTicks(milliseconds);

    /// <summary>
    /// Joue le combat jusqu'à son issue (au plus 10 minutes de jeu) et vérifie qu'il se termine, que plus aucun objectif
    /// n'est en cours, et qu'il se rejoue à l'identique. Un message figé du script est continué au pas suivant, comme
    /// par un joueur pressé. Renvoie le combat joué et son issue en clair.
    /// </summary>
    public static (CombatSimulation Sim, string Outcome) PlayToTheEnd(CombatSetup setup, string name)
    {
        var sim = new CombatSimulation(setup);
        var maxTicks = Rules.TicksPerSecond * 60 * 10;
        while (sim.CurrentTick < maxTicks && sim.Step < 2 * maxTicks && sim.Outcome is null)
        {
            if (sim.FrozenMessage is { } message)
                sim.Submit(new ContinueCommand(message.Number));
            sim.Tick();
        }

        Assert.True(sim.Outcome is not null, $"Combat '{name}' still running after {maxTicks} ticks.");
        Assert.DoesNotContain(sim.Objectives, o => o.State == ObjectiveState.InProgress);
        Assert.Equal(sim.EventLog, CombatSimulation.Replay(setup, sim.CommandLog, sim.Step).EventLog);
        var outcome = sim.Outcome == true ? "victoire" : "défaite";
        return (sim, $"[{name}] {sim.CurrentTick / (double)Rules.TicksPerSecond:0.0} s, {outcome}");
    }

    public static Cell PositionOf(CombatSimulation sim, int id) => sim.FindUnit(new UnitId(id))!.Position;
}
