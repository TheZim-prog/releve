using Game.Core;
using Godot;

namespace Game.Client;

/// <summary>
/// Accès au contenu du jeu, lu dans un dossier <c>data/</c> : celui du dépôt quand on lance depuis les sources (on le
/// cherche en remontant depuis le projet Godot), celui posé à côté de l'exécutable dans le jeu exporté.
/// </summary>
public static class GameData
{
    public static GameContent Load() => GameContent.Load(DataDirectory());

    private static string DataDirectory()
    {
        if (OS.HasFeature("editor"))
        {
            var project = ProjectSettings.GlobalizePath("res://");
            return GameContent.FindDataDirectory(project)
                ?? throw new DirectoryNotFoundException($"The data/ folder was not found above the Godot project ('{project}').");
        }

        // Jeu exporté : « res:// » n'est plus un dossier du disque, et on ne regarde qu'à côté de l'exécutable (pas
        // plus haut : un export incomplet ne doit pas marcher par hasard grâce au data/ du dépôt).
        var beside = Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? "", "data");
        return Directory.Exists(beside)
            ? beside
            : throw new DirectoryNotFoundException($"The data/ folder is missing next to the game ('{beside}').");
    }
}
