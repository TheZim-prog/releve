// Serveur dédié (ex. Raspberry Pi 5, joint par Tailscale) : héberge une partie jusqu'à Ctrl+C ou l'arrêt du service.
// Usage : Game.Server [--port 7777] [--scenario street] [--data <dossier data>]
using System.Runtime.InteropServices;
using Game.Core;
using Game.Core.Network;

const string Usage = "Usage: Game.Server [--port 7777] [--scenario street] [--data <data folder>]";
var port = Protocol.DefaultPort;
var scenario = "street";
string? data = null;
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
if (!content.Scenarios.ContainsKey(scenario))
{
    Console.Error.WriteLine($"Unknown scenario '{scenario}' (available: {string.Join(", ", content.Scenarios.Keys)}).");
    return 1;
}
void Log(string line) => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {line}");
Log($"Code {Protocol.CodeVersion}, content {content.Version} from {Path.GetFullPath(data)}, scenario '{scenario}'.");

using var server = new GameServer(content, scenario, port, Log);
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
