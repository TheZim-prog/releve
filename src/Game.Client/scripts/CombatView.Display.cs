using Game.Core.Combat;
using Godot;

namespace Game.Client;

// Ce que la vue donne à afficher : contenu de l'interface, intentions, chiffres flottants, couleurs.
public partial class CombatView
{
    private void UpdateHud()
    {
        var seconds = _sim.CurrentTick / (double)_data.Rules.TicksPerSecond;
        var speed = _session.IsNetwork ? "" : $"   Vitesse : {Speeds[_speed]} %";
        var status = $"Temps : {seconds:0.0} s{speed}{(_session.IsPaused ? "   — PAUSE —" : "")}";
        var objectives = "Objectifs :\n" + string.Join('\n', _sim.Objectives.Select(o => Texts.ObjectiveLine(o, _sim)));
        _hud.SetStatus(status + (_showIntentions ? "\nIntentions ennemies révélées" : "") + "\n\n" + objectives);
        if (_won is null)
            _hud.SetBanner(_session.IsPaused ? _session.PauseText : "", Colors.White);

        _hud.SetHeroes(_sim.Units
            .Where(u => u.IsHero)
            .Select(HeroCardOf)
            .ToList());

        if (Selected is not { } unit)
        {
            var ready = _sim.Units.Count(u => u.IsReady && IsControllable(u));
            _hud.SetSelection(ready > 0 ? $"{ready} héros prêt(s) : choisis-en un (cartes à gauche, F1-F6 ou Tab)" : "Aucun héros prêt",
                Array.Empty<ActionButton>());
            return;
        }

        var header = $"{unit.Definition.Name} — {Texts.Hp(unit)} PV — jauge {100 * unit.Gauge / _data.Rules.GaugeCapacity} %"
                     + (HasOrder(unit.Id) ? " — ORDRE DONNÉ" : unit.IsReady ? " — PRÊT" : "")
                     + (_ghost is not null ? " — déplacement prévu" : "");
        if (_sim.ImposedOn(unit.Id) is { } imposed)
            header += $"\nAction demandée : {Texts.Imposed(imposed, _sim)}";
        var states = unit.StatusEndTicks.Keys.Select(Texts.Status).ToList();
        if (unit.Guard is { } guard)
            states.Add(Texts.Guard(guard.Against));
        if (_sim.Watches.Any(w => w.Owner == unit.Id))
            states.Add("en surveillance");
        if (states.Count > 0)
            header += "\n" + string.Join(", ", states);

        var actions = new List<ActionButton>();
        for (var i = 0; i < unit.Skills.Count; i++)
        {
            var skill = unit.Skills[i];
            var cooldown = unit.CooldownEndTick(skill.Id) - _sim.CurrentTick;
            var damage = skill.DealsDamage ? $", {DamageText(unit, skill)}" : "";
            var weight = skill.Weight == SkillWeight.Heavy ? "lourde" : "légère";
            var details = unit.StatusBlocking(skill) is { } blocked ? Texts.Rejection(blocked)
                : cooldown > 0 ? $"recharge {cooldown / (double)_data.Rules.TicksPerSecond:0.0} s"
                : $"{weight}, portée {skill.Range}{damage}";
            var key = i < 9 ? $"{i + 1}  " : "";
            actions.Add(new ActionButton($"{key}{skill.DisplayName}\n{details}",
                Enabled: true, Armed: _aiming?.Skill == skill, Cooldown: CooldownLeft(unit, skill)));
        }
        actions.Add(new ActionButton("R  Interagir\n(relever, objet)", Enabled: true, Armed: _aiming is { Skill: null }));
        actions.Add(new ActionButton($"Entrée  Déplacement seul\n{(_ghost is null ? "choisis d'abord une case" : "vers le fantôme")}",
            Enabled: _ghost is not null, Armed: false));
        _hud.SetSelection(header, actions);
    }

