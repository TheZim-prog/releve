using System.Text;
using System.Text.Json;

namespace Game.Core.Campaign;

/// <summary>
/// La sauvegarde d'une campagne : un fichier JSON. L'écriture passe par un fichier temporaire vidé sur le disque puis
/// remplacé d'un coup, pour qu'une coupure (courant, arrêt du serveur) ne laisse jamais une sauvegarde à moitié écrite.
/// </summary>
public static class CampaignStore
{
    private static readonly JsonSerializerOptions Options = new(DataJson.Options) { WriteIndented = true };

    /// <summary>Lit une sauvegarde ; une sauvegarde d'un autre format ou incomplète lève une <see cref="InvalidDataException"/>.</summary>
    public static CampaignState Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var state = JsonSerializer.Deserialize<CampaignState>(File.ReadAllText(path), Options)
                    ?? throw new InvalidDataException($"Campaign save '{path}' is empty.");
        if (state.Format != CampaignState.CurrentFormat)
            throw new InvalidDataException($"Campaign save '{path}' has format {state.Format}; this game reads format {CampaignState.CurrentFormat}.");
        if (state.Players is null || state.Heroes is null || state.Materials is null
            || state.Players.Any(p => p is null || !PlayerIdentity.IsValid(p.Uid) || p.Name is null)
            || state.Heroes.Any(h => h?.Id is null || h.Name is null || h.Owner is null || h.Sheet?.Archetype is null
                                     || h.Sheet.Learned is null || h.Sheet.Equipped is null))
            throw new InvalidDataException($"Campaign save '{path}' is incomplete or damaged (players, heroes, materials).");
        return state;
    }

    public static void Save(CampaignState state, string path)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } directory)
            Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            file.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state, Options)));
            file.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }
}
