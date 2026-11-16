using Game.Core.Grids;

namespace Game.Core.Combat;

// Ce qui protège une cible : les dômes, qui bloquent une attaque entière, et les PV temporaires, qui encaissent des
// dégâts à la place des vrais PV. Un coup qui ignore les protections (SkillDefinition.IgnoresProtections) passe les deux.
public sealed partial class CombatSimulation
{
    /// <summary>
    /// Donne des PV temporaires à <paramref name="target"/> : une part des PV actuels de <paramref name="caster"/>, ou un
    /// tirage multiplié par sa puissance, au moins 1. Pas de cumul : ils remplacent ceux que la cible avait déjà,
    /// sauf s'il lui en reste davantage (un don plus faible ne détruit pas une meilleure protection).
    /// </summary>
    private void GrantTempHp(Unit caster, Unit target, TempHpDefinition tempHp)
    {
        var amount = tempHp.PercentOfCasterHp > 0
            ? (int)((long)caster.Hp * tempHp.PercentOfCasterHp / 100)
            : caster.HealWith(_random.NextInt(tempHp.Min, tempHp.Max));
        amount = Math.Max(1, amount);
        if (amount < target.TempHp)
            return;
        target.TempHp = amount;
        target.TempHpEndTick = CurrentTick + Rules.MillisecondsToTicks(tempHp.DurationMs);
        _events.Add(new TempHpGained(CurrentTick, target.Id, amount, target.TempHpEndTick));
    }

    /// <summary>Les PV temporaires de la cible encaissent ce qu'ils peuvent ; renvoie les dégâts qui passent.</summary>
    private int AbsorbByTempHp(Unit target, int damage)
    {
        var absorbed = Math.Min(target.TempHp, damage);
        if (absorbed <= 0)
            return damage;
        target.TempHp -= absorbed;
        _events.Add(new DamageAbsorbed(CurrentTick, target.Id, absorbed, target.TempHp));
        return damage - absorbed;
    }

    /// <summary>Les PV temporaires arrivés à échéance disparaissent.</summary>
    private void ExpireTempHp()
    {
        foreach (var unit in _units)
        {
            if (unit.TempHp == 0 || unit.TempHpEndTick > CurrentTick)
                continue;
            unit.TempHp = 0;
            _events.Add(new TempHpEnded(CurrentTick, unit.Id));
        }
    }

    /// <summary>
    /// Dôme posé par une unité : ses cases, ou l'allié qu'il suit (Bouclier personnel) ; s'il est tenu (Forteresse : il
    /// bloque tout sans tomber, sauf son lanceur) ; et s'il tombe à la fin de la source en cours (il a bloqué un coup,
    /// ou sa forteresse vient d'être lâchée).
    /// </summary>
    private sealed class Dome(Unit owner, IReadOnlyList<Cell>? cells, Unit? shielded, bool held)
    {
        public Unit Owner { get; } = owner;
        public Unit? Shielded { get; } = shielded;
        public bool Held { get; } = held;
        public bool Falling { get; set; }
        private readonly HashSet<Cell> _cells = cells?.ToHashSet() ?? new HashSet<Cell>();

        /// <summary>Les cases couvertes maintenant : celles du dôme, ou celle de l'allié qu'il suit.</summary>
        public IEnumerable<Cell> Cells => Shielded is { } ally ? new[] { ally.Position } : _cells;

        /// <summary>Le dôme couvre-t-il cette unité (de son équipe, ou un civil) ?</summary>
        public bool Covers(Unit unit) =>
            (Owner.Team == unit.Team || unit.IsCivilian)
            && (Shielded is { } ally ? unit == ally : _cells.Contains(unit.Position) && !(Held && unit == Owner));
    }

    /// <summary>
    /// Les cases protégées par un dôme en ce moment (celui d'un Bouclier personnel suit son allié ; une forteresse ne
    /// protège pas la case de celui qui la tient).
    /// </summary>
    public IEnumerable<Cell> DomeCells => _domes.SelectMany(d => d.Cells.Where(c => !(d.Held && c == d.Owner.Position)));

