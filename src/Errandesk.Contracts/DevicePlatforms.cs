namespace Errandesk.Contracts;

/// <summary>
/// Platforms a client device can register as.
/// </summary>
public static class DevicePlatforms
{
    /// <summary>Android; receives reminders through Firebase Cloud Messaging.</summary>
    public const string Android = "android";

    /// <summary>iOS; receives reminders through the Apple Push Notification service.</summary>
    public const string Ios = "ios";

    /// <summary>Windows App SDK; schedules reminders locally.</summary>
    public const string Windows = "windows";

    /// <summary>Desktop on macOS, Linux or Windows; schedules reminders locally.</summary>
    public const string Desktop = "desktop";

    public static IReadOnlyList<string> All { get; } = [Android, Ios, Windows, Desktop];

    /// <summary>Platforms whose devices register a push token.</summary>
    public static IReadOnlyList<string> Mobile { get; } = [Android, Ios];
}
