namespace Game.Core.Combat;

// Ce qui joue sur la jauge en dehors de son remplissage normal : perte de jauge (Brouillage, Contretemps), gains des
// passifs (Sur sa lancée, Récupération d'énergie, Sillage, Voleur de temps) et auras (Commandement, Champ de brouillage).
public sealed partial class CombatSimulation
{
    /// <summary>
    /// La cible perd une part de sa jauge (en % de la capacité), dans la limite de ce qu'elle a ; moins sur un boss.
    /// Une unité prête ne l'est plus. Sans effet sur une unité qui charge (sa jauge est déjà à 0).
    /// </summary>
    private void LoseGauge(Unit target, int percent)
    {
        var amount = Rules.GaugeAt(percent);
        if (target.Definition.IsBoss)
            amount = (int)((long)amount * Rules.BossGaugeLossPercent / 100);
        var lost = Math.Min(amount, target.Gauge);
        if (lost <= 0)
            return;
        target.Gauge -= lost;
        _events.Add(new GaugeLost(CurrentTick, target.Id, lost, target.Gauge));
    }

    /// <summary>
    /// Promet de la jauge à une unité (en % de la capacité). Elle la reçoit à la fin de ce qu'elle est en train de
    /// faire (<see cref="FlushGauge"/>) : sa jauge est d'abord remise à son point de reprise, le gain vient ensuite.
    /// </summary>
    private void PromiseGauge(Unit unit, int percent) => unit.PendingGauge += Rules.GaugeAt(percent);

    /// <summary>Donne à l'unité la jauge qui lui a été promise pendant son action, sa charge ou son attaque d'opportunité.</summary>
    private void FlushGauge(Unit unit)
    {
        if (unit.PendingGauge == 0)
            return;
        var amount = unit.PendingGauge;
        unit.PendingGauge = 0;
        if (unit.Status == UnitStatus.Active)
            GainGauge(unit, amount);
    }

    /// <summary>
    /// Passifs de jauge déclenchés par un coup (ou une collision) qui a fait <paramref name="lost"/> PV de dégâts à un
    /// adversaire : Sur sa lancée (l'attaquant gagne de la jauge s'il met hors de combat un ennemi qui n'est pas un
    /// gadget), Voleur de temps (la cible perd de la jauge ; rien si elle était en plein déplacement, jauge à 0).
    /// <paramref name="wereOpponents"/> : s'ils étaient adversaires avant le coup.
    /// </summary>
    private void GaugePassivesOnHit(Unit attacker, Unit target, int lost, bool wereOpponents)
    {
        if (!wereOpponents)
            return;
        if (target.Status != UnitStatus.Active && target.SummonedBy is null && attacker.HasPassive(PassiveKind.Momentum))
            PromiseGauge(attacker, Rules.MomentumGaugeGainPercent);
        if (lost > 0 && target.Status == UnitStatus.Active && attacker.HasPassive(PassiveKind.TimeThief))
            LoseGauge(target, Rules.TimeThiefGaugeLossPercent);
    }

    /// <summary>
    /// Sillage : une unité qui a ce passif et vient de faire une action (pas un déplacement seul) donne un peu de jauge
    /// aux héros alliés debout à son contact.
    /// </summary>
    private void Slipstream(Unit unit)
    {
        if (unit.Status != UnitStatus.Active || !unit.HasPassive(PassiveKind.Slipstream))
            return;
        foreach (var ally in _units.Where(u => u != unit && u.IsStandingCombatant && u.Team == unit.Team && IsInMeleeReach(unit, u.Position)).ToList())
            GainGauge(ally, Rules.GaugeAt(Rules.SlipstreamGaugeGainPercent));
    }

    /// <summary>
    /// Auras qui changent la vitesse de jauge de l'unité, en % : Commandement d'un allié proche (plus vite), Champ de
    /// brouillage d'un adversaire proche (plus lentement). Chacune compte une fois, quel que soit le nombre de porteurs ;
    /// la distance se compte en cases, sans ligne de vue ; le porteur ne profite pas de son propre Commandement ; les
    /// gadgets ne donnent ni ne reçoivent d'aura.
    /// </summary>
    private IEnumerable<int> AuraFillPercents(Unit unit)
    {
        if (_auraBearers.Count == 0 || unit.SummonedBy is not null || unit.IsCivilian)
            yield break;
        bool Near(Unit bearer, PassiveKind passive) =>
            bearer != unit && bearer.HasPassive(passive) && bearer.Position.DistanceTo(unit.Position) <= Rules.AuraRange;

        if (_auraBearers.Any(b => b.Team == unit.Team && Near(b, PassiveKind.Command)))
            yield return Rules.CommandFillPercent;
        if (_auraBearers.Any(b => AreOpponents(b, unit) && Near(b, PassiveKind.JammingField)))
            yield return Rules.JammingFieldFillPercent;
    }

    /// <summary>Les porteurs d'aura debout, relevés une fois par tick avant le remplissage des jauges.</summary>
    private void FindAuraBearers()
    {
        _auraBearers.Clear();
        _auraBearers.AddRange(_units.Where(u => u.IsStandingCombatant && u.Passive is PassiveKind.Command or PassiveKind.JammingField));
    }
}
