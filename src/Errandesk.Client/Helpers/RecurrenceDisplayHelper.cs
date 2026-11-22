using System.Globalization;
using Errandesk.Contracts;

namespace Errandesk.Client.Helpers;

/// <summary>
/// Describes repeat rules in the interface language and builds the rules of the repeat menu.
/// </summary>
public static class RecurrenceDisplayHelper
{
    /// <summary>
    /// The days of the week, Monday first, as the server orders them.
    /// </summary>
    public static IReadOnlyList<DayOfWeek> DaysMondayFirst { get; } = (DayOfWeek[])[DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <summary>
    /// Monday to Friday, the days of the weekdays repeat.
    /// </summary>
    public static IReadOnlyList<DayOfWeek> Weekdays { get; } = (DayOfWeek[])[DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    /// <summary>
    /// A short description such as "Daily", "Weekdays", "Weekly on Mon, Thu" or "Every 2 months".
    /// </summary>
    public static string Describe(TaskRecurrence recurrence)
    {
        var interval = Math.Max(1, recurrence.Interval);
        var days = SortDays(recurrence.DaysOfWeek);
        return recurrence.Frequency switch
        {
            RecurrenceFrequency.Daily => interval == 1 ? LocalizedStrings.Get("RecurrenceDailyText") : Format("RecurrenceEveryDaysFormat", interval),
            RecurrenceFrequency.Weekly => DescribeWeekly(interval, days),
            RecurrenceFrequency.Monthly => interval == 1 ? LocalizedStrings.Get("RecurrenceMonthlyText") : Format("RecurrenceEveryMonthsFormat", interval),
            RecurrenceFrequency.Yearly => interval == 1 ? LocalizedStrings.Get("RecurrenceYearlyText") : Format("RecurrenceEveryYearsFormat", interval),
            _ => string.Empty,
        };
    }

    /// <summary>
    /// The abbreviated name of <paramref name="dayOfWeek"/> in the interface culture.
    /// </summary>
    public static string GetShortDayName(DayOfWeek dayOfWeek) => CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(dayOfWeek);

    /// <summary>
    /// The days of a weekly rule in Monday-first order without duplicates.
    /// </summary>
    public static List<DayOfWeek> SortDays(IEnumerable<DayOfWeek>? days)
    {
        var daySet = days?.ToHashSet() ?? [];
        return [.. DaysMondayFirst.Where(daySet.Contains)];
    }

    private static string DescribeWeekly(int interval, List<DayOfWeek> days)
    {
        if (interval == 1 && days.SequenceEqual(Weekdays)) return LocalizedStrings.Get("RecurrenceWeekdaysText");

        var dayNames = string.Join(", ", days.Select(GetShortDayName));
        if (interval == 1) return days.Count == 0 ? LocalizedStrings.Get("RecurrenceWeeklyText") : Format("RecurrenceWeeklyOnFormat", dayNames);
        return days.Count == 0 ? Format("RecurrenceEveryWeeksFormat", interval) : Format("RecurrenceEveryWeeksOnFormat", interval, dayNames);
    }

    private static string Format(string resourceName, params object[] arguments) => string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get(resourceName), arguments);
}
