using Game.Core.Grids;

namespace Game.Core.Combat;

// Effet des compétences une fois validées : cible ou zone, sauvetage, invocations, attaques chargées. (Mouvements
// spéciaux : CombatSimulation.SelfMoves.cs ; déplacements forcés : CombatSimulation.ForcedMoves.cs.)
public sealed partial class CombatSimulation
{
    /// <summary>
    /// La compétence prend effet : sur sa cible, ou sur les unités debout de la zone (dans l'ordre des unités ; une
    /// unité tombée en cours de route, par une Réaction en chaîne, n'est plus touchée).
    /// <paramref name="damageFactor"/> : multiplicateur de dégâts du lanceur, en dix-millièmes (voir <see cref="DamageFactorOf"/>).
    /// <paramref name="destination"/> : case où une télékinésie pose sa cible ; <paramref name="zoneOrigin"/> : d'où part
    /// une zone ordinaire (pour la couverture ; une attaque ciblée part du lanceur) ; <paramref name="chain"/> : les
    /// maillons d'une chaîne planifiée.
    /// </summary>
    private void Execute(Unit caster, SkillDefinition skill, Unit? target, IReadOnlyList<Cell>? zone, int damageFactor,
        Cell? destination = null, Cell? zoneOrigin = null, IReadOnlyList<ChainLink>? chain = null)
    {
        // Une compétence exécutée est une « source » : un dôme bloque toute la source, pour tous ses protégés.
        if (chain is not null)
        {
            ExecuteChain(caster, skill, chain, damageFactor);
        }
        else if (skill.SelfMove is { } selfMove)
        {
            ExecuteSelfMove(caster, skill, selfMove, target, zone!, destination!.Value, damageFactor);
        }
        else if (skill.IsRescue)
        {
            Rescue(caster, skill, target!, destination!.Value);
        }
        else if (skill.Summon is not null)
        {
            _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, null));
            Summon(caster, skill.Summon, destination!.Value);
        }
        else if (zone is null)
        {
            _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, target!.Id));
            var landed = ResolveHit(caster, skill, target, damageFactor, origin: caster.Position);
            if (landed && target.Status == UnitStatus.Active && skill.ForcedMove is { } move)
                ApplyForcedMove(caster, skill, target, move, damageFactor, destination);
        }
        else
        {
            _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, null, zone));
            if (skill.CreatesDome)
            {
                RaiseDome(caster, zone);
            }
            else if (skill.Raises is { } obstacle)
            {
                RaiseObstacles(obstacle, zone);
            }
            else if (skill.HasHitEffect)
            {
                var hit = UnitsAffectedBy(caster, skill, zone).Where(u => ResolveHit(caster, skill, u, damageFactor, origin: zoneOrigin)).ToList();
                if (skill.ForcedMove is { } push)
                    PushFromZone(caster, skill, hit, push, damageFactor, zoneOrigin ?? caster.Position);
            }
            // Ce que la zone laisse sur le terrain, après ses coups : la fumée dissipée, puis sa propre zone au sol.
            if (skill.ClearsSmoke)
                ClearSmoke(zone);
            if (skill.Ground is not null)
                LayGround(caster, skill, zone);
        }
        EndSource();
    }

    /// <summary>
    /// Unités debout qu'une zone de <paramref name="caster"/> toucherait, dans l'ordre des unités : jamais le lanceur,
    /// puis tout le monde, les ennemis seulement ou les alliés seulement selon la zone ; une zone « ennemis » qui touche
    /// aussi les gadgets du lanceur (IEM) y ajoute ceux-ci, de son équipe. Les civils ne sont touchés que par les zones
    /// qui touchent tout le monde.
    /// </summary>
    internal List<Unit> UnitsAffectedBy(Unit caster, SkillDefinition skill, IReadOnlyList<Cell> zone)
    {
        var cells = zone.ToHashSet();
        var affects = skill.Zone!.Affects;
        return _units
            .Where(u => u != caster && u.Status == UnitStatus.Active && cells.Contains(u.Position))
            .Where(u => affects == ZoneTargets.All
                        || (!u.IsCivilian && (affects == ZoneTargets.Allies) == (u.Team == caster.Team))
                        || (skill.Zone.AlsoHitsOwnSummons && u.SummonedBy == caster.Id))
            .ToList();
    }

    /// <summary>
    /// Sauvetage ou transport (sans attaque d'opportunité) : l'allié est posé sur le dépôt, puis le lanceur à côté.
    /// Seul un allié ramassé à terre est marqué « sauvé ».
    /// </summary>
    private void Rescue(Unit rescuer, SkillDefinition skill, Unit ally, Cell drop)
    {
        var end = RescuerEnd(rescuer, ally, rescuer.Position, drop, skill.RescueDropRadius!.Value)!.Value;
        _events.Add(new SkillUsed(CurrentTick, rescuer.Id, skill.Id, ally.Id));
        if (!skill.CarriesStandingAlly)
        {
            _events.Add(new UnitRescued(CurrentTick, ally.Id, rescuer.Id));
            if (!ally.IsCivilian) // la marque compte pour la règle de campagne des chutes des héros
                ally.WasRescued = true;
        }
        Displace(ally, drop);
        if (rescuer.Position != end)
            Displace(rescuer, end);
    }

    /// <summary>
    /// Pose une unité invoquée sur <paramref name="cell"/>, de l'équipe du lanceur, jauge vide ; elle remplace celle
    /// du même type déjà posée par ce lanceur. Son identifiant suit le plus grand existant (déterministe). Elle
    /// profite de la puissance du lanceur (ses dégâts et ses soins), pas de ses autres points de stats.
    /// </summary>
    private void Summon(Unit owner, UnitDefinition definition, Cell cell)
    {
        foreach (var previous in _units.Where(u => u.SummonedBy == owner.Id && u.Definition.Id == definition.Id
                                                   && u.Status != UnitStatus.Removed).ToList())
            Dismiss(previous);

        var id = new UnitId(_units.Max(u => u.Id.Value) + 1);
        var unit = new Unit(definition.ToSetup(id, owner.Team, cell), Rules, owner.PowerPercent) { SummonedBy = owner.Id };
        _units.Add(unit);
        _events.Add(new UnitSummoned(CurrentTick, id, definition.Id, owner.Id, owner.Team, cell));
    }

    /// <summary>
    /// Déclenche les attaques chargées arrivées à échéance, dans l'ordre des unités. Une attaque ciblée ne touche
    /// que si la cible est encore debout, à portée et en vue ; une zone touche ce qui s'y trouve maintenant.
    /// </summary>
    private void ReleaseCharges()
    {
        foreach (var unit in _units)
        {
            // Une charge peut être interrompue par le déclenchement d'une autre, plus tôt dans la boucle.
            if (unit.Charge is not { } charge || charge.ReleaseTick > CurrentTick)
                continue;

            unit.Charge = null;
            var skill = charge.Skill;
            if (charge.Zone is not null)
                Execute(unit, skill, null, charge.Zone, charge.DamageFactor, zoneOrigin: charge.ZoneOrigin);
            else if (charge.Target is { } targetId && FindUnit(targetId) is { } target
                     && CheckTarget(unit, unit.Position, target, skill) is null)
                Execute(unit, skill, target, null, charge.DamageFactor);
            else
                _events.Add(new ChargeMissed(CurrentTick, unit.Id, skill.Id, charge.Target!.Value));

            SetGauge(unit, Rules.GaugeAt(RestartPercentAfter(skill)));
            FlushGauge(unit);
            AfterAction(unit);
            DismissIfExhausted(unit);
        }
    }

    private void InterruptCharge(Unit unit)
    {
        if (unit.Charge is not { } charge)
            return;
        unit.Charge = null;
        _events.Add(new ChargeInterrupted(CurrentTick, unit.Id, charge.Skill.Id));
    }
}
