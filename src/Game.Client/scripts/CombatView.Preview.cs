using Game.Core.Combat;
using Game.Core.Grids;
using Godot;

namespace Game.Client;

// Prévisualisation au sol : cases atteignables, chemin, fantôme, portée, zone visée, ordres donnés.
public partial class CombatView
{
    private void UpdateOverlays()
    {
        _board.SetLayer("charges", _sim.Units.Where(u => u.Charge?.Zone is not null).SelectMany(u => u.Charge!.Zone!),
            new Color(1f, 0.15f, 0.1f, 0.45f), 0.01f);
        _board.SetLayer("domes", _domes.Values.SelectMany(c => c), new Color(0.3f, 0.6f, 1f, 0.35f), 0.015f);
        ShowGroundZones();

        var unit = Selected is { Status: UnitStatus.Active } selected ? selected : null;
        var from = _ghost ?? unit?.Position;
        IEnumerable<Cell> reachable = Array.Empty<Cell>(), path = Array.Empty<Cell>(), range = Array.Empty<Cell>(),
            aim = Array.Empty<Cell>(), target = Array.Empty<Cell>();
        Cell? ghostCell = _ghost;
        var hint = "";
        var hintColor = Colors.White;

        if (unit is not null && _aiming is null && _ghost is null && OrderOf(unit.Id) is { } order)
        {
            // Ordre donné, pas encore parti : seul l'ordre figé s'affiche, rien ne suit la souris. S'il est devenu
            // impossible entre-temps, on le dit (il sera annulé au départ s'il l'est encore).
            (hint, hintColor) = _sim.Check(order, ignoreTiming: true) is { } broken
                ? ($"Ordre donné, mais impossible pour l'instant ({Texts.Rejection(broken)}) : clic droit / Échap pour l'annuler",
                    new Color(1f, 0.5f, 0.45f))
                : ("Ordre donné : clic droit / Échap pour l'annuler, ou prépare-en un autre pour le remplacer", OrderHint);
        }
        else if (unit is not null && _aiming is null)
        {
            var cells = Reachable(unit);
            reachable = cells.Keys.Where(c => c != unit.Position);
            var destination = _ghost ?? (_hovered is { } h && cells.ContainsKey(h) ? h : null);
            if (destination is { } d && d != unit.Position)
            {
                path = _sim.PathFor(unit.Id, d) ?? [];
                ghostCell = d;
            }
            hint = !unit.CanMove ? "Immobilisé : un déplacement ne partira qu'à la fin de l'état ; les compétences sur place restent possibles"
                : !unit.IsReady ? "Jauge pas pleine : prépare ton ordre, il partira dès qu'elle sera pleine"
                : _ghost is null ? "Clique une case pour prévoir un déplacement, ou choisis directement une compétence (1-9)"
                : "Déplacement prévu : choisis une compétence (1-9), ou Entrée pour te déplacer seulement";
            if (_ghost is not null)
                hintColor = OrderHint;
        }
        else if (unit is not null && _aiming is { } aiming && from is { } origin)
        {
            if (aiming.Skill is { } skill && aiming.Kind != AimKind.Self)
            {
                // Portée de l'étape en cours : la cible, puis, en deux temps, la case (autour de la cible pour une
                // télékinésie, du lanceur pour le dépôt d'un sauvetage ou le repli d'une Retraite).
                var (center, radius) = aiming.FirstTarget is { } first && _sim.FindUnit(first) is { } chosen
                    ? skill.ForcedMove is { } move ? (chosen.Position, move.Distance) : (origin, skill.RescueDropRadius ?? skill.Range)
                    : (origin, skill.Range);
                // Le repli d'une Retraite se fait à pied : les cases où le héros peut vraiment aller.
                range = aiming.FirstTarget is not null && skill.SelfMoveOptions is { RetreatDistance: > 0 } retreat
                    ? _sim.WalkableCells(unit.Id, origin, retreat.RetreatDistance).Keys.Where(c => c != origin)
                    : Cells(c => c.DistanceTo(center) <= radius && c != center);
            }
            (aim, target, hint, hintColor) = AimPreview(unit, aiming, origin);
        }

        _board.SetLayer("reachable", reachable, new Color(0.3f, 0.6f, 1f, 0.25f), 0.02f);
        _board.SetLayer("range", range, new Color(1f, 1f, 1f, 0.15f), 0.02f);
        _board.SetLayer("path", path, _ghost is null ? PathHovered : PathPlaced, 0.025f);
        _board.SetLayer("aim", aim, AimColor, 0.03f);
        _board.SetLayer("target", target, TargetColor, 0.035f);
        _ghostView.Visible = ghostCell is not null;
        _ghostView.MaterialOverride = _ghost is null ? GhostHovered : GhostPlaced;
        if (ghostCell is { } g)
            _ghostView.Position = BoardView.ToWorld(g, 0.65f);
        ShowOrders(_orders);
        if (hint.Length == 0 && _orders.Count > 0 && _session.IsPaused)
            (hint, hintColor) = ("Ordre donné : il partira dès que le héros pourra agir (Espace : reprendre)", OrderHint);
        _hud.SetHint(hint, hintColor);
    }

