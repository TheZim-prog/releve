using Game.Core;
using Godot;

namespace Game.Client;

/// <summary>
/// Accès au contenu du jeu, lu dans le dossier <c>data/</c> du dépôt (prototype : on le cherche en remontant depuis le
/// projet Godot ; l'export embarquera les données plus tard).
/// </summary>
public static class GameData
{
    public static GameContent Load() =>
        GameContent.Load(GameContent.FindDataDirectory(ProjectSettings.GlobalizePath("res://"))
            ?? throw new DirectoryNotFoundException("The data/ folder was not found above the Godot project."));
}
