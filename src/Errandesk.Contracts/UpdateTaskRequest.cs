using System.Text.Json.Serialization;

namespace Errandesk.Contracts;

/// <summary>
/// Changes a task. Only specified properties change; <c>null</c> clears the body or the due date.
/// </summary>
public sealed record UpdateTaskRequest
{
    /// <summary>Moves the task to another list of the user.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<Guid> ListId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Title { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Body { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsImportant { get; init; }

    /// <summary>Completing sets the completion time; reopening clears it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsCompleted { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateOnly?> DueDate { get; init; }

    /// <summary><see langword="true"/> adds the task to today's My Day in the user's time zone; <see langword="false"/> removes it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsInMyDay { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double> SortOrder { get; init; }

    /// <summary>Sets the reminder time, or removes the reminder with <see langword="null"/>. A new time is reminded again even if an earlier reminder was sent.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> ReminderAt { get; init; }

    /// <summary>Sets the repeat rule, or stops repeating with <see langword="null"/>. Without a due date, the due date becomes today in the user's time zone, or the first chosen day of a weekly rule from today on. Removing the due date also stops repeating.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<TaskRecurrence?> Recurrence { get; init; }

    /// <summary>When set, the change applies only if the task still has this change version; otherwise the server answers 409 with the current task.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ExpectedChangeVersion { get; init; }
}
