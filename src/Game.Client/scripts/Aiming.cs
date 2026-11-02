using Game.Core.Combat;
using Game.Core.Grids;

namespace Game.Client;

/// <summary>Ce qu'il faut désigner pour lancer une action.</summary>
public enum AimKind
{
    /// <summary>Rien : sur soi (cliquer n'importe où confirme).</summary>
    Self,

    /// <summary>Une case (zone, Percée, Saut, gadget).</summary>
    Cell,

    /// <summary>Une unité (attaque ciblée, soutien, Charge, relever).</summary>
    Unit,

    /// <summary>Une unité, puis une case (télékinésie, sauvetage).</summary>
    UnitThenCell,
}

/// <summary>
/// Action armée par le joueur (une compétence, ou relever) et sa visée : traduit les clics en <see cref="SkillUse"/> ou
/// <see cref="InteractionUse"/>, sans juger de leur validité (c'est la simulation qui le fait).
/// </summary>
public sealed record Aiming(SkillDefinition? Skill, InteractionKind? Interaction = null)
{
    public AimKind Kind => Skill switch
    {
        null => AimKind.Unit, // relever
        { Target: SkillTargeting.Self } => AimKind.Self,
        { IsRescue: true } or { ForcedMove.Type: ForcedMoveType.Place } => AimKind.UnitThenCell,
        { SelfMove: SelfMoveType.Rush } => AimKind.Unit,
        { SelfMove: not null } or { Summon: not null } or { Zone: not null } => AimKind.Cell,
        _ => AimKind.Unit,
    };

    public string Name => Skill?.DisplayName ?? "Relever";

    /// <summary>Unité déjà désignée (première étape d'une visée unité puis case).</summary>
    public UnitId? FirstTarget { get; init; }

    /// <summary>Commande pour cette visée, avec la case et l'unité survolées ; <c>null</c> s'il manque une désignation.</summary>
    public ActivateCommand? CommandFor(UnitId unit, Cell? moveTo, Cell hovered, UnitId? hoveredUnit)
    {
        if (Interaction is { } interaction)
            return hoveredUnit is { } target ? new ActivateCommand(unit, moveTo, Interaction: new InteractionUse(interaction, target)) : null;

        var use = Kind switch
        {
            AimKind.Self => new SkillUse(Skill!.Id, null),
            AimKind.Cell => new SkillUse(Skill!.Id, null, hovered),
            AimKind.Unit => hoveredUnit is { } target ? new SkillUse(Skill!.Id, target) : null,
            AimKind.UnitThenCell => FirstTarget is { } first ? new SkillUse(Skill!.Id, first, hovered) : null,
            _ => null,
        };
        return use is null ? null : new ActivateCommand(unit, moveTo, use);
    }
}
