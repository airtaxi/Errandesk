namespace Errandesk.Contracts;

/// <summary>
/// The repeat rule of a task. Completing a repeating task creates its next occurrence.
/// </summary>
/// <param name="Frequency">Unit of the repeat.</param>
/// <param name="Interval">Number of units between occurrences, at least 1.</param>
/// <param name="DaysOfWeek">Days of a weekly repeat, Monday first. Empty for other frequencies; a weekly repeat without days uses the weekday of the due date.</param>
public sealed record TaskRecurrence(RecurrenceFrequency Frequency, int Interval = 1, IReadOnlyList<DayOfWeek>? DaysOfWeek = null);
