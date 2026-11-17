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
        Cell? origin = null) =>
        ResolveHit(attacker, skill, target, damageFactor, out _, fallbackCell, origin);

    /// <summary>Le même coup ; <paramref name="hpLost"/> : les PV que la cible a réellement perdus (pour un Siphon).</summary>
    private bool ResolveHit(Unit attacker, SkillDefinition skill, Unit target, int damageFactor, out int hpLost,
        Cell? fallbackCell = null, Cell? origin = null)
    {
        hpLost = 0;
        // Tombée plus tôt dans la même compétence (une Réaction en chaîne au milieu d'une zone) : elle n'est pas refrappée.
        if (target.Status != UnitStatus.Active)
            return false;
        // Un coup ennemi, qu'il porte ou non (esquivé, bloqué, absorbé, sans dégâts) : elle a été attaquée.
        if (AreOpponents(attacker, target))
            target.AttackedSinceActivation = true;
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

        var profile = HitProfileOn(attacker, skill, target);
        if (profile.DealsDamage)
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
                var roll = profile.Damage is { } damage ? _random.NextInt(damage.Min, damage.Max) : 0;
                if (profile.ExtraDamage is { } extra)
                    roll += _random.NextInt(extra.Min, extra.Max);
                var factor = WithDamagePassives(WithSkillBonuses(damageFactor, attacker, skill, target), attacker, skill, target);
                dealt = Scaled(roll, factor, target);
                // Un pouvoir mental ne vole pas jusqu'à sa cible : pas de couverture.
                if (origin is { } from && !skill.IsMental && !attacker.HasPassive(PassiveKind.Piercing)
                    && CoverFor(from, target.Position) is { } cover)
                    dealt = AbsorbByCover(attacker, target, cover, dealt);
            }
            var wereOpponents = AreOpponents(target, attacker); // avant le coup : une unité dominée qui tombe retrouve son camp
            var lost = hpLost = Damage(target, dealt, fallbackCell, ignoresProtections);

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
            HealFrom(attacker, skill, target);
        if (skill.TempHp is { } tempHp)
            GrantTempHp(attacker, target, tempHp);
        if (skill.ClearsHarmfulStatuses)
            ClearHarmfulStatuses(target);
        foreach (var effect in profile.Effects)
            ApplyStatus(target, effect, attacker, profile.DurationPercent);
        // Un état qu'elle ignore (Inébranlable, Sérénité) : on le dit, pour un coup (pas pour un sol qui le reposerait sans cesse).
        if (profile.Effects.Any(e => ResistsStatus(target, e.Status)))
            _events.Add(new UnitResisted(CurrentTick, target.Id));
        foreach (var effect in profile.ExtraEffects)
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
            if (profile.GaugeLossPercent > 0)
                LoseGauge(target, profile.GaugeLossPercent);
        }
        if (skill.GaugeGainPercent > 0)
            GainGauge(target, Rules.GaugeAt(skill.GaugeGainPercent));
        AttackedBy(attacker, skill, target);
        return true;
    }

    /// <summary>
    /// Ce que le coup fait à cette cible (voir <see cref="HitProfile"/>) : l'effet renforcé de la compétence contre un
    /// robot adverse (pas contre les gadgets du lanceur pris dans son IEM, étourdis comme les ennemis ordinaires) ; ce
    /// qu'une domination fait à un boss.
    /// </summary>
    private HitProfile HitProfileOn(Unit attacker, SkillDefinition skill, Unit target) =>
        HitProfile.For(skill,
            robot: target.Definition.IsRobot && AreOpponents(target, attacker) ? skill.VsRobots : null,
            boss: target.Definition.IsBoss ? skill.VsBosses : null);

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

    /// <summary>
    /// La cible perd des PV ; à 0, elle est mise hors de combat (à terre ou retirée), sauf si un Ange gardien la retient
    /// à 1 PV. En tombant, elle passe ses états contagieux à l'adversaire le plus proche (Contagion).
    /// </summary>
    private void LoseHp(Unit target, int amount, Cell? fallbackCell)
    {
        var angel = target.Hp - amount <= 0 ? GuardianAngelFor(target) : null;
        target.Hp = angel is null ? Math.Max(0, target.Hp - amount) : 1;
        _events.Add(new UnitDamaged(CurrentTick, target.Id, amount, target.Hp));
        if (angel is not null)
            SavedByGuardianAngel(target, angel);
        if (target.Hp > 0)
        {
            if (!target.IsFleeing && target.Definition.FleeAtHpPercent is { } percent
                && (long)target.Hp * 100 < (long)target.MaxHp * percent)
                StartFleeing(target);
            return;
        }

        var contagious = ContagiousStatuses(target);
        Fall(target, fallbackCell);
        SpreadContagion(target, contagious);
    }

    /// <summary>
    /// La cible, à 0 PV, est mise hors de combat : retirée (et elle explose, si elle le fait), ou à terre sur sa case.
    /// <paramref name="fallbackCell"/> : où elle tombe si elle partage sa case (en traversant un allié).
    /// </summary>
    private void Fall(Unit target, Cell? fallbackCell)
    {
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
        QueueMinionsReaction(unit);
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

    /// <summary>Retire un gadget remplacé ou épuisé.</summary>
    private void Dismiss(Unit unit) => Withdraw(unit, new UnitRemoved(CurrentTick, unit.Id));

    /// <summary>
    /// Retire l'unité du combat sans qu'elle soit vaincue (gadget retiré, unité enfuie ou rendue) ; <paramref name="announcement"/>
    /// dit comment.
    /// </summary>
    private void Withdraw(Unit unit, CombatEvent announcement)
    {
        TakeOutOfAction(unit, UnitStatus.Removed);
        RemoveDome(unit);
        _events.Add(announcement);
    }

    /// <summary>Soigne une unité debout, sans dépasser son maximum. Renvoie les PV rendus.</summary>
    private int Heal(Unit unit, int amount)
    {
        var healed = Math.Min(amount, unit.MaxHp - unit.Hp);
        if (healed <= 0)
            return 0;
        unit.Hp += healed;
        _events.Add(new UnitHealed(CurrentTick, unit.Id, healed, unit.Hp));
        return healed;
    }
}
