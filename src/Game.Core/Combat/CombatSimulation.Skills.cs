using Game.Core.Grids;

namespace Game.Core.Combat;

// Effet des compétences une fois validées : zones, mouvements spéciaux, sauvetage, invocations, déplacements
// forcés, attaques chargées.
public sealed partial class CombatSimulation
{
    /// <summary>
    /// La compétence prend effet : sur sa cible, ou sur les unités debout de la zone (dans l'ordre des unités ;
    /// un coup ne touche que sa cible, donc la liste ne change pas en cours de route).
    /// <paramref name="damageFactor"/> : multiplicateur de dégâts du lanceur, en dix-millièmes (voir <see cref="DamageFactorOf"/>).
    /// <paramref name="destination"/> : case où une télékinésie pose sa cible ; <paramref name="zoneOrigin"/> : d'où part
    /// une zone ordinaire (pour la couverture ; une attaque ciblée part du lanceur).
    /// </summary>
    private void Execute(Unit caster, SkillDefinition skill, Unit? target, IReadOnlyList<Cell>? zone, int damageFactor,
        Cell? destination = null, Cell? zoneOrigin = null)
    {
        // Une compétence exécutée est une « source » : un dôme bloque toute la source, pour tous ses protégés.
        if (skill.SelfMove is { } selfMove)
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
            {
                if (move.Type == ForcedMoveType.Push)
                    Push(caster, skill, target, move, damageFactor);
                else
                    Displace(target, destination!.Value);
            }
        }
        else
        {
            _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, null, zone));
            if (skill.CreatesDome)
            {
                RaiseDome(caster, zone);
            }
            else
            {
                foreach (var unit in UnitsAffectedBy(caster, skill, zone))
                    ResolveHit(caster, skill, unit, damageFactor, origin: zoneOrigin);
            }
        }
        EndSource();
    }

    /// <summary>
    /// Mouvement spécial planifié par <see cref="PlanSelfMove"/> (sans attaque d'opportunité) :
    /// Percée = course puis coups sur tout ce qui a été traversé ; Saut = atterrissage puis zone ;
    /// Charge = course jusqu'à l'unité percutée, puis le coup (aucun si le décor l'a arrêtée).
    /// </summary>
    private void ExecuteSelfMove(Unit caster, SkillDefinition skill, SelfMoveType selfMove, Unit? struck,
        IReadOnlyList<Cell>? cells, Cell destination, int damageFactor)
    {
        switch (selfMove)
        {
            case SelfMoveType.Pierce:
            {
                var course = cells!;
                _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, null, course));
                var crossed = course.Take(course.Count - 1).ToHashSet();
                var hit = _units.Where(u => u != caster && u.Status == UnitStatus.Active && !u.IsCivilian
                                            && crossed.Contains(u.Position)).ToList();
                Displace(caster, destination);
                foreach (var unit in hit)
                    ResolveHit(caster, skill, unit, damageFactor);
                break;
            }
            case SelfMoveType.Leap:
                _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, null, cells));
                Displace(caster, destination);
                foreach (var unit in UnitsAffectedBy(caster, skill, cells!))
                    ResolveHit(caster, skill, unit, damageFactor, origin: destination); // l'onde part de l'atterrissage
                break;
            case SelfMoveType.Rush:
                // Même ordre que les autres : la compétence, la course, puis le coup (s'il a percuté quelqu'un).
                _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, struck?.Id));
                if (destination != caster.Position)
                    Displace(caster, destination);
                if (struck is not null)
                    ResolveHit(caster, skill, struck, damageFactor);
                break;
        }
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

    /// <summary>Sauvetage (sans attaque d'opportunité) : l'allié est posé sur le dépôt, puis le lanceur à côté.</summary>
    private void Rescue(Unit rescuer, SkillDefinition skill, Unit ally, Cell drop)
    {
        var end = RescuerEnd(rescuer, ally, rescuer.Position, drop, skill.RescueDropRadius!.Value)!.Value;
        _events.Add(new SkillUsed(CurrentTick, rescuer.Id, skill.Id, ally.Id));
        _events.Add(new UnitRescued(CurrentTick, ally.Id, rescuer.Id));
        if (!ally.IsCivilian) // la marque compte pour la règle de campagne des chutes des héros
            ally.WasRescued = true;
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
    /// Projection : pousse <paramref name="target"/> en ligne droite, depuis <paramref name="origin"/>, sur la distance
    /// prévue. Elle s'arrête avant une unité, ou avant un mur, le bord ou une diagonale bloquée (même règle de coupe
    /// d'angle qu'un déplacement de la cible) ; une collision fait des dégâts à la cible et à l'unité percutée si
    /// elle est debout.
    /// </summary>
    private void Push(Unit caster, SkillDefinition skill, Unit target, ForcedMoveDefinition move, int damageFactor)
    {
        var origin = caster.Position;
        var step = (X: Math.Sign(target.Position.X - origin.X), Y: Math.Sign(target.Position.Y - origin.Y));
        if (step == (0, 0))
            return;

        var position = target.Position;
        var collided = false;
        Unit? obstacle = null;
        for (var i = 0; i < move.Distance; i++)
        {
            var next = new Cell(position.X + step.X, position.Y + step.Y);
            // Le décor (ou un coin bouché) arrête d'abord : on ne percute pas une unité à travers un coin de mur.
            if (!CanStepIgnoringDestination(target, position, next, downedBlockCorners: true))
            {
                collided = true;
                break;
            }
            if (UnitAt(next) is { } other && other != target)
            {
                collided = true;
                obstacle = other;
                break;
            }
            position = next;
        }

        if (position != target.Position)
            Displace(target, position);
        if (!collided)
            return;

        _events.Add(new UnitCollided(CurrentTick, target.Id, obstacle?.Id));
        if (move.CollisionMaxDamage == 0)
            return;
        foreach (var hit in new[] { target, obstacle })
        {
            // La cible (projetée sous un dôme de son équipe) ou l'unité percutée peuvent être protégées :
            // la collision fait partie de la même source.
            // Un civil percuté n'est pas blessé (seules les zones les touchent).
            if (hit is { Status: UnitStatus.Active, IsCivilian: false } && !IsBlockedByDome(skill, hit))
            {
                var damage = _random.NextInt(move.CollisionMinDamage, move.CollisionMaxDamage);
                Damage(hit, Scaled(damage, damageFactor, hit), fallbackCell: null, piercing: skill.IgnoresProtections);
            }
        }
    }

    /// <summary>Déplacement forcé (sans chemin ni attaque d'opportunité) ; il interrompt une charge en cours.</summary>
    private void Displace(Unit unit, Cell to)
    {
        var from = unit.Position;
        unit.Position = to;
        _events.Add(new UnitDisplaced(CurrentTick, unit.Id, from, to));
        InterruptCharge(unit);
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
