using Errandesk.Server.Configuration;

namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// Builds valid <see cref="ServerSettings"/> through <see cref="ServerSettingsLoader"/> from an in-memory variable set.
/// </summary>
public static class TestServerSettings
{
    public const string JwtSigningKey = "test-signing-key-with-at-least-32-bytes";

    public static ServerSettings Create(string connectionString, string databaseName, IReadOnlyDictionary<string, string>? additionalVariables = null)
    {
        var variables = new Dictionary<string, string>
        {
            [ServerEnvironmentVariables.MongoDbConnectionString] = connectionString,
            [ServerEnvironmentVariables.MongoDbDatabase] = databaseName,
            [ServerEnvironmentVariables.JwtSigningKey] = JwtSigningKey,
        };
        foreach (var variable in additionalVariables ?? new Dictionary<string, string>()) variables[variable.Key] = variable.Value;

        var loadResult = new ServerSettingsLoader(name => variables.GetValueOrDefault(name)).Load();
        if (!loadResult.IsValid) throw new InvalidOperationException(string.Join(Environment.NewLine, loadResult.Errors));
        return loadResult.Settings;
    }
}
