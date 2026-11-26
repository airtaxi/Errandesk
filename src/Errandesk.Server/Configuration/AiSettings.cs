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

    /// <summary>Whether the assistant may read web pages with <c>read_url</c>.</summary>
    public bool IsWebFetchEnabled { get; init; } = true;

    /// <summary>Whether web pages on private networks may be read. Loopback and link-local addresses stay blocked.</summary>
    public bool AllowsPrivateNetworkWebFetch { get; init; }
}
