using Game.Core.Combat;
using Game.Core.Grids;
using Godot;

namespace Game.Client;

// Entrées du joueur : clavier, souris, choix du héros, envoi des ordres.
public partial class CombatView
{
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_frozen && @event is not InputEventKey { Keycode: Key.F10 })
            return; // figée : seul « quitter » reste possible
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                Click();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                Cancel();
                break;
        }
    }

    private void HandleKey(InputEventKey input)
    {
        // Chiffres et touches F lus par position (sur un clavier AZERTY, la rangée du haut donne & é " ' …).
        var physical = input.PhysicalKeycode;
        if (physical is >= Key.Key1 and <= Key.Key9)
        {
            ArmAction((int)(physical - Key.Key1));
            return;
        }
        if (physical is >= Key.F1 and <= Key.F6)
        {
            if (_hud.HeroAt((int)(physical - Key.F1)) is { } hero)
                SelectIfControllable(hero);
            return;
        }

        // Vitesse (solo seulement) : les deux touches à droite du 0 (« ) = » en AZERTY, « - = » en QWERTY), ou le pavé.
        if (!_session.IsNetwork && (physical is Key.Equal || input.Keycode is Key.KpAdd))
        {
            _speed = Math.Min(_speed + 1, Speeds.Length - 1);
            return;
        }
        if (!_session.IsNetwork && (physical is Key.Minus || input.Keycode is Key.KpSubtract))
        {
            _speed = Math.Max(_speed - 1, 0);
            return;
        }

        switch (input.Keycode)
        {
            case Key.Space:
                _session.TogglePause(); // en réseau : pour tout le monde
                break;
            case Key.F10:
                LeaveRequested?.Invoke();
                break;
            case Key.I:
                _showIntentions = !_showIntentions;
                break;
            case Key.H:
                _hud.ShowHelp = !_hud.ShowHelp;
                break;
            case Key.F9: // F1-F6 choisissent les héros. Solo seulement : données relues, un réglage se voit aussitôt.
                if (_session.Restart() is { } restarted)
                {
                    _session = restarted;
                    StartCombat();
                }
                break;
            case Key.Escape:
                Cancel();
                break;
            case Key.Tab:
                SelectNextReady();
                break;
            // Combat figé : Entrée continue le message, sauf si un fantôme est posé (le déplacement seul part en ordre à l'avance).
            case Key.Enter or Key.KpEnter when _sim.FrozenMessage is not null && _ghost is null:
                ContinueScript();
                break;
            case Key.Enter or Key.KpEnter:
                MoveOnly();
                break;
            case Key.R:
                if (Selected is { } reviver)
                    ArmAction(reviver.Skills.Count);
                break;
        }
    }

    /// <summary>« Continuer » le message qui fige le combat (n'importe quel joueur, pour tout le monde).</summary>
    private void ContinueScript()
    {
        if (_sim.FrozenMessage is { } message)
            _session.ContinueScript(message.Number);
    }

    /// <summary>Bouton d'action n° i : compétence i, puis interagir, puis déplacement seul (le dernier).</summary>
    private void OnActionPressed(int index)
    {
        if (Selected is { } unit && index == unit.Skills.Count + 1)
            MoveOnly();
        else
            ArmAction(index);
    }

    /// <summary>
    /// Déplacement sans action, vers le fantôme. Toujours explicite (Entrée ou bouton) : un second clic sur la case ne
    /// suffit pas, pour qu'un double-clic ne fasse jamais perdre l'attaque prévue.
    /// </summary>
    private void MoveOnly()
    {
        if (Selected is { } unit && _ghost is { } ghost)
            TrySubmit(new ActivateCommand(unit.Id, ghost));
    }

    /// <summary>
    /// Arme l'action n° <paramref name="index"/> du héros choisi : une compétence (même en recharge : l'ordre attendra
    /// qu'elle soit prête), ou interagir (juste après la dernière compétence, comme le dernier bouton).
    /// </summary>
    private void ArmAction(int index)
    {
        if (Selected is not { } unit)
            return;
        if (index == unit.Skills.Count)
        {
            _aiming = Aiming.Interact;
            return;
        }
        if (index >= unit.Skills.Count)
            return;
        _aiming = new Aiming(unit.Skills[index]);
    }

    /// <summary>Choisit un héros depuis la barre ou F1-F6, seulement s'il obéit au joueur (pas dominé ni confus).</summary>
    private void SelectIfControllable(UnitId id)
    {
        if (_sim.FindUnit(id) is { } unit && IsControllable(unit))
            Select(id);
    }

    private void Click()
    {
        if (_won is not null || _hovered is not { } cell)
            return;
        var clicked = _sim.UnitAt(cell);

        if (_aiming is { } aiming && Selected is { } unit)
        {
            if (aiming.Kind == AimKind.UnitThenCell && aiming.FirstTarget is null)
            {
                if (clicked is not null)
                    _aiming = aiming with { FirstTarget = clicked.Id };
                return;
            }
            if (aiming.CommandFor(unit.Id, _ghost, cell, clicked?.Id, _sim.DeviceAt(cell) is not null) is { } command)
                TrySubmit(command);
            return;
        }

        if (clicked is not null && clicked.Id != _selected && IsControllable(clicked))
        {
            Select(clicked.Id);
            return;
        }
        if (Selected is not { } selectedUnit)
            return;
        if (cell == selectedUnit.Position)
            _ghost = null;
        else if (Reachable(selectedUnit).ContainsKey(cell))
            _ghost = cell;
    }

    private void Cancel()
    {
        if (_aiming is { FirstTarget: not null } aiming)
            _aiming = aiming with { FirstTarget = null };
        else if (_aiming is not null)
            _aiming = null;
        else if (_ghost is not null)
            _ghost = null;
        else if (_selected is { } id && HasOrder(id))
            _session.Submit(new CancelOrderCommand(id));
        else
            _selected = null;
    }

    private void Select(UnitId id)
    {
        _selected = id;
        ClearPlan(keepSelection: true);
    }

    private void SelectNextReady()
    {
        var heroes = _sim.Units.Where(u => u.IsReady && IsControllable(u) && !HasOrder(u.Id)).ToList();
        if (heroes.Count == 0)
            return;
        var current = heroes.FindIndex(u => u.Id == _selected);
        Select(heroes[(current + 1) % heroes.Count].Id);
    }

    /// <summary>
    /// Donne l'ordre (à la simulation, qui le fera partir dès que le héros pourra agir : tout de suite s'il est prêt) si
    /// rien d'autre que le temps ne l'en empêche ; sinon, dit pourquoi. Il remplace l'ordre du héros pas encore parti.
    /// </summary>
    private void TrySubmit(ActivateCommand command)
    {
        if (_sim.Check(command, ignoreTiming: true) is { } reason)
        {
            _hud.Log($"Impossible : {Texts.Rejection(reason)}");
            return;
        }
        _session.Submit(new OrderCommand(command));
        // Le héros reste choisi tant que son ordre n'est pas parti (clic droit pour l'annuler après une erreur de clic) ;
        // l'ordre s'affiche figé et plus rien ne suit la souris.
        ClearPlan(keepSelection: true);
    }

    private bool HasOrder(UnitId unit) => _orders.Any(o => o.Unit == unit);

    private void ClearPlan(bool keepSelection)
    {
        if (!keepSelection)
            _selected = null;
        _ghost = null;
        _aiming = null;
    }

    private bool IsControllable(Unit unit) => _session.CanCommand(unit);

    private Unit? Selected => _selected is { } id ? _sim.FindUnit(id) : null;

    private IReadOnlyDictionary<Cell, int> Reachable(Unit unit) => _sim.ReachableCells(unit.Id);

    private Cell? HoveredCell()
    {
        var mouse = GetViewport().GetMousePosition();
        var origin = _camera.ProjectRayOrigin(mouse);
        var direction = _camera.ProjectRayNormal(mouse);
        if (direction.Y > -1e-4f)
            return null;
        var cell = BoardView.ToCell(origin + direction * (-origin.Y / direction.Y));
        return _sim.Grid.Contains(cell) ? cell : null;
    }
}
