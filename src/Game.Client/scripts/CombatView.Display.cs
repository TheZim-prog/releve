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
        _hud.SetStatus(status + (_showIntentions ? "\nIntentions ennemies révélées" : ""));
        if (_won is null)
            _hud.SetBanner(_session.IsPaused ? _session.PauseText : "", Colors.White);

        _hud.SetHeroes(_sim.Units
            .Where(u => u.OriginalTeam == PlayerTeam && u.SummonedBy is null && !u.IsCivilian)
            .Select(HeroCardOf)
            .ToList());

        if (Selected is not { } unit)
        {
            var ready = _sim.Units.Count(u => u.IsReady && IsControllable(u));
            _hud.SetSelection(ready > 0 ? $"{ready} héros prêt(s) : choisis-en un (cartes à gauche, F1-F6 ou Tab)" : "Aucun héros prêt",
                Array.Empty<ActionButton>());
            return;
        }

        var header = $"{unit.Definition.Name} — {unit.Hp}/{unit.MaxHp} PV — jauge {100 * unit.Gauge / _data.Rules.GaugeCapacity} %"
                     + (HasOrder(unit.Id) ? " — ORDRE DONNÉ" : unit.IsReady ? " — PRÊT" : "")
                     + (_ghost is not null ? " — déplacement prévu" : "");
        if (unit.StatusEndTicks.Count > 0)
            header += "\n" + string.Join(", ", unit.StatusEndTicks.Keys.Select(Texts.Status));

        var actions = new List<ActionButton>();
        for (var i = 0; i < unit.Skills.Count; i++)
        {
            var skill = unit.Skills[i];
            var cooldown = unit.CooldownEndTick(skill.Id) - _sim.CurrentTick;
            var damage = skill.DealsDamage ? $", {skill.MinDamage}-{skill.MaxDamage}" : "";
            var weight = skill.Weight == SkillWeight.Heavy ? "lourde" : "légère";
            var details = cooldown > 0
                ? $"recharge {cooldown / (double)_data.Rules.TicksPerSecond:0.0} s"
                : $"{weight}, portée {skill.Range}{damage}";
            var key = i < 9 ? $"{i + 1}  " : "";
            actions.Add(new ActionButton($"{key}{skill.DisplayName}\n{details}",
                Enabled: true, Armed: _aiming?.Skill == skill, Cooldown: CooldownLeft(unit, skill)));
        }
        actions.Add(new ActionButton("R  Relever\nun allié au contact", Enabled: true, Armed: _aiming?.Interaction is not null));
        actions.Add(new ActionButton($"Entrée  Déplacement seul\n{(_ghost is null ? "choisis d'abord une case" : "vers le fantôme")}",
            Enabled: _ghost is not null, Armed: false));
        _hud.SetSelection(header, actions);
    }

    private HeroCard HeroCardOf(Unit hero)
    {
        // Un héros dominé ou confus n'obéit plus : jamais « prêt » pour le joueur.
        var ready = hero.IsReady && IsControllable(hero) && !HasOrder(hero.Id);
        var state = hero.Status == UnitStatus.Downed ? "À TERRE"
            : HasOrder(hero.Id) ? (hero.IsReady ? "ordre donné" : "ordre en attente")
            : ready ? "PRÊT"
            : hero.Charge is not null ? "charge…"
            : string.Join(", ", hero.StatusEndTicks.Keys.Select(Texts.Status));
        var name = _session.ControllerName(hero.Id) is { } controller ? $"{hero.Definition.Name} ({controller})" : hero.Definition.Name;
        return new HeroCard(hero.Id, name, (float)hero.Hp / hero.MaxHp, $"{hero.Hp}/{hero.MaxHp} PV",
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

    private string? IntentionText(Unit unit)
    {
        if (!_showIntentions || unit.Intention is not { } intention || unit.Status != UnitStatus.Active)
            return null;
        if (intention.SkillId is null && intention.Target == unit.Id)
            return "→ fuit";
        var target = _sim.FindUnit(intention.Target)?.Definition.Name ?? "?";
        var skill = intention.SkillId is { } id ? unit.FindSkill(id)?.DisplayName : null;
        return skill is null ? $"→ {target}" : $"→ {skill} sur {target}";
    }

    private void Float(UnitId unit, string text, Color color)
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
            Position = view.Position + new Vector3(0, 1.4f, 0),
        };
        AddChild(label);
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(label, "position", label.Position + new Vector3(0, 1f, 0), 1.0);
        tween.TweenProperty(label, "modulate:a", 0.0, 1.0);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    private static Color ColorOf(Unit unit)
    {
        if (unit.IsCivilian)
            return Civilian;
        if (unit.SummonedBy is not null)
            return unit.OriginalTeam == PlayerTeam ? HeroGadget : EnemyDefault;
        return UnitColors.TryGetValue(unit.Definition.Id, out var color) ? color : EnemyDefault;
    }
}
