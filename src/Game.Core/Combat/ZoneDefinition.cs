using Game.Core.Grids;

namespace Game.Core.Combat;

public enum ZoneShape
{
    /// <summary>Carré de rayon <see cref="ZoneDefinition.Size"/> autour de la case visée (0 = la case seule).</summary>
    Square,

    /// <summary>
    /// Ligne droite de <see cref="ZoneDefinition.Size"/> cases depuis le lanceur, dans la direction de la case visée
    /// (alignée : même ligne, même colonne ou diagonale).
    /// </summary>
    Line,
}

/// <summary>Qui une zone touche, en plus de ne jamais toucher son lanceur.</summary>
public enum ZoneTargets
{
    All,
    Enemies,

    /// <summary>Seulement les alliés du lanceur : une zone de soutien (ex. soin de la balise), jamais bloquée par un dôme.</summary>
    Allies,
}

/// <summary>Zone d'effet d'une compétence (données). Une compétence à zone vise une case, pas une unité.</summary>
public sealed record ZoneDefinition
{
    public required ZoneShape Shape { get; init; }
    public required int Size { get; init; }

    /// <summary>
    /// Vrai : la zone ignore les murs (seules les cases de mur sont exclues). Faux : un carré ne couvre que les
    /// cases visibles depuis son centre, une ligne s'arrête au premier mur (sans coupe d'angle). Dans les deux cas,
    /// les obstacles bas ne gênent pas (on voit par-dessus). Propre à chaque compétence.
    /// </summary>
    public required bool ThroughWalls { get; init; }

    /// <summary>Par défaut, la zone touche tout le monde (docs/design.md).</summary>
    public ZoneTargets Affects { get; init; } = ZoneTargets.All;

    /// <summary>
    /// Une zone qui ne touche que les ennemis touche aussi les gadgets posés par le lanceur (ex. l'IEM désactive les
    /// gadgets du tech : un dilemme). Faux par défaut.
    /// </summary>
    public bool AlsoHitsOwnSummons { get; init; }

    public void Validate(string skillId)
    {
        if (!Enum.IsDefined(Shape) || !Enum.IsDefined(Affects))
            throw new ArgumentException($"Skill '{skillId}' has an unknown zone shape or target filter.");
        if (AlsoHitsOwnSummons && Affects != ZoneTargets.Enemies)
            throw new ArgumentException($"Skill '{skillId}': only an enemies-only zone can also hit its caster's gadgets.");
        if (Size < (Shape == ZoneShape.Line ? 1 : 0))
            throw new ArgumentException($"Skill '{skillId}' has an invalid zone size {Size}.");
    }

    /// <summary>
    /// Cases de la zone lancée depuis <paramref name="caster"/> vers <paramref name="target"/>, dans un ordre fixe,
    /// ou <c>null</c> si la case visée ne convient pas : un mur pour un carré, une case non alignée ou celle
    /// du lanceur pour une ligne. La liste peut être vide (ligne bloquée dès le premier pas).
    /// </summary>
    public IReadOnlyList<Cell>? Cells(Grid grid, Cell caster, Cell target) => Shape switch
    {
        ZoneShape.Square => SquareCells(grid, target),
        ZoneShape.Line => LineCells(grid, caster, target),
        _ => throw new InvalidOperationException($"Unknown zone shape {Shape}."),
    };

    private IReadOnlyList<Cell>? SquareCells(Grid grid, Cell center)
    {
        if (grid.IsBlocked(center))
            return null;

        var cells = new List<Cell>();
        for (var y = center.Y - Size; y <= center.Y + Size; y++)
            for (var x = center.X - Size; x <= center.X + Size; x++)
            {
                var cell = new Cell(x, y);
                if (grid.IsWalkable(cell) && (ThroughWalls || LineOfSight.IsClear(grid, center, cell)))
                    cells.Add(cell);
            }
        return cells;
    }

    private IReadOnlyList<Cell>? LineCells(Grid grid, Cell caster, Cell target)
    {
        if (caster.DirectionTo(target) is not { } step)
            return null;

        var cells = new List<Cell>();
        var previous = caster;
        for (var i = 0; i < Size; i++)
        {
            var next = new Cell(previous.X + step.X, previous.Y + step.Y);
            if (!grid.Contains(next))
                break;
            // Sans traverser les murs : arrêt au premier mur (y compris sur un coin frôlé en diagonale) ; un obstacle
            // bas se survole (sa case, où personne ne se tient, n'est simplement pas dans la zone).
            if (!ThroughWalls && !grid.CanSeeStep(previous, next))
                break;
            if (grid.IsWalkable(next))
                cells.Add(next);
            previous = next;
        }
        return cells;
    }
}
