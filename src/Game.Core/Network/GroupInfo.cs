namespace Game.Core.Network;

/// <summary>Où en est un groupe : il se forme au QG, il est en route vers sa quête, ou il la joue.</summary>
public enum GroupPhase
{
    Forming,
    Travelling,
    Fighting,
}

/// <summary>
/// Un groupe formé autour d'une quête (cf. docs/design.md 4.3) : sa quête, son chef (le joueur qui l'a acceptée), ses
/// joueurs dans l'ordre d'arrivée (le chef d'abord), où il en est, et combien de héros la quête accueille.
/// </summary>
public sealed record GroupInfo(int Id, string Quest, string Leader, IReadOnlyList<string> Members, GroupPhase Phase, int Slots);
