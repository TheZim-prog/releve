using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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
        CampaignState? state;
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(path),
                           documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                       ?? throw new InvalidDataException($"Campaign save '{path}' is empty.");
            state = Migrated(json).Deserialize<CampaignState>(Options);
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException)
        {
            // Une valeur du mauvais type lue pendant la conversion d'un ancien format : une sauvegarde abîmée.
            throw new InvalidDataException($"Campaign save '{path}' is damaged: {e.Message}", e);
        }
        if (state is null)
            throw new InvalidDataException($"Campaign save '{path}' is empty.");
        if (state.Format != CampaignState.CurrentFormat)
            throw new InvalidDataException($"Campaign save '{path}' has format {state.Format}; this game reads format {CampaignState.CurrentFormat}.");
        if (state.Players is null || state.Heroes is null || state.Materials is null
            || state.DoneQuests is null || state.KnownPois is null || state.ExhaustedPois is null || state.Trips is null
            || state.Trips.Any(t => t?.Quest is null || t.Heroes is null || t.Path is null)
            || state.Players.Any(p => p is null || !PlayerIdentity.IsValid(p.Uid) || p.Name is null)
            || state.Heroes.Any(h => h?.Id is null || h.Name is null || h.Owner is null || h.Sheet?.Archetype is null
                                     || h.Sheet.Learned is null || h.Sheet.Equipped is null))
            throw new InvalidDataException($"Campaign save '{path}' is incomplete or damaged (players, heroes, materials, lists).");
        return state;
    }

    /// <summary>
    /// Une sauvegarde d'un format plus ancien, mise au format courant, d'étape en étape. Format 1 → 2 : les chutes d'un
    /// héros (« falls ») deviennent des demi-chutes (« halfFalls »). Format 2 → 3 : le repos se compte en heures, et non
    /// plus en quêtes (« questsRested » est oublié : chacun repart de zéro). Format 3 → 4 : le trajet en cours (« trip »,
    /// peut-être vide) devient le premier des trajets des groupes (« trips »).
    /// </summary>
    private static JsonNode Migrated(JsonNode json)
    {
        if (json is not JsonObject save)
            return json;
        var heroes = save["heroes"]?.AsArray().OfType<JsonObject>().ToList() ?? new List<JsonObject>();
        if (save["format"]?.GetValue<int>() == 1)
        {
            foreach (var hero in heroes)
            {
                var falls = hero["falls"]?.GetValue<int>() ?? 0;
                hero.Remove("falls");
                hero["halfFalls"] = Injuries.HalvesPerFall * falls;
            }
            save["format"] = 2;
        }
        if (save["format"]?.GetValue<int>() == 2)
        {
            foreach (var hero in heroes)
                hero.Remove("questsRested");
            save["format"] = 3;
        }
        if (save["format"]?.GetValue<int>() == 3)
        {
            if (save.Remove("trip", out var trip) && trip is not null)
                save["trips"] = new JsonArray(trip);
            save["format"] = 4;
        }
        return save;
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
