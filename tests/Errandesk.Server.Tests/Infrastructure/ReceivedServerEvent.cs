namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// One event read from a server-sent event stream.
/// </summary>
public sealed record ReceivedServerEvent(string EventType, string Data)
{
    /// <summary>Deserializes the JSON data through the contracts context.</summary>
    public TData Deserialize<TData>() => ApiClient.DeserializeJson<TData>(Data);
}
