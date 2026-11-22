namespace Errandesk.Server.Configuration;

/// <summary>
/// Apple Push Notification service credentials used to send iOS push notifications.
/// </summary>
public sealed class ApnsSettings
{
    public required string TeamId { get; init; }

    public required string KeyId { get; init; }

    /// <summary>PEM-encoded content of the .p8 authentication key.</summary>
    public required string PrivateKey { get; init; }

    /// <summary>The iOS bundle ID, sent as the <c>apns-topic</c> header.</summary>
    public required string BundleId { get; init; }

    public required ApnsEnvironment Environment { get; init; }
}
