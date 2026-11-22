namespace Errandesk.Server.Import;

/// <summary>
/// A Microsoft To Do task (<c>todoTask</c>) with the properties the import reads.
/// </summary>
/// <param name="Importance"><c>low</c>, <c>normal</c> or <c>high</c>.</param>
/// <param name="Status"><c>notStarted</c>, <c>inProgress</c>, <c>completed</c>, <c>waitingOnOthers</c> or <c>deferred</c>.</param>
public sealed record GraphTodoTask(string Id, string? Title, GraphItemBody? Body, string? Importance, string? Status, DateTimeOffset? CreatedDateTime, GraphDateTimeTimeZone? CompletedDateTime, GraphDateTimeTimeZone? DueDateTime, GraphDateTimeTimeZone? ReminderDateTime, bool? IsReminderOn, GraphPatternedRecurrence? Recurrence)
{
    public bool IsCompleted => string.Equals(Status, "completed", StringComparison.OrdinalIgnoreCase);

    public bool IsImportant => string.Equals(Importance, "high", StringComparison.OrdinalIgnoreCase);
}
