using Game.Core.Reputation;

namespace Game.Core.Campaign;

/// <summary>
/// La campagne tenue par un serveur : son état courant et son fichier, réécrit à chaque changement. Pas thread-safe :
/// le serveur s'en sert depuis son fil unique.
/// </summary>
public sealed class CampaignKeeper
{
    private readonly string _path;

    private CampaignKeeper(string path, CampaignState state)
    {
        _path = path;
        State = state;
    }

    public CampaignState State { get; private set; }

    /// <summary>
    /// Ouvre la campagne de ce fichier, ou en crée une nouvelle (aussitôt sauvegardée) s'il n'existe pas. Une réputation
    /// sortie des bornes (l'échelle a changé dans les données) y est ramenée.
    /// </summary>
    public static CampaignKeeper Open(string path, ReputationScale scale)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(scale);
        if (File.Exists(path))
        {
            var loaded = CampaignStore.Load(path);
            return new CampaignKeeper(path, loaded with { Reputation = scale.Clamp(loaded.Reputation) });
        }
        var keeper = new CampaignKeeper(path, CampaignState.New(scale));
        CampaignStore.Save(keeper.State, path);
        return keeper;
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
}
