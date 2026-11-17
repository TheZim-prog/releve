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

    /// <summary>Une unité (attaque ciblée, soutien, Charge, Ruée, Bond, permutation, relever) ou un objet de mission (interagir).</summary>
    Unit,

    /// <summary>Une unité, puis une case (télékinésie, lancer d'un allié, sauvetage, transport, Retraite, Tourniquet).</summary>
    UnitThenCell,
}

/// <summary>
/// Action armée par le joueur (une compétence, ou interagir) et sa visée : traduit les clics en <see cref="SkillUse"/> ou
/// <see cref="InteractionUse"/>, sans juger de leur validité (c'est la simulation qui le fait). Interagir sur une unité
/// la relève ; sur une case sans unité, c'est travailler sur l'objet de mission qui s'y trouve.
/// </summary>
public sealed record Aiming(SkillDefinition? Skill)
{
    /// <summary>L'action « interagir » (relever un allié, travailler sur un objet de mission).</summary>
    public static Aiming Interact { get; } = new(Skill: null);

    public AimKind Kind => Skill switch
    {
        null => AimKind.Unit, // interagir
        { Target: SkillTargeting.Self } => AimKind.Self,
        { IsRescue: true } or { ForcedMove.Type: ForcedMoveType.Place or ForcedMoveType.Spin } or { SelfMove: SelfMoveType.Retreat }
            => AimKind.UnitThenCell,
        { SelfMove: SelfMoveType.Charge or SelfMoveType.Dash or SelfMoveType.Pounce } => AimKind.Unit,
        { SelfMove: not null } or { Summon: not null } or { Zone: not null } => AimKind.Cell,
        _ => AimKind.Unit,
    };

    public string Name => Skill?.DisplayName ?? "Interagir";

    /// <summary>Unité déjà désignée (première étape d'une visée unité puis case).</summary>
    public UnitId? FirstTarget { get; init; }

    /// <summary>
    /// Commande pour cette visée, avec la case et l'unité survolées (<paramref name="hoveredDevice"/> : un objet de
    /// mission est sur la case) ; <c>null</c> s'il manque une désignation.
    /// </summary>
    public ActivateCommand? CommandFor(UnitId unit, Cell? moveTo, Cell hovered, UnitId? hoveredUnit, bool hoveredDevice = false)
    {
        if (Skill is null)
        {
            var interaction = hoveredUnit is { } target ? new InteractionUse(InteractionKind.Revive, Target: target)
                : hoveredDevice ? new InteractionUse(InteractionKind.Operate, Cell: hovered)
                : null;
            return interaction is null ? null : new ActivateCommand(unit, moveTo, Interaction: interaction);
        }

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