    private ActivateCommand? OrderOf(UnitId unit) => _orders.FirstOrDefault(o => o.Unit == unit);

    /// <summary>
    /// Ordres donnés, pas encore partis (jauge à remplir, ou pause) : fantôme, chemin et zone visée, figés, quel que soit
    /// le héros choisi. L'ordre d'un héros auquel on en prépare un autre reste affiché : c'est lui qui partira tant que
    /// le nouveau n'est pas donné. Recalculé seulement quand les ordres ou le tick changent.
    /// </summary>
    private void ShowOrders(IReadOnlyList<ActivateCommand> orders)
    {
        if (_shownOrders is { } shown && shown.Tick == _sim.CurrentTick && shown.Orders.SequenceEqual(orders))
            return;
        _shownOrders = (_sim.CurrentTick, orders);

        var ghosts = new List<Cell>();
        var path = new List<Cell>();
        var aim = new List<Cell>();
        var target = new List<Cell>();
        foreach (var command in orders)
        {
            var id = command.Unit;
            if (_sim.FindUnit(id) is not { } unit)
                continue;
            if (command.MoveTo is { } moveTo)
            {
                ghosts.Add(moveTo);
                path.AddRange(_sim.PathFor(id, moveTo) ?? []);
            }
            if (command.Skill is { } use)
            {
                var preview = _sim.PreviewAction(id, command.MoveTo ?? unit.Position, use);
                aim.AddRange(preview.Cells ?? []);
                if (preview.Destination is { } destination)
                    aim.Add(destination);
                target.AddRange(StruckCells(preview));
            }
            else if (command.Interaction is { } interaction && _sim.FindUnit(interaction.Target) is { } other)
            {
                target.Add(other.Position);
            }
        }

        _board.SetLayer("order-path", path, PathPlaced, 0.026f);
        _board.SetLayer("order-aim", aim, AimColor, 0.031f);
        _board.SetLayer("order-target", target, TargetColor, 0.036f);
        while (_orderGhosts.Count < ghosts.Count)
        {
            var ghost = new MeshInstance3D { Mesh = _ghostView.Mesh, MaterialOverride = GhostPlaced };
            AddChild(ghost);
            _orderGhosts.Add(ghost);
        }
        for (var i = 0; i < _orderGhosts.Count; i++)
        {
            _orderGhosts[i].Visible = i < ghosts.Count;
            if (i < ghosts.Count)
                _orderGhosts[i].Position = BoardView.ToWorld(ghosts[i], 0.65f);
        }
    }

