namespace Game.Core.Combat;

/// <summary>Ce qu'une compétence fait de l'obstacle bas qu'elle prend (voir <see cref="SceneryDefinition"/>).</summary>
public enum SceneryUse
{
    /// <summary>Lancer de décor : l'obstacle est lancé sur la zone visée (dégâts de la compétence) et détruit.</summary>
    Throw,

    /// <summary>Barricade : l'obstacle est posé sur la case visée, libre ; il garde ses PV.</summary>
    Place,
}
