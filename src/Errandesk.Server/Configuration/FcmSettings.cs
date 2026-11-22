namespace Errandesk.Server.Configuration;

/// <summary>
/// Firebase service account credentials used to send Android push notifications.
/// </summary>
public sealed class FcmSettings
{
    public required string ProjectId { get; init; }

    public required string ClientEmail { get; init; }

    /// <summary>PEM-encoded RSA private key of the service account.</summary>
    public required string PrivateKey { get; init; }
}