    private HeroCard HeroCardOf(Unit hero)
    {
        // Un héros dominé ou confus n'obéit plus : jamais « prêt » pour le joueur.
        var ready = hero.IsReady && IsControllable(hero) && !HasOrder(hero.Id);
        var statuses = string.Join(", ", hero.StatusEndTicks.Keys.Select(Texts.Status));
        var state = hero.Status == UnitStatus.Downed ? "À TERRE"
            : HasOrder(hero.Id) ? (hero.IsReady ? "ordre donné" : "ordre en attente")
            : ready ? "PRÊT"
            : hero.Charge is { Skill.HoldsDome: true } ? "tient la forteresse"
            : hero.Charge is not null ? "charge…"
            : "";
        // Les états restent visibles même prêt : un héros prêt mais immobilisé ou désarmé ne peut pas tout faire.
        if (hero.Status == UnitStatus.Active && statuses.Length > 0)
            state = state.Length > 0 ? $"{state} · {statuses}" : statuses;
        var name = _session.ControllerName(hero.Id) is { } controller ? $"{hero.Definition.Name} ({controller})" : hero.Definition.Name;
        return new HeroCard(hero.Id, name, (float)hero.Hp / hero.MaxHp, $"{Texts.Hp(hero)} PV",
            Math.Min(1f, (float)hero.Gauge / _data.Rules.GaugeCapacity), Ready: ready, Selected: hero.Id == _selected, State: state,
            SkillReadiness: hero.Skills.Select(s => 1f - CooldownLeft(hero, s)).ToList());
    }

    /// <summary>Part de la recharge de la compétence qu'il reste à attendre : 1 juste après usage, 0 quand elle est prête.</summary>
    private float CooldownLeft(Unit unit, SkillDefinition skill)
    {
        var total = _data.Rules.MillisecondsToTicks(skill.CooldownMs);
        var left = unit.CooldownEndTick(skill.Id) - _sim.CurrentTick;
        return total <= 0 || left <= 0 ? 0f : Math.Min(1f, (float)left / total);
    }

    /// <param name="revealed">Les unités dont l'Analyse tactique d'un tech proche révèle l'intention.</param>
    private string? IntentionText(Unit unit, IReadOnlySet<UnitId> revealed)
    {
        // Révélées pour toutes (touche de débogage), ou par l'Analyse tactique.
        if (unit.Intention is not { } intention || unit.Status != UnitStatus.Active || !(_showIntentions || revealed.Contains(unit.Id)))
            return null;
        // Sans compétence, sur elle-même : elle fuit, ou (prudente, à protéger) elle reste sur ses gardes.
        if (intention.SkillId is null && intention.Target == unit.Id)
            return unit.IsFleeing || unit.IsCivilian ? "→ fuit" : "→ sur ses gardes";
        var target = _sim.FindUnit(intention.Target)?.Definition.Name ?? "?";
        var skill = intention.SkillId is { } id ? unit.FindSkill(id)?.DisplayName : null;
        return skill is null ? $"→ {target}" : $"→ {skill} sur {target}";
    }

    private void Float(UnitId unit, string text, Color color, float lift = 0)
    {
        if (!_views.TryGetValue(unit, out var view))
            return;
        var label = new Label3D
        {
            Text = text,
            Modulate = color,
            FontSize = 56,
            PixelSize = 0.006f,
            OutlineSize = 10,
            NoDepthTest = true,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = view.Position + new Vector3(0, 1.4f + lift, 0),
        };
        AddChild(label);
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(label, "position", label.Position + new Vector3(0, 1f, 0), 1.0);
        tween.TweenProperty(label, "modulate:a", 0.0, 1.0);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    /// <summary>Dégâts de la compétence tels que le héros les ferait maintenant (puissance et état surpuissant compris).</summary>
    private static string DamageText(Unit unit, SkillDefinition skill)
    {
        var (min, max) = unit.DamageRange(skill);
        return $"{min}-{max}";
    }

    private static Color ColorOf(Unit unit)
    {
        if (unit.IsCivilian)
            return Civilian;
        if (unit.SummonedBy is not null)
            return unit.OriginalTeam == CombatSimulation.PlayerTeam ? HeroGadget : EnemyDefault;
        if (UnitColors.TryGetValue(unit.Definition.Id, out var color))
            return color;
        // Ce qu'on protège (personne ou structure) : la couleur des alliés, pas celle des ennemis.
        return unit.IsProtected ? Protected : EnemyDefault;
    }
}
