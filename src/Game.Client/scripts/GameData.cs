using Game.Core;
using Godot;

namespace Game.Client;

/// <summary>
/// Accès au contenu du jeu, lu dans le dossier <c>data/</c> du dépôt (prototype : on le cherche en remontant depuis le
/// projet Godot ; l'export embarquera les données plus tard).
/// </summary>
public static class GameData
{
    public static GameContent Load() => GameContent.Load(FindDataDirectory());

    private static string FindDataDirectory()
    {
        var directory = new DirectoryInfo(ProjectSettings.GlobalizePath("res://"));
        for (; directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "data");
            if (File.Exists(Path.Combine(candidate, "combat-rules.json")))
                return candidate;
        }
        throw new DirectoryNotFoundException("The data/ folder was not found above the Godot project.");
    }
}
