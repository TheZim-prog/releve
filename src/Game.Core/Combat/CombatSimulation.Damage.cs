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
        // Tombée plus tôt dans la même compétence (une Réaction en chaîne au milieu d'une zone) : elle n'est pas refrappée.
        if (target.Status != UnitStatus.Active)
            return false;
        // Prise par un garde du corps, esquivée ou parée : la cible ne la reçoit pas.
        if (AvertedBeforeHit(attacker, skill, target, damageFactor, origin))
            return false;
        // Bloquée par un dôme ou sans effet sur elle : elle a quand même été attaquée (riposte).
        if (IsBlockedByDome(skill, target))
        {
            AttackedBy(attacker, skill, target);
            return false;
        }
        // Immunisée (robot face à un pouvoir mental, prise dans une zone : une cible directe est refusée avant).
        if (IsImmune(skill, target))
        {
            _events.Add(new UnitResisted(CurrentTick, target.Id));
            AttackedBy(attacker, skill, target);
            return false;
        }

        // Contre un robot adverse, l'effet renforcé de la compétence (dégâts en plus, états plus longs) ; pas sur ses
        // propres gadgets pris dans son IEM, étourdis comme les ennemis ordinaires.
        var robotBonus = target.Definition.IsRobot && AreOpponents(target, attacker) ? skill.VsRobots : null;
        if (skill.DealsDamage || robotBonus is { DealsDamage: true })
        {
            int dealt;
            var ignoresProtections = skill.IgnoresProtections;
            if (CanExecute(skill, target))
            {
                _events.Add(new UnitExecuted(CurrentTick, attacker.Id, target.Id));
                dealt = target.Hp;
                ignoresProtections = true; // une exécution achève : des PV temporaires ne la retiennent pas
            }
            else
            {
                var roll = skill.DealsDamage ? _random.NextInt(skill.MinDamage, skill.MaxDamage) : 0;
                if (robotBonus is { DealsDamage: true })
                    roll += _random.NextInt(robotBonus.MinDamage, robotBonus.MaxDamage);
                var factor = WithDamagePassives(WithSkillBonuses(damageFactor, attacker, skill, target), attacker, skill, target);
                dealt = Scaled(roll, factor, target);
                if (origin is { } from && !attacker.HasPassive(PassiveKind.Piercing) && CoverFor(from, target.Position) is { } cover)
                    dealt = AbsorbByCover(attacker, target, cover, dealt);
            }
            var wereOpponents = AreOpponents(target, attacker); // avant le coup : une unité dominée qui tombe retrouve son camp
            var lost = Damage(target, dealt, fallbackCell, ignoresProtections);

            // Point faible : un coup qui a vraiment entamé les PV d'un ennemi le rend vulnérable (pas un coup
            // entièrement encaissé par ses PV temporaires).
            if (lost > 0 && target.Status == UnitStatus.Active && AreOpponents(target, attacker)
                && attacker.HasPassive(PassiveKind.WeakPoint))
                ApplyStatus(target, new StatusEffect { Status = StatusKind.Exposed, DurationMs = Rules.WeakPointExposedMs }, attacker);
            GaugePassivesOnHit(attacker, target, lost, wereOpponents);
            ChainReaction(attacker, target, wereOpponents);
        }
        if (target.Status != UnitStatus.Active)
            return true;
        if (skill.Heals)
            Heal(target, attacker.HealWith(_random.NextInt(skill.MinHeal, skill.MaxHeal)));
        if (skill.TempHp is { } tempHp)
            GrantTempHp(attacker, target, tempHp);
        // Les états de la compétence, sauf sur un robot dont l'effet renforcé les remplace (Piratage), puis ceux du robot.
        var effects = robotBonus is { ReplacesEffects: true } ? Array.Empty<StatusEffect>() : skill.Effects;
        foreach (var effect in effects)
            ApplyStatus(target, effect, attacker, robotBonus?.StatusDurationPercent ?? 100);
        // Un état qu'elle ignore (Inébranlable) : on le dit, pour un coup (pas pour un sol qui le reposerait sans cesse).
        if (effects.Any(e => ResistsStatus(target, e.Status)))
            _events.Add(new UnitResisted(CurrentTick, target.Id));
        foreach (var effect in robotBonus?.Effects ?? [])
            ApplyStatus(target, effect, attacker);
        if (skill.ResetsCooldowns)
            ResetCooldowns(target);
        StatusPassivesOnHit(attacker, skill, target);
        if (skill.TauntActions > 0 && AreOpponents(target, attacker))
            Taunt(target, attacker, skill.TauntActions);
        if (AreOpponents(target, attacker))
        {
            if (skill.InterruptsCharge)
                InterruptCharge(target);
            if (skill.GaugeLossPercent + (robotBonus?.GaugeLossPercent ?? 0) is > 0 and var gaugeLoss)
                LoseGauge(target, gaugeLoss);
        }
        if (skill.GaugeGainPercent > 0)
            GainGauge(target, Rules.GaugeAt(skill.GaugeGainPercent));
        AttackedBy(attacker, skill, target);
        return true;
    }

    /// <summary>La cible a été attaquée par un adversaire, que le coup ait porté ou non : elle peut riposter.</summary>
    private void AttackedBy(Unit attacker, SkillDefinition skill, Unit target)
    {
        if (AreOpponents(target, attacker))
            QueueRiposte(attacker, skill, target);
    }

    /// <summary>Multiplicateur des dégâts infligés par l'unité, en dix-millièmes (voir <see cref="Unit.DamageFactor"/>).</summary>
    private static int DamageFactorOf(Unit unit) => unit.DamageFactor;

    /// <summary>
    /// Dégâts finaux : tirage × bonus de l'attaquant (<paramref name="damageFactor"/>) × vulnérabilité de la cible
    /// × sa réduction Seul contre tous (qui vaut pour tous les dégâts : coups, collisions, passifs de zone),
    /// en une seule division (pas d'arrondi intermédiaire).
    /// </summary>
    private int Scaled(int damage, int damageFactor, Unit target)
    {
        var exposedPercent = target.HasStatus(StatusKind.Exposed) ? Rules.ExposedDamageTakenPercent : 100;
        var alonePercent = IsAloneAgainstAll(target) ? Rules.AloneAgainstAllDamageTakenPercent : 100;
        return (int)((long)damage * damageFactor * exposedPercent * alonePercent / 100_000_000);
    }

    /// <summary>
    /// Le multiplicateur de dégâts, augmenté par les bonus de la compétence elle-même. Prise en tenaille : si un allié du
    /// lanceur (un héros debout, pas un gadget) est lui aussi au contact de la cible. Coup de grâce : selon la part de
    /// PV qui manquent à la cible (boss compris).
    /// </summary>
    private int WithSkillBonuses(int damageFactor, Unit attacker, SkillDefinition skill, Unit target)
    {
        long factor = damageFactor;
        if (skill.FlankDamagePercent != 0
            && _units.Any(u => u != attacker && u.IsStandingCombatant && u.Team == attacker.Team && IsInMeleeReach(u, target.Position)))
            factor = factor * skill.FlankDamagePercent / 100;
        if (skill.MissingHpDamageBonusPercent != 0)
            factor = factor * (100 + (long)skill.MissingHpDamageBonusPercent * (target.MaxHp - target.Hp) / target.MaxHp) / 100;
        return (int)factor;
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
    /// l'encaisse (davantage contre un adversaire qui a Démesure, comme son bonus de dégâts) ; à 0 PV, il devient du sol. Renvoie les dégâts qui passent.
    /// </summary>
    private int AbsorbByCover(Unit attacker, Unit target, Cell cover, int damage)
    {
        var hp = _coverHp.GetValueOrDefault(cover, Rules.LowObstacleHp);
        var absorbed = Math.Min(hp, (int)((long)damage * Rules.CoverDamageReductionPercent / 100));
        if (absorbed == 0)
            return damage;
        var wear = attacker.HasPassive(PassiveKind.Excess) && AreOpponents(attacker, target)
            ? (int)((long)absorbed * Rules.ExcessCoverWearPercent / 100)
            : absorbed;
        hp -= Math.Min(hp, wear);
        _coverHp[cover] = hp;
        _events.Add(new HitCovered(CurrentTick, target.Id, cover, absorbed, hp));
        if (hp == 0)
        {
            _grid.SetTerrain(cover, Terrain.Floor);
            ForgetRaisedObstacle(cover);
            _events.Add(new CoverDestroyed(CurrentTick, cover));
        }
        return damage - absorbed;
    }

    /// <summary>
    /// Inflige des dégâts : les PV temporaires de la cible encaissent d'abord (sauf coup <paramref name="ignoresProtections"/>, qui
    /// ignore les protections), le reste entame ses PV. Renvoie les PV réellement perdus.
    /// </summary>
    private int Damage(Unit target, int amount, Cell? fallbackCell, bool ignoresProtections = false)
    {
        if (!ignoresProtections && amount > 0)
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
            // Détruite, elle explose (ex. Leurre piégé) : une réaction, après la source qui l'a détruite.
            if (target.Definition.DefeatBlast is { } blast)
            {
                var cell = target.Position;
                _pendingReactions.Enqueue(() =>
                {
                    _events.Add(new UnitBlewUp(CurrentTick, target.Id, cell));
                    DamageOpponentsAround(target, cell, blast.MinDamage, blast.MaxDamage);
                });
            }
            return;
        }

        // Une unité par case : à terre en traversant un allié, elle tombe sur la dernière case libre.
        // Sans case de repli connue (des dégâts de zone sur une unité en pleine course), la première case libre voisine.
        if (IsOccupiedByOther(target, target.Position))
            target.Position = fallbackCell ?? FreeCellNear(target)
                ?? throw new InvalidOperationException($"Unit {target.Id} fell on an occupied cell.");
        _events.Add(new UnitDowned(CurrentTick, target.Id, target.Position));
    }

    /// <summary>Une case libre voisine de l'unité (ordre fixe des voisines), ou <c>null</c>.</summary>
    private Cell? FreeCellNear(Unit unit) =>
        _grid.Neighbors(unit.Position).Where(c => !IsOccupiedByOther(unit, c)).Select(c => (Cell?)c).FirstOrDefault();

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
        ClearReactions(unit);
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
