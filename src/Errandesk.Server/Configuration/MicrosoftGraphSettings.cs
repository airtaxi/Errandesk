namespace Errandesk.Server.Configuration;

/// <summary>
/// Public client application settings used to import data from Microsoft To Do through Microsoft Graph.
/// </summary>
public sealed class MicrosoftGraphSettings
{
    public required string ClientId { get; init; }

    public required string Tenant { get; init; }
}
