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

    /// <summary>Dôme posé par une unité : ses cases, et s'il a déjà bloqué quelque chose pendant la source en cours.</summary>
    private sealed class Dome(Unit owner, IReadOnlyList<Cell> cells)
    {
        public Unit Owner { get; } = owner;
        public HashSet<Cell> Cells { get; } = cells.ToHashSet();
        public bool Absorbing { get; set; }
    }

    /// <summary>Pose le dôme (l'ancien est déjà tombé : poser un dôme est une action du lanceur).</summary>
    private void RaiseDome(Unit owner, IReadOnlyList<Cell> cells)
    {
        _domes.Add(new Dome(owner, cells));
        _events.Add(new DomeRaised(CurrentTick, owner.Id, cells));
    }

    private void RemoveDome(Unit owner)
    {
        if (_domes.RemoveAll(d => d.Owner == owner) > 0)
            _events.Add(new DomeEnded(CurrentTick, owner.Id));
    }

    /// <summary>
    /// Un coup offensif (compétence visant un ennemi ou une zone) sur <paramref name="target"/> est-il bloqué par
    /// un dôme de son équipe qui la couvre ? Si oui, le dôme absorbe la source en cours (il tombera à sa fin). Un coup
    /// qui ignore les protections passe sans l'entamer. <paramref name="harmful"/> : des dégâts que fait une
    /// compétence de soutien (l'impact d'un allié lancé), bloqués comme un coup offensif.
    /// </summary>
    private bool IsBlockedByDome(SkillDefinition skill, Unit target, bool harmful = false)
    {
        if (!(skill.IsOffensive || harmful) || skill.IgnoresProtections)
            return false;
        // Un dôme protège son équipe et les civils sous lui.
        var dome = _domes.FirstOrDefault(d => (d.Owner.Team == target.Team || target.IsCivilian) && d.Cells.Contains(target.Position));
        if (dome is null)
            return false;
        dome.Absorbing = true;
        _events.Add(new HitBlocked(CurrentTick, target.Id, dome.Owner.Id));
        return true;
    }

    /// <summary>
    /// Fin d'une source (compétence ou attaque d'opportunité) : les dômes qui l'ont absorbée disparaissent, ainsi que
    /// ceux dont le lanceur est tombé pendant cette source (pas avant : l'attaque reste bloquée pour tous).
    /// Tous les dégâts arrivent pendant une source, donc tout lanceur tombé passe par ici.
    /// </summary>
    private void EndSource()
    {
        if (_domes.Count == 0)
            return;
        foreach (var dome in _domes.Where(d => d.Absorbing || d.Owner.Status != UnitStatus.Active).ToList())
            RemoveDome(dome.Owner);
    }
}
