using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game.Core.Network;

/// <summary>Sérialisation JSON des messages réseau (un message par ligne, sans retour à la ligne interne).</summary>
public static class NetJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowOutOfOrderMetadataProperties = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static string Serialize(ClientMessage message) => JsonSerializer.Serialize(message, Options);

    public static string Serialize(ServerMessage message) => JsonSerializer.Serialize(message, Options);

    public static ClientMessage DeserializeClient(string json) => Deserialize<ClientMessage>(json);

    public static ServerMessage DeserializeServer(string json) => Deserialize<ServerMessage>(json);

    /// <summary>Tout message malformé lève une <see cref="JsonException"/>, même quand c'est un constructeur qui échoue.</summary>
    private static T Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException($"{typeof(T).Name} is null.");
        }
        catch (Exception e) when (e is not JsonException)
        {
            throw new JsonException($"Invalid {typeof(T).Name}: {e.Message}", e);
        }
    }
}
