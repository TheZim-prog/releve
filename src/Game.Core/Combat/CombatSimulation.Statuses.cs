namespace Game.Core.Combat;

// États (étourdi, déséquilibré, confus, dominé…) : pose, fin, changement de camp.
public sealed partial class CombatSimulation
{
    /// <summary>
    /// Applique un état posé par <paramref name="source"/> ; s'il est déjà là, garde la plus longue des deux durées.
    /// Emprise : un état néfaste posé par une unité qui a ce passif dure plus longtemps.
    /// Étourdi ou désarmé : interrompt la charge. Confus ou dominé : l'unité change de décisions (et de camp, si
    /// dominée). Désarmé : dure moins longtemps sur un boss.
    /// </summary>
    private void ApplyStatus(Unit target, StatusEffect effect, Unit source, int durationPercent = 100)
    {
        // Tous les multiplicateurs de durée (robot, Emprise) en une seule division.
        long durationMs = (long)effect.DurationMs * durationPercent, scale = 100;
        if (effect.Status.IsHarmful() && source.HasPassive(PassiveKind.Grip))
        {
            durationMs *= Rules.GripDurationPercent;
            scale *= 100;
        }
        if (effect.Status == StatusKind.Disarmed && target.Definition.IsBoss)
        {
            durationMs *= Rules.DisarmedOnBossDurationPercent;
            scale *= 100;
        }
        durationMs /= scale;
        var endTick = CurrentTick + Rules.MillisecondsToTicks((int)durationMs);
        if (target.StatusEndTicks.TryGetValue(effect.Status, out var currentEnd))
            endTick = Math.Max(endTick, currentEnd);

        var isNew = !target.HasStatus(effect.Status);
        target.SetStatus(effect.Status, endTick);
        _events.Add(new StatusApplied(CurrentTick, target.Id, effect.Status, endTick));
        // Récupération d'énergie : pour un ennemi (pas un gadget) qu'il étourdit, pas pour un étourdissement prolongé.
        if (isNew && effect.Status == StatusKind.Stunned && source.HasPassive(PassiveKind.EnergyRecovery)
            && AreOpponents(target, source) && target.SummonedBy is null)
            PromiseGauge(source, Rules.EnergyRecoveryGaugeGainPercent);

        switch (effect.Status)
        {
            case StatusKind.Stunned or StatusKind.Disarmed:
                InterruptCharge(target);
                break;
            case StatusKind.Confused:
                InterruptCharge(target);
                SetIntention(target, null);
                break;
            case StatusKind.Dominated:
                InterruptCharge(target);
                SetIntention(target, null);
                target.DominatedBy = source.Id;
                ChangeTeam(target, source.Team);
                break;
        }

        // Une intention que l'état rend impossible (compétence interdite) est abandonnée : l'unité en choisira une autre.
        if (target.Intention?.SkillId is { } intended && target.FindSkill(intended) is { } planned
            && target.StatusBlocking(planned) is not null)
            SetIntention(target, null);
    }

    private void ExpireStatuses()
    {
        foreach (var unit in _units)
        {
            if (unit.StatusEndTicks.Count == 0)
                continue;

            var expired = unit.StatusEndTicks.Where(s => s.Value <= CurrentTick).Select(s => s.Key).ToList();
            foreach (var status in expired)
                EndStatus(unit, status);
        }
    }

    private void EndStatus(Unit unit, StatusKind status)
    {
        var wasReady = unit.IsReady;
        unit.ClearStatus(status);
        _events.Add(new StatusEnded(CurrentTick, unit.Id, status));

        // Fin d'une confusion ou d'une domination : l'unité retrouve son camp et ses propres décisions ; ce qu'elle
        // avait lancé sous contrôle (intention, charge) est perdu.
        if (status is StatusKind.Confused or StatusKind.Dominated)
        {
            SetIntention(unit, null);
            InterruptCharge(unit);
        }
        if (status == StatusKind.Dominated)
        {
            unit.DominatedBy = null;
            ChangeTeam(unit, unit.OriginalTeam);
        }

        if (!wasReady && unit.IsReady)
            _events.Add(new UnitReady(CurrentTick, unit.Id));
    }

    private void ChangeTeam(Unit unit, int team)
    {
        if (unit.Team == team)
            return;
        ClearReactions(unit); // ce qu'elle avait préparé pour son ancien camp ne sert plus
        unit.Team = team;
        _events.Add(new TeamChanged(CurrentTick, unit.Id, team));
    }
}
