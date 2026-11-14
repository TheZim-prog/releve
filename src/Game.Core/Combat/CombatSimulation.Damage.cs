using Game.Core.Grids;

namespace Game.Core.Combat;

// Coups : dégâts, exécution, couverture, soins, mise hors de combat. Ce qui protège une cible (dômes, PV temporaires)
// est dans CombatSimulation.Protections.cs.
public sealed partial class CombatSimulation
{
    /// <summary>
    /// Effets d'un coup de <paramref name="attacker"/> sur sa cible : dégâts (s'il y en a : exécution si la cible est
    /// assez basse et pas un boss, sinon tirage multiplié par <paramref name="damageFactor"/> et par la vulnérabilité
    /// de la cible), Point faible de l'attaquant, puis soin, PV temporaires et états si la cible est encore debout,
    /// puis gain de jauge.
    /// <paramref name="fallbackCell"/> : où tombe la cible si elle est à terre sur une case qu'elle partage
    /// (seulement possible en plein déplacement, en traversant un allié).
    /// <paramref name="origin"/> : d'où vient le coup (tireur, centre d'une zone), si la couverture peut jouer ; une
    /// exécution l'ignore.
    /// Renvoie faux si un dôme a bloqué le coup (il n'a alors aucun effet).
    /// </summary>
    private bool ResolveHit(Unit attacker, SkillDefinition skill, Unit target, int damageFactor, Cell? fallbackCell = null,
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
            var piercing = skill.IgnoresProtections;
            if (CanExecute(skill, target))
            {
                _events.Add(new UnitExecuted(CurrentTick, attacker.Id, target.Id));
                dealt = target.Hp;
                piercing = true; // une exécution achève : des PV temporaires ne la retiennent pas
            }
            else
            {
                var roll = skill.DealsDamage ? _random.NextInt(skill.MinDamage, skill.MaxDamage) : 0;
                if (robotBonus is { DealsDamage: true })
                    roll += _random.NextInt(robotBonus.MinDamage, robotBonus.MaxDamage);
                dealt = Scaled(roll, WithFlankBonus(damageFactor, attacker, skill, target), target);
                if (origin is { } from && CoverFor(from, target.Position) is { } cover)
                    dealt = AbsorbByCover(target, cover, dealt);
            }
            var lost = Damage(target, dealt, fallbackCell, piercing);

            // Point faible : un coup qui a vraiment entamé les PV d'un ennemi le rend vulnérable (pas un coup
            // entièrement encaissé par ses PV temporaires).
            if (lost > 0 && target.Status == UnitStatus.Active && AreOpponents(target, attacker)
                && attacker.HasPassive(PassiveKind.WeakPoint))
                ApplyStatus(target, new StatusEffect { Status = StatusKind.Exposed, DurationMs = Rules.WeakPointExposedMs }, attacker);
        }
        if (target.Status != UnitStatus.Active)
            return true;
        if (skill.Heals)
            Heal(target, attacker.HealWith(_random.NextInt(skill.MinHeal, skill.MaxHeal)));
        if (skill.TempHp is { } tempHp)
            GrantTempHp(attacker, target, tempHp);
        foreach (var effect in skill.Effects)
            ApplyStatus(target, effect, attacker, robotBonus?.StatusDurationPercent ?? 100);
        foreach (var effect in robotBonus?.Effects ?? [])
            ApplyStatus(target, effect, attacker);
        if (skill.TauntActions > 0 && AreOpponents(target, attacker))
            Taunt(target, attacker, skill.TauntActions);
        if (skill.GaugeGainPercent > 0)
            GainGauge(target, Rules.GaugeAt(skill.GaugeGainPercent));
        return true;
    }

    /// <summary>Multiplicateur des dégâts infligés par l'unité, en dix-millièmes (voir <see cref="Unit.DamageFactor"/>).</summary>
    private static int DamageFactorOf(Unit unit) => unit.DamageFactor;

    /// <summary>
    /// Dégâts finaux : tirage × bonus de l'attaquant (<paramref name="damageFactor"/>) × vulnérabilité de la cible,
    /// en une seule division (pas d'arrondi intermédiaire).
    /// </summary>
    private int Scaled(int damage, int damageFactor, Unit target)
    {
        var exposedPercent = target.HasStatus(StatusKind.Exposed) ? Rules.ExposedDamageTakenPercent : 100;
        return (int)((long)damage * damageFactor * exposedPercent / 1_000_000);
    }

    /// <summary>
    /// Prise en tenaille : le multiplicateur de dégâts, augmenté si la compétence a ce bonus et qu'un allié du lanceur
    /// (un héros debout, pas un gadget) est lui aussi au contact de la cible.
    /// </summary>
    private int WithFlankBonus(int damageFactor, Unit attacker, SkillDefinition skill, Unit target)
    {
        if (skill.FlankDamagePercent == 0)
            return damageFactor;
        var flanked = _units.Any(u => u != attacker && u.IsStandingCombatant && u.Team == attacker.Team
                                     && IsInMeleeReach(u, target.Position));
        return flanked ? (int)((long)damageFactor * skill.FlankDamagePercent / 100) : damageFactor;
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

    /// <summary>
    /// Inflige des dégâts : les PV temporaires de la cible encaissent d'abord (sauf coup <paramref name="piercing"/>, qui
    /// ignore les protections), le reste entame ses PV. Renvoie les PV réellement perdus.
    /// </summary>
    private int Damage(Unit target, int amount, Cell? fallbackCell, bool piercing = false)
    {
        if (!piercing && amount > 0)
        {
            amount = AbsorbByTempHp(target, amount);
            if (amount == 0)
                return 0; // tout a été encaissé : ses PV ne bougent pas
        }
        var before = target.Hp;
        LoseHp(target, amount, fallbackCell);
        return before - target.Hp;
    }

    /// <summary>La cible perd des PV ; à 0, elle est mise hors de combat (à terre ou retirée).</summary>
    private void LoseHp(Unit target, int amount, Cell? fallbackCell)
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
        unit.TempHp = 0;
        EndTauntsOf(unit);
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
}
