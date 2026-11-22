using Errandesk.Contracts;
using Errandesk.Server.Data;

namespace Errandesk.Server.Services;

/// <summary>
/// Validates repeat rules and converts them between the contract and the stored form.
/// </summary>
public static class TaskRecurrenceRules
{
    public const int MaximumInterval = 999;

    /// <summary>Monday to Friday, the days of the "weekdays" repeat.</summary>
    public static IReadOnlyList<DayOfWeek> Weekdays { get; } = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    /// <summary>Returns the rule with distinct days in Monday-first order, or throws when the frequency, interval or days are invalid.</summary>
    public static TaskRecurrence Normalize(TaskRecurrence recurrence)
    {
        if (!Enum.IsDefined(recurrence.Frequency)) throw new RequestValidationException("The repeat frequency must be daily, weekly, monthly or yearly.");
        if (recurrence.Interval is < 1 or > MaximumInterval) throw new RequestValidationException($"The repeat interval must be between 1 and {MaximumInterval}.");

        var daysOfWeek = recurrence.DaysOfWeek ?? [];
        if (daysOfWeek.Any(dayOfWeek => !Enum.IsDefined(dayOfWeek))) throw new RequestValidationException("The repeat days must be days of the week.");
        if (daysOfWeek.Count > 0 && recurrence.Frequency != RecurrenceFrequency.Weekly) throw new RequestValidationException("Repeat days can only be set for a weekly repeat.");
        return recurrence with { DaysOfWeek = [.. daysOfWeek.Distinct().OrderBy(RecurrenceCalculator.GetMondayBasedIndex)] };
    }

    /// <summary>The stored form of a normalized rule, anchored on the day of the month of <paramref name="dueDate"/>.</summary>
    public static TaskRecurrenceDocument ToDocument(TaskRecurrence normalizedRecurrence, DateOnly dueDate) => new()
    {
        Frequency = normalizedRecurrence.Frequency,
        Interval = normalizedRecurrence.Interval,
        DaysOfWeek = normalizedRecurrence.DaysOfWeek?.ToList() ?? [],
        AnchorDayOfMonth = dueDate.Day,
    };

    public static TaskRecurrence ToResponse(this TaskRecurrenceDocument recurrence) => new(recurrence.Frequency, recurrence.Interval, [.. recurrence.DaysOfWeek]);

    public static TaskRecurrenceDocument Copy(this TaskRecurrenceDocument recurrence) => new()
    {
        Frequency = recurrence.Frequency,
        Interval = recurrence.Interval,
        DaysOfWeek = recurrence.DaysOfWeek.ToList(),
        AnchorDayOfMonth = recurrence.AnchorDayOfMonth,
    };
}
