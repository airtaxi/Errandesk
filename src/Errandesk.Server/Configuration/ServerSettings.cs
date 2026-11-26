using Errandesk.Contracts;

namespace Errandesk.Server.Configuration;

/// <summary>
/// Validated server configuration, loaded once at startup from environment variables.
/// </summary>
public sealed class ServerSettings
{
    public required string MongoDbConnectionString { get; init; }

    public required string MongoDbDatabaseName { get; init; }

    /// <summary>Key used to sign access tokens. Always at least 32 bytes in UTF-8.</summary>
    public required string JwtSigningKey { get; init; }

    public required TimeSpan AccessTokenLifetime { get; init; }

    public required TimeSpan RefreshTokenLifetime { get; init; }

    public required int HttpPort { get; init; }

    public required LogLevel LogLevel { get; init; }

    public required RegistrationMode RegistrationMode { get; init; }

    /// <summary>Assistant model settings, or <see langword="null"/> when no model is configured.</summary>
    public AiSettings? Ai { get; init; }

    /// <summary>Firebase Cloud Messaging credentials, or <see langword="null"/> when Android push is not configured.</summary>
    public FcmSettings? Fcm { get; init; }

    /// <summary>Apple Push Notification service credentials, or <see langword="null"/> when iOS push is not configured.</summary>
    public ApnsSettings? Apns { get; init; }

    /// <summary>Microsoft Graph application settings, or <see langword="null"/> when Microsoft To Do import is not configured.</summary>
    public MicrosoftGraphSettings? MicrosoftGraph { get; init; }

    /// <summary>Base URL below which the client update version files and packages are published, or <see langword="null"/> when client updates are not configured.</summary>
    public Uri? ClientUpdateBaseUrl { get; init; }

    public bool IsAiAvailable => Ai is not null;

    public bool IsAndroidPushAvailable => Fcm is not null;

    public bool IsIosPushAvailable => Apns is not null;

    public bool IsMicrosoftTodoImportAvailable => MicrosoftGraph is not null;
}
