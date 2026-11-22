using System.Globalization;

namespace Errandesk.Client.Helpers;

/// <summary>
/// Formats dates for display in the interface language.
/// </summary>
public static class DateDisplayHelper
{
    /// <summary>
    /// Today's date on this device.
    /// </summary>
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>
    /// Formats a due date relative to <paramref name="today"/>: Today, Tomorrow or Yesterday, otherwise a short date.
    /// </summary>
    public static string FormatDueDate(DateOnly dueDate, DateOnly today) => (dueDate.DayNumber - today.DayNumber) switch
    {
        0 => LocalizedStrings.Get("DueTodayText"),
        1 => LocalizedStrings.Get("DueTomorrowText"),
        -1 => LocalizedStrings.Get("DueYesterdayText"),
        _ => dueDate.ToString(LocalizedStrings.Get(dueDate.Year == today.Year ? "ShortDatePattern" : "ShortDateWithYearPattern"), CultureInfo.CurrentCulture),
    };

    /// <summary>
    /// Formats the local date of <paramref name="value"/> as a short date.
    /// </summary>
    public static string FormatShortDate(DateTimeOffset value)
    {
        var localDate = DateOnly.FromDateTime(value.ToLocalTime().DateTime);
        return localDate.ToString(LocalizedStrings.Get(localDate.Year == Today.Year ? "ShortDatePattern" : "ShortDateWithYearPattern"), CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Formats a reminder time in local time: the day as for due dates, followed by the time of day.
    /// </summary>
    public static string FormatReminder(DateTimeOffset reminderAt)
    {
        var localTime = reminderAt.ToLocalTime();
        return string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("ReminderDateTimeFormat"), FormatDueDate(DateOnly.FromDateTime(localTime.DateTime), Today), FormatTime(localTime));
    }

    /// <summary>
    /// Formats a reminder suggestion of the reminder menu: the short day of the week and the time of day.
    /// </summary>
    public static string FormatReminderSuggestion(DateTimeOffset reminderAt)
    {
        var localTime = reminderAt.ToLocalTime();
        return string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("ReminderDateTimeFormat"), localTime.ToString("ddd", CultureInfo.CurrentCulture), FormatTime(localTime));
    }

    /// <summary>
    /// Formats the local time of day of <paramref name="value"/> with the short time pattern of the interface culture.
    /// </summary>
    public static string FormatTime(DateTimeOffset value) => value.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);

    /// <summary>
    /// Formats today's date for the My Day header, with the day of the week.
    /// </summary>
    public static string FormatMyDayDate(DateOnly date) => date.ToString(LocalizedStrings.Get("MyDayDatePattern"), CultureInfo.CurrentCulture);

    /// <summary>
    /// Formats the local date of <paramref name="value"/> as a long date.
    /// </summary>
    public static string FormatLongDate(DateTimeOffset value) => DateOnly.FromDateTime(value.ToLocalTime().DateTime).ToString(LocalizedStrings.Get("LongDatePattern"), CultureInfo.CurrentCulture);

    public static DateTimeOffset? ToDateTimeOffset(DateOnly? date) => date is { } value ? new DateTimeOffset(value.ToDateTime(TimeOnly.MinValue)) : null;

    public static DateOnly? ToDateOnly(DateTimeOffset? value) => value is { } dateTimeOffset ? DateOnly.FromDateTime(dateTimeOffset.DateTime) : null;
}
