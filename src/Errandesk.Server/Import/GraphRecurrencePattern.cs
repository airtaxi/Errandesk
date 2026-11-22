namespace Errandesk.Server.Import;

/// <summary>
/// How often a Microsoft To Do task repeats (<c>recurrencePattern</c>).
/// </summary>
/// <param name="Type"><c>daily</c>, <c>weekly</c>, <c>absoluteMonthly</c>, <c>relativeMonthly</c>, <c>absoluteYearly</c> or <c>relativeYearly</c>.</param>
/// <param name="Interval">Number of days, weeks, months or years between occurrences.</param>
/// <param name="DaysOfWeek">Lower-case English day names, such as <c>monday</c>.</param>
public sealed record GraphRecurrencePattern(string? Type, int? Interval, IReadOnlyList<string>? DaysOfWeek);