    /// <summary>
    /// Pose un dôme sur ces cases, ou sur un allié qu'il suit (l'ancien est déjà tombé : poser un dôme est une action
    /// du lanceur). <paramref name="held"/> : une forteresse, tenue pendant la charge.
    /// </summary>
    private void RaiseDome(Unit owner, IReadOnlyList<Cell>? cells, Unit? shielded = null, bool held = false)
    {
        var dome = new Dome(owner, cells, shielded, held);
        _domes.Add(dome);
        _events.Add(new DomeRaised(CurrentTick, owner.Id, dome.Cells.ToList(), shielded?.Id));
    }

    /// <summary>
    /// La forteresse de l'unité est lâchée (charge interrompue) : elle tombe à la fin de la source en cours, qui reste
    /// bloquée pour tous ses protégés.
    /// </summary>
    private void LetGoOfFortress(Unit owner)
    {
        foreach (var dome in _domes.Where(d => d.Owner == owner && d.Held))
            dome.Falling = true;
    }

    private void RemoveDome(Unit owner)
    {
        if (_domes.RemoveAll(d => d.Owner == owner) > 0)
            _events.Add(new DomeEnded(CurrentTick, owner.Id));
    }

    /// <summary>
    /// Un coup offensif (compétence visant un ennemi ou une zone) sur <paramref name="target"/> est-il bloqué par
    /// un dôme de son équipe qui la couvre ? Si oui, le dôme absorbe la source en cours (il tombera à sa fin), sauf une
    /// forteresse, qui tient (et passe donc avant les autres dômes). Un coup qui ignore les protections passe sans
    /// l'entamer. <paramref name="harmful"/> : des dégâts que fait une compétence de soutien (l'impact d'un allié lancé),
    /// bloqués comme un coup offensif.
    /// </summary>
    private bool IsBlockedByDome(SkillDefinition skill, Unit target, bool harmful = false)
    {
        if (DomeBlocking(skill, target, harmful) is not { } dome)
            return false;
        if (!dome.Held)
            dome.Falling = true;
        _events.Add(new HitBlocked(CurrentTick, target.Id, dome.Owner.Id));
        return true;
    }

    /// <summary>
    /// Le dôme qui bloquerait ce coup sur <paramref name="target"/>, sans rien consommer : une forteresse d'abord (elle
    /// tient), sinon le premier dôme qui la couvre ; aucun pour un soutien ou un coup qui ignore les protections.
    /// </summary>
    private Dome? DomeBlocking(SkillDefinition skill, Unit target, bool harmful = false)
    {
        if (!(skill.IsOffensive || harmful) || skill.IgnoresProtections)
            return null;
        return _domes.FirstOrDefault(d => d.Held && d.Covers(target)) ?? _domes.FirstOrDefault(d => d.Covers(target));
    }

    /// <summary>
    /// Fin d'une source (compétence, attaque d'opportunité, piège…) : les dômes qui l'ont absorbée disparaissent, ainsi que
    /// ceux dont le lanceur est tombé pendant cette source (pas avant : l'attaque reste bloquée pour tous). Puis les
    /// réactions qu'elle a provoquées (ripostes, tirs de protection, explosions) partent, chacune comme une source à part.
    /// Tous les dégâts arrivent pendant une source, donc tout lanceur tombé passe par ici.
    /// </summary>
    private void EndSource()
    {
        EndDomes();
        FlushReactions();
    }

    /// <summary>
    /// Les dômes qui ont absorbé la source ou qui ont été lâchés, ceux dont le lanceur est tombé et ceux dont l'allié
    /// protégé est tombé disparaissent.
    /// </summary>
    private void EndDomes()
    {
        if (_domes.Count == 0)
            return;
        foreach (var dome in _domes.Where(d => d.Falling || d.Owner.Status != UnitStatus.Active
                                               || d.Shielded is { Status: not UnitStatus.Active }).ToList())
            RemoveDome(dome.Owner);
    }
}
