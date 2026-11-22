namespace Errandesk.Server.Import;

/// <summary>
/// Timing of the Microsoft To Do import.
/// </summary>
public sealed class MicrosoftTodoImportOptions
{
    /// <summary>Wait between sign-in checks. When <see langword="null"/>, the interval the Microsoft identity platform asks for is used.</summary>
    public TimeSpan? PollInterval { get; init; }

    /// <summary>How often a Microsoft Graph request is tried when Graph throttles it or is temporarily unavailable.</summary>
    public int MaximumRequestAttempts { get; init; } = 5;

    /// <summary>Longest wait before trying a throttled Microsoft Graph request again.</summary>
    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromSeconds(60);
}
