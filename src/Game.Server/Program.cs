// Serveur dédié (ex. Raspberry Pi 5, joint par Tailscale) : héberge une partie jusqu'à Ctrl+C ou l'arrêt du service.
// Usage : Game.Server [--port 7777] [--scenario street] [--data <dossier data>] [--campaign <fichier de sauvegarde>]
// Avec --campaign, le serveur tient la campagne de ce fichier (créée s'il n'existe pas) et la sauvegarde à chaque changement.
using System.Runtime.InteropServices;
using Game.Core;
using Game.Core.Campaign;
using Game.Core.Network;

const string Usage = "Usage: Game.Server [--port 7777] [--scenario street] [--data <data folder>] [--campaign <save file>]";
var port = Protocol.DefaultPort;
var scenario = "street";
string? data = null;
string? campaignPath = null;
for (var i = 0; i < args.Length; i++)
{
    var value = i + 1 < args.Length ? args[i + 1] : null;
    switch (args[i])
    {
        case "--port" when int.TryParse(value, out var p) && p is > 0 and <= 65535:
            port = p;
            i++;
            break;
        case "--scenario" when value is not null:
            scenario = value;
            i++;
            break;
        case "--data" when value is not null:
            data = value;
            i++;
            break;
        case "--campaign" when value is not null:
            campaignPath = value;
            i++;
            break;
        default:
            Console.Error.WriteLine(Usage);
            return 1;
    }
}

data ??= GameContent.FindDataDirectory(AppContext.BaseDirectory);
if (data is null)
{
    Console.Error.WriteLine("data/ folder not found (next to the server or above it): use --data <folder>.");
    return 1;
}

var content = GameContent.Load(data);
// Sans campagne, un combat libre : un scénario avec ses propres héros (pas une quête, qui attend les héros des joueurs).
var freeCombats = content.Scenarios.Values.Where(s => s.HeroSlots is null).Select(s => s.Id).ToList();
if (campaignPath is null && !freeCombats.Contains(scenario))
{
    Console.Error.WriteLine($"Unknown free-combat scenario '{scenario}' (available: {string.Join(", ", freeCombats)}; quests are played with --campaign).");
    return 1;
}
void Log(string line) => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {line}");
Log($"Code {Protocol.CodeVersion}, content {content.Version} from {Path.GetFullPath(data)}, scenario '{scenario}'.");

CampaignKeeper? campaign = null;
if (campaignPath is not null)
{
    if (content.Reputation is null)
    {
        Console.Error.WriteLine("This content has no reputation.json: a campaign needs it.");
        return 1;
    }
    try
    {
        campaign = CampaignKeeper.Open(campaignPath, content.Reputation.Scale);
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
    {
        Console.Error.WriteLine($"Cannot open the campaign '{campaignPath}': {e.Message}");
        return 1;
    }
    Log($"Campaign {Path.GetFullPath(campaignPath)}: {campaign.State.Players.Count} player(s), reputation {campaign.State.Reputation}.");
}

using var server = new GameServer(content, scenario, port, Log, campaign);
server.Start();

// Ctrl+C, ou SIGTERM (arrêt du service systemd) : on annule l'arrêt brutal pour fermer proprement.
var stop = new TaskCompletionSource();
void OnSignal(PosixSignalContext context)
{
    context.Cancel = true;
    stop.TrySetResult();
}
using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);
using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);
await stop.Task;
Log("Stopping.");
return 0;
