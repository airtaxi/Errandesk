namespace Errandesk.Contracts;

/// <summary>
/// A task with its steps.
/// </summary>
/// <param name="Id">Task ID.</param>
/// <param name="ListId">List that contains the task.</param>
/// <param name="Title">Task title.</param>
/// <param name="Body">Plain-text note. Empty when there is none.</param>
/// <param name="IsImportant">Whether the task is marked important.</param>
/// <param name="IsCompleted">Whether the task is completed.</param>
/// <param name="CompletedAt">When the task was completed.</param>
/// <param name="DueDate">Due date, without a time.</param>
/// <param name="MyDayDate">Date on which the requesting user added the task to their My Day. My Day is personal, also in shared lists.</param>
/// <param name="IsInMyDay">Whether <paramref name="MyDayDate"/> is today in the user's time zone.</param>
/// <param name="SortOrder">Ascending position within the list.</param>
/// <param name="Steps">Steps in ascending sort order.</param>
/// <param name="CreatedAt">When the task was created.</param>
/// <param name="UpdatedAt">When the task was last changed.</param>
/// <param name="ChangeVersion">Global change version of the last change.</param>
/// <param name="LinkedNotes">The user's notes linked to the task, pinned notes first, then the most recently changed.</param>
/// <param name="ReminderAt">When the reminder is due, in UTC.</param>
/// <param name="Recurrence">The repeat rule, or <see langword="null"/> when the task does not repeat.</param>
/// <param name="NextOccurrenceId">ID of the occurrence created when this repeating task was completed.</param>
/// <param name="ReminderSetByUserId">The user who set the reminder. Only that user is notified.</param>
public sealed record TaskItemResponse(Guid Id, Guid ListId, string Title, string Body, bool IsImportant, bool IsCompleted, DateTimeOffset? CompletedAt, DateOnly? DueDate, DateOnly? MyDayDate, bool IsInMyDay, double SortOrder, IReadOnlyList<TaskStepResponse> Steps, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long ChangeVersion, IReadOnlyList<NoteSummaryResponse> LinkedNotes, DateTimeOffset? ReminderAt = null, TaskRecurrence? Recurrence = null, Guid? NextOccurrenceId = null, Guid? ReminderSetByUserId = null);
