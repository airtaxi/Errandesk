using System.Reflection;
using Errandesk.Contracts;
using Errandesk.Server.Configuration;
using Errandesk.Server.Data;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Errandesk.Server.Services;

/// <summary>
/// Builds the public description of the server: version, registration policy and capability flags.
/// </summary>
public sealed class ServerInfoService(ServerSettings settings, IMongoDatabase database)
{
    /// <summary>The server version without build metadata.</summary>
    public static string ServerVersion { get; } = ReadServerVersion();

    public async Task<ServerInfoResponse> GetServerInfoAsync(CancellationToken cancellationToken)
    {
        var isRegistrationAvailable = settings.RegistrationMode != RegistrationMode.Disabled || !await HasAnyUserAsync(cancellationToken);
        return new ServerInfoResponse(ServerVersion, settings.RegistrationMode, isRegistrationAvailable, settings.IsAiAvailable, settings.IsAndroidPushAvailable, settings.IsIosPushAvailable, settings.IsMicrosoftTodoImportAvailable);
    }

    private Task<bool> HasAnyUserAsync(CancellationToken cancellationToken) => database.GetCollection<BsonDocument>(MongoCollectionNames.Users).Find(FilterDefinition<BsonDocument>.Empty).Limit(1).AnyAsync(cancellationToken);

    private static string ReadServerVersion()
    {
        var assembly = typeof(ServerInfoService).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informationalVersion)) return assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        var buildMetadataIndex = informationalVersion.IndexOf('+');
        return buildMetadataIndex >= 0 ? informationalVersion[..buildMetadataIndex] : informationalVersion;
    }
}
