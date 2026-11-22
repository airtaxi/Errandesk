using System.Text;
using Errandesk.Contracts;

namespace Errandesk.Client.Sync;

/// <summary>
/// Applies requests to stored lists, tasks and notes the way the server applies them, so a change shows on this device before the server confirms it.
/// </summary>
internal static class LocalEditRules
{
    /// <summary>Length of the note excerpts in task responses, as the server creates them.</summary>
    public const int ExcerptLength = 160;

    /// <summary>
    /// Removes the part below a millisecond and converts to UTC, as the server stores times.
    /// </summary>
    public static DateTimeOffset Truncate(DateTimeOffset value) => new(value.UtcTicks - (value.UtcTicks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);

    public static TaskListResponse CreateList(Guid id, string name, IReadOnlyList<TaskListResponse> lists, DateTimeOffset now) => new(id, name.Trim(), lists.Count == 0 ? 0 : Math.Floor(lists.Max(list => list.SortOrder)) + 1, false, now, now, 0);

    public static TaskListResponse ApplyListUpdate(TaskListResponse list, UpdateTaskListRequest request, DateTimeOffset now) => list with
    {
        Name = request.Name.IsSpecified ? request.Name.Value.Trim() : list.Name,
        SortOrder = request.SortOrder.IsSpecified ? request.SortOrder.Value : list.SortOrder,
        UpdatedAt = now,
    };

    /// <summary>
    /// A new task at the top of its list, unless the request sets a sort order.
    /// </summary>
    public static TaskItemResponse CreateTask(CreateTaskRequest request, Guid id, Guid listId, IReadOnlyList<TaskItemResponse> tasksInList, DateOnly today, DateTimeOffset now)
    {
        var sortOrder = request.SortOrder ?? (tasksInList.Count == 0 ? 0 : Math.Ceiling(tasksInList.Min(task => task.SortOrder)) - 1);
        List<TaskStepResponse> steps = [];
        foreach (var stepRequest in request.Steps ?? []) steps.Add(new TaskStepResponse(stepRequest.Id ?? Guid.CreateVersion7(), stepRequest.Title.Trim(), stepRequest.IsCompleted, stepRequest.SortOrder ?? GetNextStepSortOrder(steps)));

        var task = new TaskItemResponse(id, listId, request.Title.Trim(), request.Body ?? "", request.IsImportant, request.IsCompleted, request.IsCompleted ? now : null, request.DueDate, request.IsInMyDay ? today : null, request.IsInMyDay, sortOrder, steps, now, now, 0, [], request.ReminderAt is { } reminderAt ? Truncate(reminderAt) : null);
        return request.Recurrence is { } recurrence ? ApplyRecurrence(task, recurrence, today) : task;
    }

    public static TaskItemResponse ApplyTaskUpdate(TaskItemResponse task, UpdateTaskRequest request, DateOnly today, DateTimeOffset now)
    {
        var updatedTask = task with { UpdatedAt = now };
        if (request.ListId.IsSpecified) updatedTask = updatedTask with { ListId = request.ListId.Value };
        if (request.Title.IsSpecified) updatedTask = updatedTask with { Title = request.Title.Value.Trim() };
        if (request.Body.IsSpecified) updatedTask = updatedTask with { Body = request.Body.Value ?? "" };
        if (request.IsImportant.IsSpecified) updatedTask = updatedTask with { IsImportant = request.IsImportant.Value };
        if (request.IsCompleted.IsSpecified && request.IsCompleted.Value != task.IsCompleted) updatedTask = updatedTask with { IsCompleted = request.IsCompleted.Value, CompletedAt = request.IsCompleted.Value ? now : null };
        if (request.DueDate.IsSpecified) updatedTask = updatedTask with { DueDate = request.DueDate.Value, Recurrence = request.DueDate.Value is null ? null : updatedTask.Recurrence };
        if (request.IsInMyDay.IsSpecified) updatedTask = updatedTask with { MyDayDate = request.IsInMyDay.Value ? today : null, IsInMyDay = request.IsInMyDay.Value };
        if (request.SortOrder.IsSpecified) updatedTask = updatedTask with { SortOrder = request.SortOrder.Value };
        if (request.ReminderAt.IsSpecified) updatedTask = updatedTask with { ReminderAt = request.ReminderAt.Value is { } reminderAt ? Truncate(reminderAt) : null, ReminderSetByUserId = null };
        if (request.Recurrence.IsSpecified) updatedTask = request.Recurrence.Value is { } recurrence ? ApplyRecurrence(updatedTask, recurrence, today) : updatedTask with { Recurrence = null };
        return updatedTask;
    }

    /// <summary>
    /// Adds a step after the last step. A step whose ID the task already has leaves the task unchanged.
    /// </summary>
    public static TaskItemResponse AddStep(TaskItemResponse task, CreateTaskStepRequest request, Guid stepId, DateTimeOffset now)
    {
        if (task.Steps.Any(step => step.Id == stepId)) return task;
        TaskStepResponse[] steps = [.. task.Steps, new TaskStepResponse(stepId, request.Title.Trim(), request.IsCompleted, request.SortOrder ?? GetNextStepSortOrder(task.Steps))];
        return task with { Steps = [.. steps.OrderBy(step => step.SortOrder)], UpdatedAt = now };
    }

    /// <exception cref="LocalItemNotFoundException">The task has no step with <paramref name="stepId"/>.</exception>
    public static TaskItemResponse ApplyStepUpdate(TaskItemResponse task, Guid stepId, UpdateTaskStepRequest request, DateTimeOffset now)
    {
        if (task.Steps.All(step => step.Id != stepId)) throw new LocalItemNotFoundException("step", stepId);

        var steps = task.Steps.Select(step => step.Id == stepId ? ApplyStepUpdate(step, request) : step);
        return task with { Steps = [.. steps.OrderBy(step => step.SortOrder)], UpdatedAt = now };
    }

    private static TaskStepResponse ApplyStepUpdate(TaskStepResponse step, UpdateTaskStepRequest request) => step with
    {
        Title = request.Title.IsSpecified ? request.Title.Value.Trim() : step.Title,
        IsCompleted = request.IsCompleted.IsSpecified ? request.IsCompleted.Value : step.IsCompleted,
        SortOrder = request.SortOrder.IsSpecified ? request.SortOrder.Value : step.SortOrder,
    };

    public static TaskItemResponse RemoveStep(TaskItemResponse task, Guid stepId, DateTimeOffset now) => task with { Steps = [.. task.Steps.Where(step => step.Id != stepId)], UpdatedAt = now };

    public static NoteResponse CreateNote(CreateNoteRequest request, Guid id, IEnumerable<Guid> linkedTaskIds, DateTimeOffset now) => new(id, (request.Title ?? "").Trim(), request.Content ?? "", NormalizeTags(request.Tags), [.. linkedTaskIds.Distinct()], request.IsPinned, now, now, 0);

    public static NoteResponse ApplyNoteUpdate(NoteResponse note, UpdateNoteRequest request, DateTimeOffset now) => note with
    {
        Title = request.Title.IsSpecified ? (request.Title.Value ?? "").Trim() : note.Title,
        Content = request.Content.IsSpecified ? request.Content.Value ?? "" : note.Content,
        Tags = request.Tags.IsSpecified ? NormalizeTags(request.Tags.Value) : note.Tags,
        IsPinned = request.IsPinned.IsSpecified ? request.IsPinned.Value : note.IsPinned,
        UpdatedAt = now,
    };

    public static NoteResponse LinkTask(NoteResponse note, Guid taskId, DateTimeOffset now) => note.LinkedTaskIds.Contains(taskId) ? note : note with { LinkedTaskIds = [.. note.LinkedTaskIds, taskId], UpdatedAt = now };

    public static NoteResponse UnlinkTask(NoteResponse note, Guid taskId, DateTimeOffset now) => !note.LinkedTaskIds.Contains(taskId) ? note : note with { LinkedTaskIds = [.. note.LinkedTaskIds.Where(linkedTaskId => linkedTaskId != taskId)], UpdatedAt = now };

    /// <summary>
    /// Trims tags, removes a leading <c>#</c>, lower-cases them and drops empty tags and duplicates, keeping the first order.
    /// </summary>
    public static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tags) =>
    [.. (tags ?? []).Select(NormalizeTag).Where(tag => tag.Length > 0).Distinct(StringComparer.Ordinal)];

