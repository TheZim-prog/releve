namespace Game.Core.Combat;

// États (étourdi, déséquilibré, confus, dominé…) : pose, fin, changement de camp.
public sealed partial class CombatSimulation
{
    /// <summary>
    /// Applique un état posé par <paramref name="source"/>, pour sa durée (voir <see cref="PutStatus"/>).
    /// Emprise : un état néfaste posé par une unité qui a ce passif dure plus longtemps. Désarmé : dure moins longtemps
    /// sur un boss.
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
        PutStatus(target, effect.Status, CurrentTick + Rules.MillisecondsToTicks((int)durationMs), source);
    }

    /// <summary>
    /// Pose un état jusqu'à <paramref name="endTick"/>, sauf s'il ne prend pas sur elle (Inébranlable, Sérénité) ; s'il
    /// est déjà là, garde la plus longue des deux durées (et l'unité dont elle vient ; à égalité, la nouvelle). Étourdi ou désarmé : interrompt
    /// la charge. Confus ou dominé : l'unité change de décisions (et de camp, si dominée).
    /// </summary>
    private void PutStatus(Unit target, StatusKind status, int endTick, Unit source)
    {
        if (ResistsStatus(target, status))
            return;
        // À durée égale, l'état est désormais celui de la nouvelle source.
        var isNew = !target.StatusEndTicks.TryGetValue(status, out var currentEnd);
        if (isNew || endTick >= currentEnd)
            target.SetStatus(status, endTick, source.Id);
        _events.Add(new StatusApplied(CurrentTick, target.Id, status, target.StatusEndTicks[status]));
        // Récupération d'énergie : pour un ennemi (pas un gadget) qu'il étourdit, pas pour un étourdissement prolongé.
        if (isNew && status == StatusKind.Stunned && source.HasPassive(PassiveKind.EnergyRecovery)
            && AreOpponents(target, source) && target.SummonedBy is null)
            PromiseGauge(source, Rules.EnergyRecoveryGaugeGainPercent);

        switch (status)
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
                // Commandée comme celle dont vient la domination en cours (une plus courte, posée par-dessus, ne change
                // rien) ; si celle-ci est elle-même dominée (un tech dominé qui pirate un robot), comme son propre maître.
                var master = target.StatusSource(status) is { } masterId && FindUnit(masterId) is { } holder ? holder : source;
                target.DominatedBy = master.DominatedBy ?? master.Id;
                ChangeTeam(target, master.Team);
                break;
        }

        // Une intention que l'état rend impossible (compétence interdite) est abandonnée : l'unité en choisira une autre.
        if (target.Intention?.SkillId is { } intended && target.FindSkill(intended) is { } planned
            && target.StatusBlocking(planned) is not null)
            SetIntention(target, null);
    }

    /// <summary>
    /// Inébranlable : jamais déséquilibré, d'où que ça vienne (un gaz allié compris). Serein : aucun état néfaste ne prend.
    /// </summary>
    private static bool ResistsStatus(Unit target, StatusKind status) =>
        (status == StatusKind.OffBalance && target.HasPassive(PassiveKind.Unshakable))
        || (status.IsHarmful() && target.HasStatus(StatusKind.Serene));

    /// <summary>
    /// Les états arrivés à échéance prennent fin. C'est une source à part : une forteresse lâchée par une fin d'état
    /// (domination) tombe à sa fin.
    /// </summary>
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
        EndDomes();
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
        ClearReactions(unit); // ce qu'elle avait préparé pour son ancien camp ne sert plus, son dôme non plus
        RemoveDome(unit);
        unit.Team = team;
        _events.Add(new TeamChanged(CurrentTick, unit.Id, team));
    }
}
