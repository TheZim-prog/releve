using Game.Core.Grids;

namespace Game.Core.Combat;

// Passifs de dégâts : ceux qui changent ce qu'un coup inflige ou ce qu'une cible encaisse, et ceux qui ajoutent un
// effet à un coup ou à une action. (Les passifs de jauge sont dans CombatSimulation.Gauges.cs.)
public sealed partial class CombatSimulation
{
    /// <summary>
    /// Un coup est « à distance » quand l'attaquant n'est pas au contact de sa cible au moment où il la touche, zones
    /// comprises. Un mouvement spécial (Percée, Saut, Charge) est toujours un coup au contact.
    /// </summary>
    private static bool IsRangedHit(Unit attacker, SkillDefinition skill, Unit target) =>
        skill.SelfMove is null && attacker.Position != target.Position && !attacker.Position.IsAdjacentTo(target.Position);

    /// <summary>
    /// Le multiplicateur de dégâts d'un coup, corrigé par le passif de l'attaquant contre un adversaire (Rage,
    /// Opportuniste, Premier sang, Embusqué, Démesure) et par l'Écran cinétique de la cible. (Seul contre tous joue sur tous les
    /// dégâts : voir <see cref="Scaled"/>.)
    /// </summary>
    private int WithDamagePassives(int damageFactor, Unit attacker, SkillDefinition skill, Unit target)
    {
        long factor = damageFactor;
        void Times(int percent) => factor = factor * percent / 100;

        if (AreOpponents(attacker, target))
        {
            switch (attacker.Passive)
            {
                // Plus ses PV sont bas, plus il frappe fort : le bonus grandit avec la part de PV perdue.
                case PassiveKind.Rage:
                    Times(100 + (int)((long)Rules.RageMaxDamageBonusPercent * (attacker.MaxHp - attacker.Hp) / attacker.MaxHp));
                    break;
                case PassiveKind.Opportunist when target.HasStatus(StatusKind.Stunned) || target.HasStatus(StatusKind.OffBalance)
                                                  || target.HasStatus(StatusKind.Immobilized):
                    Times(Rules.OpportunistDamagePercent);
                    break;
                // Intact : tous ses PV, et rien qui les protège encore (sinon chaque coup encaissé resterait « le premier »).
                case PassiveKind.FirstBlood when target.Hp == target.MaxHp && target.TempHp == 0:
                    Times(Rules.FirstBloodDamagePercent);
                    break;
                case PassiveKind.Ambusher when IsRangedHit(attacker, skill, target) && IsAgainstScenery(attacker.Position):
                    Times(Rules.AmbusherDamagePercent);
                    break;
                case PassiveKind.Excess:
                    Times(Rules.ExcessDamagePercent);
                    break;
            }
        }
        if (target.HasPassive(PassiveKind.KineticScreen) && IsRangedHit(attacker, skill, target))
            Times(Rules.KineticScreenDamageTakenPercent);
        return (int)factor;
    }

    /// <summary>Seul contre tous : la cible a ce passif et au moins deux adversaires (pas des gadgets) à son contact.</summary>
    private bool IsAloneAgainstAll(Unit target) =>
        target.HasPassive(PassiveKind.AloneAgainstAll)
        && _units.Count(u => u.IsStandingCombatant && AreOpponents(u, target) && IsInMeleeReach(u, target.Position)) >= 2;

    /// <summary>Collé au décor : une case voisine (diagonales comprises) est un mur ou un obstacle bas.</summary>
    private bool IsAgainstScenery(Cell cell)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var next = new Cell(cell.X + dx, cell.Y + dy);
                // Un objet de mission n'est pas du décor : on ne s'y embusque pas (il ne couvre pas non plus).
                if (next != cell && _grid.Contains(next) && _grid.TerrainAt(next) is Terrain.Wall or Terrain.LowObstacle)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Passifs qui ajoutent un état à un coup qui fait des dégâts, entre adversaires encore debout : Givre (l'attaque de
    /// base de l'attaquant ralentit la cible) et Carapace de givre (qui frappe la cible au contact, sans zone, est ralenti).
    /// </summary>
    private void StatusPassivesOnHit(Unit attacker, SkillDefinition skill, Unit target)
    {
        if (!skill.IsOffensive || !skill.DealsDamage || !AreOpponents(target, attacker))
            return;
        if (attacker.HasPassive(PassiveKind.Frost) && skill == attacker.BasicAttack)
            ApplyStatus(target, new StatusEffect { Status = StatusKind.OffBalance, DurationMs = Rules.FrostDurationMs }, attacker);
        if (target.HasPassive(PassiveKind.FrostShell) && attacker.Status == UnitStatus.Active && skill.Zone is null
            && !IsRangedHit(attacker, skill, target))
            ApplyStatus(attacker, new StatusEffect { Status = StatusKind.OffBalance, DurationMs = Rules.FrostShellDurationMs }, target);
    }

    /// <summary>
    /// Réaction en chaîne : un ennemi (pas un gadget) que l'attaquant vient de mettre hors de combat explose et blesse
    /// les adversaires de l'attaquant au contact de sa case ; ceux qui en tombent explosent à leur tour.
    /// </summary>
    private void ChainReaction(Unit attacker, Unit fallen, bool wereOpponents)
    {
        if (!wereOpponents || fallen.Status == UnitStatus.Active || fallen.SummonedBy is not null
            || !attacker.HasPassive(PassiveKind.ChainReaction))
            return;
        var explosions = new Queue<Cell>();
        explosions.Enqueue(fallen.Position);
        while (explosions.TryDequeue(out var center))
        {
            _events.Add(new ChainExplosion(CurrentTick, center, attacker.Id));
            foreach (var victim in DamageOpponentsAround(attacker, center, Rules.ChainReactionMinDamage, Rules.ChainReactionMaxDamage))
            {
                if (victim.Status != UnitStatus.Active && victim.SummonedBy is null)
                    explosions.Enqueue(victim.Position);
            }
        }
    }

    /// <summary>Ce qui suit une action réellement faite (pas un déplacement seul) : Sillage, Fournaise et Présence.</summary>
    private void AfterAction(Unit unit)
    {
        Slipstream(unit);
        Furnace(unit);
        Presence(unit);
    }

    /// <summary>Fournaise : après chacune de ses actions, les adversaires debout à son contact prennent de petits dégâts.</summary>
    private void Furnace(Unit unit)
    {
        if (unit.Status != UnitStatus.Active || !unit.HasPassive(PassiveKind.Furnace))
            return;
        DamageOpponentsAround(unit, unit.Position, Rules.FurnaceMinDamage, Rules.FurnaceMaxDamage);
        EndSource();
    }

    /// <summary>
    /// Dégâts d'un passif de zone (Fournaise, Réaction en chaîne) : chaque adversaire de <paramref name="source"/> debout
    /// au contact de <paramref name="center"/> prend un tirage multiplié par sa puissance. Un dôme n'en protège pas.
    /// Renvoie les unités touchées.
    /// </summary>
    private List<Unit> DamageOpponentsAround(Unit source, Cell center, int minDamage, int maxDamage)
    {
        var victims = _units.Where(u => u.Status == UnitStatus.Active && AreOpponents(u, source) && IsInMeleeReach(center, u.Position)).ToList();
        foreach (var victim in victims)
            Damage(victim, Scaled(_random.NextInt(minDamage, maxDamage), source.DamageFactor, victim), fallbackCell: null);
        return victims;
    }
}
