namespace Errandesk.Contracts;

/// <summary>
/// Creates a task.
/// </summary>
/// <param name="Title">Task title.</param>
/// <param name="Id">Client-generated ID. Creating an ID that already exists for the same user returns the existing task.</param>
/// <param name="ListId">Target list. Defaults to the user's default list.</param>
/// <param name="Body">Plain-text note.</param>
/// <param name="IsImportant">Whether the task is important.</param>
/// <param name="IsCompleted">Whether the task starts completed.</param>
/// <param name="DueDate">Due date.</param>
/// <param name="IsInMyDay">Adds the task to today's My Day in the user's time zone.</param>
/// <param name="SortOrder">Position within the list. Defaults to the top.</param>
/// <param name="Steps">Initial steps.</param>
/// <param name="ReminderAt">When to remind the user.</param>
/// <param name="Recurrence">Repeat rule. Without a due date, the due date becomes today in the user's time zone, or the first chosen day of a weekly rule from today on.</param>
public sealed record CreateTaskRequest(string Title, Guid? Id = null, Guid? ListId = null, string? Body = null, bool IsImportant = false, bool IsCompleted = false, DateOnly? DueDate = null, bool IsInMyDay = false, double? SortOrder = null, IReadOnlyList<CreateTaskStepRequest>? Steps = null, DateTimeOffset? ReminderAt = null, TaskRecurrence? Recurrence = null);
