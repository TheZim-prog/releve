using Game.Core.Reputation;

namespace Game.Core.Campaign;

/// <summary>La sauvegarde est déjà tenue par un autre jeu ou serveur (sur ce PC).</summary>
public sealed class CampaignInUseException(string path) : IOException($"Campaign save '{path}' is already open in another game or server.");

/// <summary>
/// La campagne tenue par un serveur : son état courant et son fichier, réécrit à chaque changement. Tant qu'il la
/// tient, la sauvegarde est verrouillée (un fichier « .lock » ouvert en exclusif, effacé à la fermeture) : deux jeux
/// qui l'ouvriraient en même temps écraseraient chacun les progrès de l'autre. Pas thread-safe : le serveur s'en sert
/// depuis son fil unique.
/// </summary>
public sealed class CampaignKeeper : IDisposable
{
    private readonly string _path;
    private readonly FileStream _lock;

    private CampaignKeeper(string path, CampaignState state, FileStream lockFile)
    {
        _path = path;
        State = state;
        _lock = lockFile;
    }

    public CampaignState State { get; private set; }

    /// <summary>
    /// Ouvre la campagne de ce fichier, ou en crée une nouvelle (aussitôt sauvegardée) s'il n'existe pas. Une réputation
    /// sortie des bornes (l'échelle a changé dans les données) y est ramenée. Lève une
    /// <see cref="CampaignInUseException"/> si un autre jeu la tient déjà.
    /// </summary>
    public static CampaignKeeper Open(string path, ReputationScale scale)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(scale);
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } directory)
            Directory.CreateDirectory(directory);
        var lockFile = Lock(path);
        try
        {
            if (File.Exists(path))
            {
                var loaded = CampaignStore.Load(path);
                return new CampaignKeeper(path, loaded with { Reputation = scale.Clamp(loaded.Reputation) }, lockFile);
            }
            var keeper = new CampaignKeeper(path, CampaignState.New(scale), lockFile);
            CampaignStore.Save(keeper.State, path);
            return keeper;
        }
        catch
        {
            lockFile.Dispose();
            throw;
        }
    }

    private static FileStream Lock(string path)
    {
        try
        {
            return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            throw new CampaignInUseException(path);
        }
    }

    /// <summary>Applique un changement ; s'il change quelque chose, la sauvegarde est réécrite.</summary>
    public void Update(Func<CampaignState, CampaignState> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var next = change(State);
        if (next == State)
            return;
        CampaignStore.Save(next, _path);
        State = next;
    }

    /// <summary>Lâche la sauvegarde : un autre jeu peut l'ouvrir.</summary>
    public void Dispose() => _lock.Dispose();
}
