using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Game.Core.Loading;

/// <summary>
/// Lecture des fichiers d'un dossier <c>data/</c>. Chaque texte lu entre dans une empreinte : deux contenus de même
/// empreinte donnent les mêmes combats. Un fichier que le jeu ne lit pas ne la change pas.
/// </summary>
internal sealed class DataFiles(string directory) : IDisposable
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public bool Exists(string relativePath) => File.Exists(Path.Combine(directory, relativePath));

    /// <summary>Texte d'un fichier, désigné par son chemin relatif (séparateur « / »).</summary>
    public string Read(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(directory, relativePath));
        // Fins de ligne normalisées : Git peut les convertir différemment selon la machine (Windows, Raspberry Pi).
        _hash.AppendData(Encoding.UTF8.GetBytes(relativePath + "\n" + text.Replace("\r\n", "\n") + "\n"));
        return text;
    }

    /// <summary>Lit un fichier et l'interprète ; une erreur de données nomme le fichier.</summary>
    public T Load<T>(string relativePath, Func<string, T> parse) => InContext(relativePath, () => parse(Read(relativePath)));

    /// <summary>
    /// Fichiers JSON d'un sous-dossier et de ses propres sous-dossiers, en chemins relatifs (séparateur « / »), dans un
    /// ordre stable d'une machine à l'autre. Aucun si le sous-dossier n'existe pas.
    /// </summary>
    public IReadOnlyList<string> List(string folder)
    {
        var path = Path.Combine(directory, folder);
        if (!Directory.Exists(path))
            return Array.Empty<string>();
        return Directory.GetFiles(path, "*.json", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Empreinte de tout ce qui a été lu jusqu'ici.</summary>
    public string Version() => Convert.ToHexString(_hash.GetCurrentHash())[..16];

    public void Dispose() => _hash.Dispose();

    /// <summary>Exécute une lecture ; une erreur de données (JSON illisible, valeur refusée) est complétée par <paramref name="where"/>.</summary>
    public static T InContext<T>(string where, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (JsonException e)
        {
            throw new JsonException($"{where}: {e.Message}", e);
        }
        catch (ArgumentException e)
        {
            throw new ArgumentException($"{where}: {e.Message}", e);
        }
    }
}