    /// <summary>Ce que donnerait l'action armée sur la case survolée : cases touchées, cible, et un message.</summary>
    private (IEnumerable<Cell> Aim, IEnumerable<Cell> Target, string Hint, Color Color) AimPreview(Unit unit, Aiming aiming, Cell origin)
    {
        var none = Array.Empty<Cell>();
        var name = _ghost is null ? aiming.Name : $"Déplacement + {aiming.Name}";
        var prefix = aiming.Kind == AimKind.UnitThenCell && aiming.FirstTarget is null ? $"{name} : choisis l'unité"
            : aiming.Kind == AimKind.UnitThenCell ? $"{name} : choisis la case" : name;
        if (_hovered is not { } cell)
            return (none, none, prefix, Colors.White);

        var first = aiming.FirstTarget is { } firstId ? _sim.FindUnit(firstId) : null;
        var firstCells = first is null ? none : new[] { first.Position };
        if (aiming.CommandFor(unit.Id, _ghost, cell, _sim.UnitAt(cell)?.Id) is not { } command)
            return (none, firstCells, prefix, Colors.White);

        // Une seule validation complète si le héros peut agir ; sinon, une seconde sans le temps pour trouver le vrai obstacle.
        var now = _sim.Check(command);
        var waiting = now is { } n && n.OnlyNeedsTime();
        var reason = waiting ? _sim.Check(command, ignoreTiming: true) : now;
        IEnumerable<Cell> aim = none, target = firstCells;
        if (command.Skill is { } use)
        {
            var preview = _sim.PreviewAction(unit.Id, origin, use);
            aim = preview.Cells ?? none;
            target = target.Concat(StruckCells(preview));
            if (preview.Destination is { } destination)
                aim = aim.Append(destination);
        }
        else if (_sim.UnitAt(cell) is { } other)
        {
            target = new[] { other.Position };
        }

        if (reason is { } r)
            return (aim, target, $"{prefix} : {Texts.Rejection(r)}", new Color(1f, 0.5f, 0.45f));
        var damage = aiming.Skill is { DealsDamage: true } s ? $" ({DamageText(unit, s)} dégâts)" : "";
        var when = waiting ? "clic : partira dès que possible" : "clic pour lancer";
        return (aim, target, $"{prefix}{damage} : {when}", new Color(0.6f, 1f, 0.6f));
    }

    /// <summary>
    /// Zones au sol en cours, un calque par sorte : fumée (grise), pare-tirs (blanc bleuté), sol qui pose un état
    /// (bleu glace), piège (orange ; les joueurs sont du même camp, ils le voient tous).
    /// </summary>
    private void ShowGroundZones()
    {
        IEnumerable<Cell> Of(Func<GroundZoneDefinition, bool> kind) =>
            _sim.GroundZones.Where(z => kind(z.Definition)).SelectMany(z => z.Cells);

        _board.SetLayer("ground-smoke", Of(g => g.BlocksSight), new Color(0.75f, 0.75f, 0.75f, 0.6f), 0.012f);
        _board.SetLayer("ground-wind", Of(g => g.BlocksShots), new Color(0.8f, 1f, 1f, 0.4f), 0.012f);
        _board.SetLayer("ground-status", Of(g => !g.IsTrap && g.Effects.Count > 0), new Color(0.45f, 0.8f, 1f, 0.45f), 0.012f);
        _board.SetLayer("ground-trap", Of(g => g.IsTrap), new Color(1f, 0.6f, 0.1f, 0.7f), 0.012f);
    }

    /// <summary>Les cases des unités qu'une action frapperait : sa cible, ou toutes celles d'une chaîne.</summary>
    private IEnumerable<Cell> StruckCells(ActionPreview preview) =>
        (preview.Chain ?? (preview.Target is { } t ? new[] { t } : Array.Empty<UnitId>()))
            .Select(_sim.FindUnit)
            .OfType<Unit>()
            .Select(u => u.Position);

    private IEnumerable<Cell> Cells(Func<Cell, bool> predicate)
    {
        for (var y = 0; y < _sim.Grid.Height; y++)
            for (var x = 0; x < _sim.Grid.Width; x++)
                if (_sim.Grid.IsWalkable(new Cell(x, y)) && predicate(new Cell(x, y)))
                    yield return new Cell(x, y);
    }
}
