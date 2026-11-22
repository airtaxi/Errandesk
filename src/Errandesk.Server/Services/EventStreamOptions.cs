namespace Errandesk.Server.Services;

/// <summary>
/// Settings of the change event stream.
/// </summary>
public sealed class EventStreamOptions
{
    /// <summary>How long the stream may stay silent before a keep-alive comment is sent.</summary>
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(25);
}
