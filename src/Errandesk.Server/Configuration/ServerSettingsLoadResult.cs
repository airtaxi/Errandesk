using System.Diagnostics.CodeAnalysis;

namespace Errandesk.Server.Configuration;

/// <summary>
/// The outcome of loading server settings: the settings when every value is valid, otherwise every problem found.
/// </summary>
public sealed class ServerSettingsLoadResult
{
    private ServerSettingsLoadResult(ServerSettings? settings, IReadOnlyList<string> errors)
    {
        Settings = settings;
        Errors = errors;
    }

    public ServerSettings? Settings { get; }

    /// <summary>Problems found while loading. Messages name the variables involved and never contain secret values.</summary>
    public IReadOnlyList<string> Errors { get; }

    [MemberNotNullWhen(true, nameof(Settings))]
    public bool IsValid => Settings is not null;

    public static ServerSettingsLoadResult Success(ServerSettings settings) => new(settings, []);

    public static ServerSettingsLoadResult Failure(IReadOnlyList<string> errors) => new(null, errors);
}
