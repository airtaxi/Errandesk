namespace Errandesk.Server.Configuration;

/// <summary>
/// Connection settings for the OpenAI-compatible chat model used by the assistant.
/// </summary>
public sealed class AiSettings
{
    public required Uri Endpoint { get; init; }

    /// <summary>API key, or <see langword="null"/> for endpoints that accept unauthenticated requests.</summary>
    public string? ApiKey { get; init; }

    public required string Model { get; init; }
}
