using Errandesk.Contracts;
using Errandesk.Server.Data;

namespace Errandesk.Server.Services;

/// <summary>
/// Calculates the due date of the next occurrence of a repeating task.
/// </summary>
public static class RecurrenceCalculator
{
    /// <summary>
    /// The due date that follows <paramref name="dueDate"/>:
    /// daily adds the interval in days; weekly moves to the next chosen day in the same Monday-to-Sunday week, or to the first chosen day of the week that is the interval later;
    /// monthly and yearly move by the interval on the anchor day of the month, or on the last day of a shorter month.
    /// </summary>
    public static DateOnly GetNextDueDate(DateOnly dueDate, TaskRecurrenceDocument recurrence)
    {
        var interval = Math.Max(1, recurrence.Interval);
        var anchorDayOfMonth = recurrence.AnchorDayOfMonth is >= 1 and <= 31 ? recurrence.AnchorDayOfMonth : dueDate.Day;
        return recurrence.Frequency switch
        {
            RecurrenceFrequency.Daily => dueDate.AddDays(interval),
            RecurrenceFrequency.Weekly => GetNextWeeklyDate(dueDate, interval, recurrence.DaysOfWeek),
            RecurrenceFrequency.Monthly => CreateClampedDate(new DateOnly(dueDate.Year, dueDate.Month, 1).AddMonths(interval), anchorDayOfMonth),
            RecurrenceFrequency.Yearly => CreateClampedDate(new DateOnly(dueDate.Year, dueDate.Month, 1).AddYears(interval), anchorDayOfMonth),
            _ => throw new ArgumentOutOfRangeException(nameof(recurrence), recurrence.Frequency, "Unknown recurrence frequency."),
        };
    }

    /// <summary>The due date a task gets when it starts repeating without one: <paramref name="today"/>, or for a weekly rule with days the first chosen day from today on.</summary>
    public static DateOnly GetFirstDueDate(DateOnly today, TaskRecurrence normalizedRecurrence)
    {
        var daysOfWeek = normalizedRecurrence.DaysOfWeek ?? [];
        if (normalizedRecurrence.Frequency != RecurrenceFrequency.Weekly || daysOfWeek.Count == 0) return today;

        var firstDueDate = today;
        while (!daysOfWeek.Contains(firstDueDate.DayOfWeek)) firstDueDate = firstDueDate.AddDays(1);
        return firstDueDate;
    }

    /// <summary>Position of the day in a week that starts on Monday, from 0 for Monday to 6 for Sunday.</summary>
    public static int GetMondayBasedIndex(DayOfWeek dayOfWeek) => ((int)dayOfWeek + 6) % 7;

    private static DateOnly GetNextWeeklyDate(DateOnly dueDate, int interval, IReadOnlyCollection<DayOfWeek> daysOfWeek)
    {
        var dayIndexes = daysOfWeek.Count == 0 ? [GetMondayBasedIndex(dueDate.DayOfWeek)] : daysOfWeek.Select(GetMondayBasedIndex).Distinct().Order().ToList();
        var dueDayIndex = GetMondayBasedIndex(dueDate.DayOfWeek);
        var weekStart = dueDate.AddDays(-dueDayIndex);
        var laterDayIndexes = dayIndexes.Where(dayIndex => dayIndex > dueDayIndex).ToList();
        return laterDayIndexes.Count > 0 ? weekStart.AddDays(laterDayIndexes[0]) : weekStart.AddDays((7 * interval) + dayIndexes[0]);
    }

    private static DateOnly CreateClampedDate(DateOnly firstDayOfMonth, int dayOfMonth) => new(firstDayOfMonth.Year, firstDayOfMonth.Month, Math.Min(dayOfMonth, DateTime.DaysInMonth(firstDayOfMonth.Year, firstDayOfMonth.Month)));
}
