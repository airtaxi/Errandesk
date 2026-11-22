namespace Errandesk.Server.Agent;

/// <summary>
/// A task as the morning briefing request describes it to the model.
/// </summary>
/// <param name="Title">Title of the task.</param>
/// <param name="List">Name of the task's list.</param>
/// <param name="DueDate">Due date as <c>yyyy-MM-dd</c>.</param>
/// <param name="IsImportant">Whether the task is marked as important.</param>
/// <param name="Steps">Progress of the steps, such as <c>1 of 3 done</c>.</param>
/// <param name="Reminder">Local date and time of the reminder as <c>yyyy-MM-dd HH:mm</c>.</param>
public sealed record MorningBriefingTaskData(string Title, string List, string? DueDate, bool IsImportant, string? Steps, string? Reminder);
