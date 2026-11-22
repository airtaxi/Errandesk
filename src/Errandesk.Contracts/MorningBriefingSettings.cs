namespace Errandesk.Contracts;

/// <summary>
/// When the assistant writes the user's morning briefing.
/// </summary>
/// <param name="IsEnabled">Whether a briefing is written every morning.</param>
/// <param name="Time">Local time of the briefing in the user's time zone, as <c>HH:mm</c>.</param>
public sealed record MorningBriefingSettings(bool IsEnabled, string Time)
{
    /// <summary>The time used until the user picks one.</summary>
    public const string DefaultTime = "07:00";

    /// <summary>The settings of a user who never changed them.</summary>
    public static MorningBriefingSettings Default { get; } = new(false, DefaultTime);
}
