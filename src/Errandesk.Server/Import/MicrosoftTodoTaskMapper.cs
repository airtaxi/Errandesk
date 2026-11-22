using System.Globalization;
using Errandesk.Contracts;
using Errandesk.Server.Data;
using Errandesk.Server.Services;

namespace Errandesk.Server.Import;

/// <summary>
/// Converts Microsoft To Do lists, tasks and checklist items into Errandesk documents.
/// </summary>
public static class MicrosoftTodoTaskMapper
{
    public const string UntitledListName = "Untitled list";
    public const string UntitledTaskTitle = "Untitled task";

    /// <summary>The trimmed list name, shortened to the longest allowed name, or <see cref="UntitledListName"/> when it is blank.</summary>
    public static string MapListName(string? displayName) => Shorten(displayName, InputValidation.MaximumListNameLength) ?? UntitledListName;

    /// <summary>
    /// A new task document with the title, plain-text body, importance, completion, due date, reminder, repeat rule and steps of <paramref name="source"/>.
    /// The caller sets the ID, list and sort order. Reminders already due are marked as handled so the import sends no notifications, and completed tasks keep no reminder or repeat rule.
    /// </summary>
    /// <param name="source">The To Do task.</param>
    /// <param name="checklistItems">The task's checklist items in Graph order.</param>
    /// <param name="ownerUserId">The importing user, who also becomes the user who set the reminder.</param>
    /// <param name="userTimeZone">IANA time zone of the importing user, used to find the calendar date of due dates stored as instants.</param>
    /// <param name="today">Today in the user's time zone, the first due date of a repeating task without one.</param>
    /// <param name="utcNow">The current UTC time.</param>
    public static TaskDocument CreateTask(GraphTodoTask source, IReadOnlyList<GraphChecklistItem> checklistItems, Guid ownerUserId, string userTimeZone, DateOnly today, DateTime utcNow)
    {
        var isCompleted = source.IsCompleted;
        var task = new TaskDocument
        {
            Title = Shorten(source.Title, InputValidation.MaximumTaskTitleLength) ?? UntitledTaskTitle,
            Body = MapBody(source.Body),
            IsImportant = source.IsImportant,
            IsCompleted = isCompleted,
            CompletedAt = isCompleted ? ToUtc(source.CompletedDateTime) ?? utcNow : null,
            DueDate = MapDueDate(source.DueDateTime, userTimeZone),
            Steps = MapSteps(checklistItems),
            ExternalSource = ImportSources.MicrosoftTodo,
            ExternalId = source.Id,
        };

        if (!isCompleted && source.IsReminderOn == true && ToUtc(source.ReminderDateTime) is { } reminderAt && reminderAt.Year is >= InputValidation.MinimumReminderYear and <= InputValidation.MaximumReminderYear)
        {
            task.SetReminder(TruncateToMilliseconds(reminderAt), ownerUserId);
            if (task.ReminderAt <= utcNow) task.ReminderSentAt = utcNow;
        }

        if (!isCompleted && MapRecurrence(source.Recurrence?.Pattern) is { } recurrence)
        {
            task.DueDate ??= RecurrenceCalculator.GetFirstDueDate(today, recurrence);
            task.Recurrence = TaskRecurrenceRules.ToDocument(recurrence, task.DueDate.Value);
        }

        return task;
    }

    /// <summary>The body as plain text with <c>\n</c> line breaks: HTML bodies lose their markup. Empty when there is no body.</summary>
    public static string MapBody(GraphItemBody? body)
    {
        if (string.IsNullOrWhiteSpace(body?.Content)) return "";
        var text = body.IsHtml ? HtmlTextConverter.ToPlainText(body.Content) : body.Content.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return text.Length > InputValidation.MaximumTaskBodyLength ? text[..InputValidation.MaximumTaskBodyLength] : text;
    }

    /// <summary>
    /// The calendar date of a To Do due date. A value at midnight is taken as that date; another time of day is converted to the user's time zone first.
    /// </summary>
    public static DateOnly? MapDueDate(GraphDateTimeTimeZone? dueDateTime, string userTimeZone)
    {
        if (!TryParseDateTime(dueDateTime?.DateTime, out var dateTime)) return null;
        if (dateTime.Kind == DateTimeKind.Unspecified && dateTime.TimeOfDay == TimeSpan.Zero) return DateOnly.FromDateTime(dateTime);
        return ToUtc(dueDateTime) is { } utcValue ? DateOnly.FromDateTime(UserTimeZones.ToLocal(utcValue, userTimeZone).DateTime) : null;
    }

