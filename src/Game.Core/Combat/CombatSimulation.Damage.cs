using Game.Core.Grids;

namespace Game.Core.Combat;

// Coups : dégâts, exécution, couverture, soins, mise hors de combat, et dômes qui bloquent une source.
public sealed partial class CombatSimulation
{
    /// <summary>
    /// Effets d'un coup de <paramref name="attacker"/> sur sa cible : dégâts (s'il y en a : exécution si la cible est
    /// assez basse et pas un boss, sinon tirage multiplié par <paramref name="damagePercent"/> et par la vulnérabilité
    /// de la cible), Point faible de l'attaquant, puis états si la cible est encore debout, puis gain de jauge.
    /// <paramref name="fallbackCell"/> : où tombe la cible si elle est à terre sur une case qu'elle partage
    /// (seulement possible en plein déplacement, en traversant un allié).
    /// <paramref name="origin"/> : d'où vient le coup (tireur, centre d'une zone), si la couverture peut jouer ; une
    /// exécution l'ignore.
    /// Renvoie faux si un dôme a bloqué le coup (il n'a alors aucun effet).
    /// </summary>
    private bool ResolveHit(Unit attacker, SkillDefinition skill, Unit target, int damagePercent, Cell? fallbackCell = null,
        Cell? origin = null)
    {
        if (IsBlockedByDome(skill, target))
            return false;
        // Immunisée (robot face à un pouvoir mental, prise dans une zone : une cible directe est refusée avant).
        if (IsImmune(skill, target))
        {
            _events.Add(new UnitResisted(CurrentTick, target.Id));
            return false;
        }

        // Contre un robot adverse, l'effet renforcé de la compétence (dégâts en plus, états plus longs) ; pas sur ses
        // propres gadgets pris dans son IEM, étourdis comme les ennemis ordinaires.
        var robotBonus = target.Definition.IsRobot && AreOpponents(target, attacker) ? skill.VsRobots : null;
        if (skill.DealsDamage || robotBonus is { DealsDamage: true })
        {
            int dealt;
            if (CanExecute(skill, target))
            {
                _events.Add(new UnitExecuted(CurrentTick, attacker.Id, target.Id));
                dealt = target.Hp;
            }
            else
            {
                var roll = skill.DealsDamage ? _random.NextInt(skill.MinDamage, skill.MaxDamage) : 0;
                if (robotBonus is { DealsDamage: true })
                    roll += _random.NextInt(robotBonus.MinDamage, robotBonus.MaxDamage);
                dealt = Scaled(roll, damagePercent, target);
                if (origin is { } from && CoverFor(from, target.Position) is { } cover)
                    dealt = AbsorbByCover(target, cover, dealt);
            }
            Damage(target, dealt, fallbackCell);

            // Point faible : un coup qui a vraiment fait des dégâts à un ennemi le rend vulnérable.
            if (dealt > 0 && target.Status == UnitStatus.Active && AreOpponents(target, attacker)
                && attacker.HasPassive(PassiveKind.WeakPoint))
                ApplyStatus(target, new StatusEffect { Status = StatusKind.Exposed, DurationMs = Rules.WeakPointExposedMs }, attacker);
        }
        if (target.Status != UnitStatus.Active)
            return true;
        if (skill.Heals)
            Heal(target, _random.NextInt(skill.MinHeal, skill.MaxHeal));
        foreach (var effect in skill.Effects)
            ApplyStatus(target, effect, attacker, robotBonus?.StatusDurationPercent ?? 100);
        if (skill.GaugeGainPercent > 0)
            GainGauge(target, Rules.GaugeAt(skill.GaugeGainPercent));
        return true;
    }

    /// <summary>Multiplicateur des dégâts infligés par l'unité, en % (surpuissant ou non).</summary>
    private int DamagePercentOf(Unit unit) => unit.HasStatus(StatusKind.Empowered) ? Rules.EmpoweredDamagePercent : 100;

    /// <summary>
    /// Dégâts finaux : tirage × bonus de l'attaquant (<paramref name="damagePercent"/>) × vulnérabilité de la cible,
    /// en une seule division (pas d'arrondi intermédiaire).
    /// </summary>
    private int Scaled(int damage, int damagePercent, Unit target)
    {
        var exposedPercent = target.HasStatus(StatusKind.Exposed) ? Rules.ExposedDamageTakenPercent : 100;
        return (int)((long)damage * damagePercent * exposedPercent / 10_000);
    }

    /// <summary>Exécution : la cible est au plus au seuil de PV de la compétence, et ce n'est pas un boss.</summary>
    private static bool CanExecute(SkillDefinition skill, Unit target) =>
        skill.ExecuteAtOrBelowPercent > 0
        && !target.Definition.IsBoss
        && (long)target.Hp * 100 <= (long)target.MaxHp * skill.ExecuteAtOrBelowPercent;

