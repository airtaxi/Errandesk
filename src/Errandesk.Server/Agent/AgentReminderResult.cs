namespace Errandesk.Server.Agent;

/// <summary>
/// The result of the reminder tool.
/// </summary>
/// <param name="TaskId">ID of the task.</param>
/// <param name="Title">Title of the task.</param>
/// <param name="ReminderLocalTime">The reminder as local date and time (yyyy-MM-ddTHH:mm) in the user's time zone, or <see langword="null"/> when the reminder was removed.</param>
/// <param name="TimeZone">The user's time zone.</param>
public sealed record AgentReminderResult(Guid TaskId, string Title, string? ReminderLocalTime, string TimeZone);
