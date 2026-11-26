namespace Errandesk.Client.Droid;

/// <summary>
/// How one task widget looks and which tasks it lists.
/// </summary>
/// <param name="Opacity">The opacity of the widget background, from 0 to 100 percent.</param>
/// <param name="FontSize">The text size of the header and the task rows.</param>
/// <param name="ShowsCompletedTasks">Whether completed tasks follow the open ones.</param>
public sealed record TaskWidgetSettings(int Opacity, TaskWidgetFontSize FontSize, bool ShowsCompletedTasks)
{
    public const int MaximumOpacity = 100;

    /// <summary>The most completed tasks a widget lists, the most recently completed first.</summary>
    public const int MaximumCompletedTaskCount = 50;

    public static TaskWidgetSettings Default { get; } = new(MaximumOpacity, TaskWidgetFontSize.Normal, true);

    /// <summary>The background alpha from 0 to 255.</summary>
    public int BackgroundAlpha => Math.Clamp(Opacity, 0, MaximumOpacity) * 255 / MaximumOpacity;

    /// <summary>The size of the header title in scaled pixels.</summary>
    public float HeaderTextSize => FontSize switch
    {
        TaskWidgetFontSize.Small => 14,
        TaskWidgetFontSize.Large => 18,
        _ => 16,
    };

    /// <summary>The size of the task titles in scaled pixels.</summary>
    public float TaskTextSize => FontSize switch
    {
        TaskWidgetFontSize.Small => 13,
        TaskWidgetFontSize.Large => 17,
        _ => 15,
    };
}