    /// <summary>
    /// The UTC instant of a Graph date and time. The time zone may be a Windows or an IANA ID; an unknown time zone counts as UTC.
    /// </summary>
    public static DateTime? ToUtc(GraphDateTimeTimeZone? value)
    {
        if (!TryParseDateTime(value?.DateTime, out var dateTime)) return null;
        return dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime,
            DateTimeKind.Local => dateTime.ToUniversalTime(),
            _ => UserTimeZones.ToUtc(dateTime, string.IsNullOrWhiteSpace(value?.TimeZone) ? "UTC" : value.TimeZone),
        };
    }

    /// <summary>
    /// The Errandesk repeat rule of a To Do pattern: daily, weekly with its days, monthly on a day of the month, or yearly on a date.
    /// Patterns on a weekday position in the month (<c>relativeMonthly</c>, <c>relativeYearly</c>) have no Errandesk equivalent and give <see langword="null"/>.
    /// </summary>
    public static TaskRecurrence? MapRecurrence(GraphRecurrencePattern? pattern)
    {
        if (pattern?.Type is not { } type) return null;

        RecurrenceFrequency? frequency = type.ToLowerInvariant() switch
        {
            "daily" => RecurrenceFrequency.Daily,
            "weekly" => RecurrenceFrequency.Weekly,
            "absolutemonthly" => RecurrenceFrequency.Monthly,
            "absoluteyearly" => RecurrenceFrequency.Yearly,
            _ => null,
        };
        if (frequency is not { } mappedFrequency) return null;

        var interval = Math.Clamp(pattern.Interval ?? 1, 1, TaskRecurrenceRules.MaximumInterval);
        DayOfWeek[] daysOfWeek = mappedFrequency == RecurrenceFrequency.Weekly ? [.. (pattern.DaysOfWeek ?? []).Select(day => Enum.TryParse<DayOfWeek>(day, true, out var dayOfWeek) && Enum.IsDefined(dayOfWeek) ? dayOfWeek : (DayOfWeek?)null).OfType<DayOfWeek>()] : [];
        return TaskRecurrenceRules.Normalize(new TaskRecurrence(mappedFrequency, interval, daysOfWeek));
    }

    /// <summary>Steps in creation order with their completion; blank items are left out and at most <see cref="InputValidation.MaximumStepCount"/> are kept.</summary>
    public static List<TaskStepDocument> MapSteps(IReadOnlyList<GraphChecklistItem> checklistItems)
    {
        var namedItems = checklistItems.Select((item, index) => (Item: item, Index: index, Title: Shorten(item.DisplayName, InputValidation.MaximumStepTitleLength))).Where(entry => entry.Title is not null);
        var orderedItems = namedItems.OrderBy(entry => entry.Item.CreatedDateTime ?? DateTimeOffset.MaxValue).ThenBy(entry => entry.Index).Take(InputValidation.MaximumStepCount);
        return [.. orderedItems.Select((entry, sortOrder) => new TaskStepDocument { Id = Guid.CreateVersion7(), Title = entry.Title!, IsCompleted = entry.Item.IsChecked == true, SortOrder = sortOrder })];
    }

    /// <summary>The UTC time truncated to milliseconds, the precision MongoDB stores.</summary>
    public static DateTime TruncateToMilliseconds(DateTime utcValue) => DateTime.SpecifyKind(utcValue.AddTicks(-(utcValue.Ticks % TimeSpan.TicksPerMillisecond)), DateTimeKind.Utc);

    private static bool TryParseDateTime(string? value, out DateTime dateTime) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dateTime);

    private static string? Shorten(string? value, int maximumLength)
    {
        var trimmedValue = value?.Trim();
        if (string.IsNullOrEmpty(trimmedValue)) return null;
        return trimmedValue.Length > maximumLength ? trimmedValue[..maximumLength].TrimEnd() : trimmedValue;
    }
}
