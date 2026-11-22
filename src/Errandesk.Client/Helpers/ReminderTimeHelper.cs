namespace Errandesk.Client.Helpers;

/// <summary>
/// Computes the suggested reminder times of the reminder menu, in the local time of this device.
/// </summary>
public static class ReminderTimeHelper
{
    /// <summary>
    /// The hour of the day used by the Tomorrow and Next week suggestions.
    /// </summary>
    public const int DefaultReminderHour = 9;

    private const int LaterTodayHours = 3;

    /// <summary>
    /// Three hours from <paramref name="now"/>, rounded up to the full hour, or <see langword="null"/> when that is no longer today.
    /// </summary>
    public static DateTimeOffset? GetLaterToday(DateTimeOffset now)
    {
        var localNow = now.ToLocalTime().DateTime;
        var candidate = localNow.AddHours(LaterTodayHours);
        var roundedCandidate = new DateTime(candidate.Year, candidate.Month, candidate.Day, candidate.Hour, 0, 0, DateTimeKind.Unspecified);
        if (roundedCandidate < candidate) roundedCandidate = roundedCandidate.AddHours(1);
        return roundedCandidate.Date == localNow.Date ? ToLocalOffset(roundedCandidate) : null;
    }

    /// <summary>
    /// Tomorrow at <see cref="DefaultReminderHour"/>.
    /// </summary>
    public static DateTimeOffset GetTomorrow(DateTimeOffset now) => ToLocalOffset(now.ToLocalTime().Date.AddDays(1).AddHours(DefaultReminderHour));

    /// <summary>
    /// Next Monday at <see cref="DefaultReminderHour"/>; on a Monday this is the Monday a week later.
    /// </summary>
    public static DateTimeOffset GetNextWeek(DateTimeOffset now)
    {
        var today = now.ToLocalTime().Date;
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return ToLocalOffset(today.AddDays(daysUntilMonday == 0 ? 7 : daysUntilMonday).AddHours(DefaultReminderHour));
    }

    /// <summary>
    /// The time a new custom reminder starts from: the due date at <see cref="DefaultReminderHour"/> when that is still ahead, otherwise tomorrow.
    /// </summary>
    public static DateTimeOffset GetCustomDefault(DateOnly? dueDate, DateTimeOffset now)
    {
        if (dueDate is { } date)
        {
            var dueDateReminder = ToLocalOffset(date.ToDateTime(new TimeOnly(DefaultReminderHour, 0)));
            if (dueDateReminder > now) return dueDateReminder;
        }

        return GetTomorrow(now);
    }

    /// <summary>
    /// Combines a local date and time of day into a point in time with this device's offset at that moment.
    /// </summary>
    public static DateTimeOffset Combine(DateOnly date, TimeSpan timeOfDay) => ToLocalOffset(date.ToDateTime(TimeOnly.MinValue).Add(timeOfDay));

    private static DateTimeOffset ToLocalOffset(DateTime localDateTime)
    {
        var unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        var timeZone = TimeZoneInfo.Local;
        if (timeZone.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);
        return new DateTimeOffset(unspecified, timeZone.GetUtcOffset(unspecified));
    }
}
