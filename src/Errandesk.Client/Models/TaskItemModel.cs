using System.Collections.ObjectModel;
using System.Globalization;
using Errandesk.Client.Helpers;
using Errandesk.Contracts;

namespace Errandesk.Client.Models;

/// <summary>
/// A task as shown in a task row and in the detail pane.
/// </summary>
public sealed partial class TaskItemModel : ObservableObject
{
    public TaskItemModel(TaskItemResponse response, TaskViewKind viewKind)
    {
        Id = response.Id;
        ViewKind = viewKind;
        Apply(response);
    }

    public Guid Id { get; }

    /// <summary>
    /// The kind of view that shows the task, which decides the parts of the metadata line.
    /// </summary>
    public TaskViewKind ViewKind { get; }

    public long ChangeVersion { get; private set; }

    public ObservableCollection<TaskStepModel> Steps { get; } = [];

    public ObservableCollection<LinkedNoteModel> LinkedNotes { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLinkedNotes), nameof(LinkedNoteCountText), nameof(HasMetadata))]
    public partial int LinkedNoteCount { get; set; }

    [ObservableProperty]
    public partial Guid ListId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListNameVisible), nameof(HasMetadata))]
    public partial string ListName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Body { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsImportant { get; set; }

    [ObservableProperty]
    public partial bool IsCompleted { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDueDate), nameof(DueDateText), nameof(RowReminderText), nameof(HasMetadata))]
    public partial DateOnly? DueDate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMyDayLabelVisible), nameof(HasMetadata))]
    public partial bool IsInMyDay { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReminder), nameof(ReminderText), nameof(RowReminderText), nameof(HasMetadata))]
    public partial DateTimeOffset? ReminderAt { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecurrence), nameof(RecurrenceText), nameof(HasMetadata))]
    public partial TaskRecurrence? Recurrence { get; set; }

    [ObservableProperty]
    public partial double SortOrder { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset CreatedAt { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSteps), nameof(StepsProgressText), nameof(HasMetadata))]
    public partial int StepCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepsProgressText))]
    public partial int CompletedStepCount { get; set; }

    public bool IsListNameVisible => ViewKind != TaskViewKind.List && !string.IsNullOrEmpty(ListName);

    public bool IsMyDayLabelVisible => IsInMyDay && ViewKind != TaskViewKind.MyDay;

    public bool HasSteps => StepCount > 0;

    public bool HasDueDate => DueDate is not null;

    public bool HasLinkedNotes => LinkedNoteCount > 0;

    public string LinkedNoteCountText => string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get(LinkedNoteCount == 1 ? "LinkedNoteCountSingularFormat" : "LinkedNoteCountFormat"), LinkedNoteCount);

    public bool HasReminder => ReminderAt is not null;

    /// <summary>
    /// The reminder time in local time, such as "Tomorrow, 9:00 AM"; empty without a reminder.
    /// </summary>
    public string ReminderText => ReminderAt is { } reminderAt ? DateDisplayHelper.FormatReminder(reminderAt) : string.Empty;

    /// <summary>
    /// The reminder in the task row: only the time of day when the reminder falls on the due date shown beside it.
    /// </summary>
    public string RowReminderText => ReminderAt is not { } reminderAt ? string.Empty : DueDate == DateOnly.FromDateTime(reminderAt.ToLocalTime().DateTime) ? DateDisplayHelper.FormatTime(reminderAt) : DateDisplayHelper.FormatReminder(reminderAt);

    public bool HasRecurrence => Recurrence is not null;

    /// <summary>
    /// The repeat rule, such as "Weekdays" or "Every 2 weeks on Mon"; empty when the task does not repeat.
    /// </summary>
    public string RecurrenceText => Recurrence is { } recurrence ? RecurrenceDisplayHelper.Describe(recurrence) : string.Empty;

    public bool HasMetadata => IsListNameVisible || IsMyDayLabelVisible || HasSteps || HasDueDate || HasReminder || HasRecurrence || HasLinkedNotes;

    public string StepsProgressText => string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("StepsProgressFormat"), CompletedStepCount, StepCount);

    public string DueDateText => DueDate is { } dueDate ? DateDisplayHelper.FormatDueDate(dueDate, DateDisplayHelper.Today) : string.Empty;

    /// <summary>
    /// The title, which list items use as their accessible name.
    /// </summary>
    public override string ToString() => Title;

    /// <summary>
    /// Copies the values of <paramref name="response"/> unless it is older than the values already shown.
    /// </summary>
    /// <returns><see langword="true"/> when the values were copied.</returns>
    public bool Apply(TaskItemResponse response)
    {
        if (response.ChangeVersion < ChangeVersion) return false;

        ChangeVersion = response.ChangeVersion;
        ListId = response.ListId;
        Title = response.Title;
        Body = response.Body;
        IsImportant = response.IsImportant;
        IsCompleted = response.IsCompleted;
        DueDate = response.DueDate;
        IsInMyDay = response.IsInMyDay;
        ReminderAt = response.ReminderAt;
        if (!AreSameRecurrence(Recurrence, response.Recurrence)) Recurrence = response.Recurrence;
        SortOrder = response.SortOrder;
        CreatedAt = response.CreatedAt;
        ApplySteps(response.Steps);
        ApplyLinkedNotes(response.LinkedNotes);
        return true;
    }

    private static bool AreSameRecurrence(TaskRecurrence? first, TaskRecurrence? second)
    {
        if (first is null || second is null) return first is null && second is null;
        return first.Frequency == second.Frequency && first.Interval == second.Interval && (first.DaysOfWeek ?? []).SequenceEqual(second.DaysOfWeek ?? []);
    }

    private void ApplyLinkedNotes(IReadOnlyList<NoteSummaryResponse> linkedNotes)
    {
        var hasSameNotes = LinkedNotes.Count == linkedNotes.Count && LinkedNotes.Zip(linkedNotes).All(pair => pair.First.Id == pair.Second.Id && pair.First.UpdatedAt == pair.Second.UpdatedAt);
        if (!hasSameNotes)
        {
            LinkedNotes.Clear();
            foreach (var linkedNote in linkedNotes) LinkedNotes.Add(new LinkedNoteModel(linkedNote));
        }

        LinkedNoteCount = linkedNotes.Count;
    }

    private void ApplySteps(IReadOnlyList<TaskStepResponse> steps)
    {
        var hasSameSteps = Steps.Count == steps.Count && Steps.Select(step => step.Id).SequenceEqual(steps.Select(step => step.Id));
        if (hasSameSteps)
        {
            for (var index = 0; index < steps.Count; index++)
            {
                Steps[index].Apply(steps[index]);
            }
        }
        else
        {
            Steps.Clear();
            foreach (var step in steps) Steps.Add(new TaskStepModel(step));
        }

        StepCount = steps.Count;
        CompletedStepCount = steps.Count(step => step.IsCompleted);
    }
}