    /// <summary>
    /// Obstacle bas qui couvre <paramref name="target"/> contre un coup venu de <paramref name="origin"/> : le premier
    /// que traverse la ligne (centre à centre, coins compris) parmi ceux collés à la cible. Aucun pour un coup au
    /// contact (ou parti de la case même de la cible).
    /// </summary>
    private Cell? CoverFor(Cell origin, Cell target)
    {
        if (origin == target || origin.IsAdjacentTo(target))
            return null;
        foreach (var cell in LineOfSight.CrossedCells(origin, target))
        {
            if (_grid.TerrainAt(cell) == Terrain.LowObstacle && cell.IsAdjacentTo(target))
                return cell;
        }
        return null;
    }

    /// <summary>
    /// L'obstacle <paramref name="cover"/> absorbe sa part des dégâts (arrondie en dessous), dans la limite de ses PV, et
    /// l'encaisse ; à 0 PV, il devient du sol. Renvoie les dégâts qui passent.
    /// </summary>
    private int AbsorbByCover(Unit target, Cell cover, int damage)
    {
        var hp = _coverHp.GetValueOrDefault(cover, Rules.LowObstacleHp);
        var absorbed = Math.Min(hp, (int)((long)damage * Rules.CoverDamageReductionPercent / 100));
        if (absorbed == 0)
            return damage;
        hp -= absorbed;
        _coverHp[cover] = hp;
        _events.Add(new HitCovered(CurrentTick, target.Id, cover, absorbed, hp));
        if (hp == 0)
        {
            _grid.SetTerrain(cover, Terrain.Floor);
            _coverHp.Remove(cover);
            _events.Add(new CoverDestroyed(CurrentTick, cover));
        }
        return damage - absorbed;
    }

    private void Damage(Unit target, int amount, Cell? fallbackCell)
    {
        target.Hp = Math.Max(0, target.Hp - amount);
        _events.Add(new UnitDamaged(CurrentTick, target.Id, amount, target.Hp));
        if (target.Hp > 0)
        {
            if (!target.IsFleeing && target.Definition.FleeAtHpPercent is { } percent
                && (long)target.Hp * 100 < (long)target.MaxHp * percent)
                StartFleeing(target);
            return;
        }

        // Son dôme éventuel tombe à la fin de la source en cours (EndSource).
        TakeOutOfAction(target, target.OnDefeat == DefeatOutcome.Downed ? UnitStatus.Downed : UnitStatus.Removed);

        if (target.Status == UnitStatus.Removed)
        {
            _events.Add(new UnitRemoved(CurrentTick, target.Id));
            return;
        }

        // Une unité par case : à terre en traversant un allié, elle tombe sur la dernière case libre.
        if (IsOccupiedByOther(target, target.Position))
            target.Position = fallbackCell ?? throw new InvalidOperationException($"Unit {target.Id} fell on an occupied cell.");
        _events.Add(new UnitDowned(CurrentTick, target.Id, target.Position));
    }

    /// <summary>
    /// Met l'unité hors de combat (à terre ou retirée) : elle perd ses états, sa charge et son intention, annoncés
    /// avant sa chute (rien ne lui arrive plus après). Sa jauge est gelée à sa valeur. Le statut change d'abord,
    /// pour que la fin d'un étourdissement n'annonce pas l'unité prête.
    /// </summary>
    private void TakeOutOfAction(Unit unit, UnitStatus status)
    {
        unit.Status = status;
        foreach (var effect in unit.StatusEndTicks.Keys.ToList())
            EndStatus(unit, effect);
        InterruptCharge(unit);
        unit.Intention = null;
    }

    /// <summary>
    /// Unités à activations limitées (ex. balise) : épuisées, elles disparaissent, mais après leur dernière action
    /// (une attaque chargée en cours se déclenche d'abord).
    /// </summary>
    private void DismissIfExhausted(Unit unit)
    {
        if (unit.Definition.MaxActivations is { } max && unit.Activations >= max
            && unit.Status == UnitStatus.Active && unit.Charge is null)
            Dismiss(unit);
    }

    /// <summary>Retire une unité sans qu'elle soit vaincue (gadget remplacé ou épuisé).</summary>
    private void Dismiss(Unit unit)
    {
        TakeOutOfAction(unit, UnitStatus.Removed);
        RemoveDome(unit);
        _events.Add(new UnitRemoved(CurrentTick, unit.Id));
    }

    /// <summary>Soigne une unité debout, sans dépasser son maximum.</summary>
    private void Heal(Unit unit, int amount)
    {
        var healed = Math.Min(amount, unit.MaxHp - unit.Hp);
        if (healed <= 0)
            return;
        unit.Hp += healed;
        _events.Add(new UnitHealed(CurrentTick, unit.Id, healed, unit.Hp));
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
    /// un dôme de son équipe qui la couvre ? Si oui, le dôme absorbe la source en cours (il tombera à sa fin).
    /// </summary>
    private bool IsBlockedByDome(SkillDefinition skill, Unit target)
    {
        if (!skill.IsOffensive)
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
