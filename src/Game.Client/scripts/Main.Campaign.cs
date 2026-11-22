using System.Security.Cryptography;
using System.Text;
using Game.Core.Campaign;
using Godot;

namespace Game.Client;

// Identité du joueur et campagne de l'hôte.
public partial class Main
{
    private const string IdentityPath = "user://identity.cfg";
    private const string HostedCampaignPath = "user://campaign.json";

    // Lancements de test (--name=) : une campagne à part, pour ne pas toucher à celle du joueur.
    private const string DevCampaignPath = "user://campaign-dev.json";

    private static string? _playerUid;

    /// <summary>
    /// L'identifiant du joueur, créé au premier lancement et gardé sur ce PC (le serveur le reconnaît d'une session à
    /// l'autre), le même pendant toute la partie. Lancement de test : --uid=, sinon un identifiant tiré de --name=
    /// (plusieurs jeux sur un même PC).
    /// </summary>
    private static string PlayerUid() => _playerUid ??= ReadPlayerUid();

    private static string ReadPlayerUid()
    {
        if (DevArg("uid") is { } uid && PlayerIdentity.IsValid(uid))
            return uid;
        if (DevArg("name") is { Length: > 0 } name)
            return "dev-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..16];
        var config = new ConfigFile();
        config.Load(IdentityPath); // absent la première fois
        var known = (string)config.GetValue("player", "uid", "");
        if (PlayerIdentity.IsValid(known))
            return known;
        var created = PlayerIdentity.Create();
        config.SetValue("player", "uid", created);
        if (config.Save(IdentityPath) != Error.Ok)
            GD.PushError($"Cannot save the player identity to {ProjectSettings.GlobalizePath(IdentityPath)}: it will change at the next launch.");
        return created;
    }

    private static string CampaignPath => DevArg("name") is not null ? DevCampaignPath : HostedCampaignPath;

    /// <summary>
    /// La campagne de la partie hébergée depuis ce jeu (sauvegardée sur ce PC), ou <c>null</c> si le contenu n'a pas de
    /// règles de réputation. Lève une exception si la sauvegarde est illisible.
    /// </summary>
    private CampaignKeeper? HostedCampaign() =>
        _content.Reputation is { } rules
            ? CampaignKeeper.Open(ProjectSettings.GlobalizePath(CampaignPath), rules.Scale)
            : null;
}
