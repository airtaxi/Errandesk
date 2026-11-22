using Errandesk.Contracts;

namespace Errandesk.Server.Data;

/// <summary>
/// The repeat rule embedded in a <see cref="TaskDocument"/>.
/// </summary>
public sealed class TaskRecurrenceDocument
{
    public RecurrenceFrequency Frequency { get; set; }

    /// <summary>Number of frequency units between occurrences, at least 1.</summary>
    public int Interval { get; set; } = 1;

    /// <summary>Days of a weekly repeat, Monday first. Empty for other frequencies.</summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } = [];

    /// <summary>Day of the month of the due date when the rule or the due date was last set. Monthly and yearly occurrences return to it after shorter months.</summary>
    public int AnchorDayOfMonth { get; set; }
}