    public static string NormalizeTag(string tag) => tag.Trim().TrimStart('#').ToLowerInvariant();

    /// <summary>
    /// The first <see cref="ExcerptLength"/> characters of <paramref name="content"/> after collapsing every run of white space into one space and trimming.
    /// </summary>
    public static string CreateExcerpt(string content)
    {
        var excerptBuilder = new StringBuilder(ExcerptLength);
        var isPendingSpace = false;
        foreach (var character in content)
        {
            if (char.IsWhiteSpace(character))
            {
                isPendingSpace = excerptBuilder.Length > 0;
                continue;
            }

            if (isPendingSpace) excerptBuilder.Append(' ');
            isPendingSpace = false;
            excerptBuilder.Append(character);
            if (excerptBuilder.Length >= ExcerptLength) break;
        }

        return excerptBuilder.ToString();
    }

    /// <summary>
    /// Sets a repeat rule with its days in Monday-first order. A task without a due date becomes due today, or on the first chosen day of a weekly rule from today on.
    /// </summary>
    public static TaskItemResponse ApplyRecurrence(TaskItemResponse task, TaskRecurrence recurrence, DateOnly today)
    {
        DayOfWeek[] daysOfWeek = recurrence.Frequency == RecurrenceFrequency.Weekly ? [.. (recurrence.DaysOfWeek ?? []).Distinct().OrderBy(GetMondayBasedIndex)] : [];
        var normalizedRecurrence = new TaskRecurrence(recurrence.Frequency, Math.Max(1, recurrence.Interval), daysOfWeek);
        return task with { Recurrence = normalizedRecurrence, DueDate = task.DueDate ?? GetFirstDueDate(today, normalizedRecurrence) };
    }

    private static DateOnly GetFirstDueDate(DateOnly today, TaskRecurrence recurrence)
    {
        var daysOfWeek = recurrence.DaysOfWeek ?? [];
        if (recurrence.Frequency != RecurrenceFrequency.Weekly || daysOfWeek.Count == 0) return today;

        var firstDueDate = today;
        while (!daysOfWeek.Contains(firstDueDate.DayOfWeek)) firstDueDate = firstDueDate.AddDays(1);
        return firstDueDate;
    }

    private static int GetMondayBasedIndex(DayOfWeek dayOfWeek) => ((int)dayOfWeek + 6) % 7;

    private static double GetNextStepSortOrder(IReadOnlyList<TaskStepResponse> steps) => steps.Count == 0 ? 0 : Math.Floor(steps.Max(step => step.SortOrder)) + 1;
}
