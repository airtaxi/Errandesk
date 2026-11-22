using System.Collections.ObjectModel;
using System.Globalization;
using Errandesk.Client.Helpers;
using Errandesk.Client.Managers;
using Errandesk.Client.Models;
using Errandesk.Client.Services;
using Errandesk.Contracts;

namespace Errandesk.Client.ViewModels;

/// <summary>
/// The detail pane of the selected task: title, completion, importance, steps, My Day, due date, note, linked notes and deletion.
/// </summary>
public sealed partial class TaskDetailViewModel(SyncManager syncManager, DialogService dialogService, NoteNavigationManager noteNavigationManager) : ObservableObject
{
    private const int MaximumNoteSuggestionCount = 8;
    private static readonly TimeSpan s_noteSearchDelay = TimeSpan.FromMilliseconds(250);

    private readonly SyncManager _syncManager = syncManager;
    private readonly DialogService _dialogService = dialogService;
    private readonly NoteNavigationManager _noteNavigationManager = noteNavigationManager;
    private CancellationTokenSource? _noteSearchCancellation;

    /// <summary>
    /// Raised after a change to the task was stored on this device.
    /// </summary>
    public event EventHandler<TaskItemModel>? TaskChanged;

    public event EventHandler<TaskItemModel>? TaskDeleted;

    public event EventHandler? CloseRequested;

    [ObservableProperty]
    public partial TaskItemModel? CurrentTask { get; set; }

    /// <summary>
    /// The due date in the form the date picker uses.
    /// </summary>
    [ObservableProperty]
    public partial DateTimeOffset? DueDateValue { get; set; }

    [ObservableProperty]
    public partial string NewStepTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CreatedText { get; set; } = string.Empty;

    /// <summary>
    /// Notes that match the text typed into the link box and are not linked to the task yet.
    /// </summary>
    public ObservableCollection<NoteItemModel> NoteSuggestions { get; } = [];

    [ObservableProperty]
    public partial bool IsErrorVisible { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// The "Later today" suggestion of the reminder menu, or <see langword="null"/> late in the day when it would fall on tomorrow.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLaterTodayReminderAvailable))]
    public partial DateTimeOffset? LaterTodayReminder { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset TomorrowReminder { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset NextWeekReminder { get; set; }

    [ObservableProperty]
    public partial string LaterTodayReminderText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TomorrowReminderText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NextWeekReminderText { get; set; } = string.Empty;

    public bool IsLaterTodayReminderAvailable => LaterTodayReminder is not null;

    /// <summary>
    /// Computes the reminder suggestions from the current time, before the reminder menu opens.
    /// </summary>
    public void RefreshReminderSuggestions()
    {
        var now = DateTimeOffset.Now;
        LaterTodayReminder = ReminderTimeHelper.GetLaterToday(now);
        TomorrowReminder = ReminderTimeHelper.GetTomorrow(now);
        NextWeekReminder = ReminderTimeHelper.GetNextWeek(now);
        LaterTodayReminderText = LaterTodayReminder is { } laterToday ? FormatSuggestion("LaterTodayReminderFormat", DateDisplayHelper.FormatTime(laterToday)) : string.Empty;
        TomorrowReminderText = FormatSuggestion("TomorrowReminderFormat", DateDisplayHelper.FormatReminderSuggestion(TomorrowReminder));
        NextWeekReminderText = FormatSuggestion("NextWeekReminderFormat", DateDisplayHelper.FormatReminderSuggestion(NextWeekReminder));
    }

    /// <summary>
    /// The time the custom reminder dialog starts from: the current reminder, or a suggestion based on the due date.
    /// </summary>
    public DateTimeOffset GetCustomReminderStart() => CurrentTask?.ReminderAt?.ToLocalTime() ?? ReminderTimeHelper.GetCustomDefault(CurrentTask?.DueDate, DateTimeOffset.Now);

    /// <summary>
    /// The rule the custom repeat dialog starts from: the current rule, or weekly on the weekday of the due date or today.
    /// </summary>
    public TaskRecurrence GetCustomRecurrenceStart() => CurrentTask?.Recurrence ?? CreateWeeklyRecurrence();

    /// <summary>
    /// Sets the reminder time, or removes the reminder with <see langword="null"/>.
    /// </summary>
    public Task SetReminderAsync(DateTimeOffset? reminderAt) => CurrentTask is null ? Task.CompletedTask : UpdateTaskAsync(new UpdateTaskRequest { ReminderAt = reminderAt?.ToUniversalTime() });

    /// <summary>
    /// Sets the repeat rule, or stops repeating with <see langword="null"/>. The server gives a task without a due date one.
    /// </summary>
    public Task SetRecurrenceAsync(TaskRecurrence? recurrence) => CurrentTask is null ? Task.CompletedTask : UpdateTaskAsync(new UpdateTaskRequest { Recurrence = recurrence });

    [RelayCommand]
    private Task SetLaterTodayReminderAsync() => LaterTodayReminder is { } laterToday ? SetReminderAsync(laterToday) : Task.CompletedTask;

    [RelayCommand]
    private Task SetTomorrowReminderAsync() => SetReminderAsync(TomorrowReminder);

    [RelayCommand]
    private Task SetNextWeekReminderAsync() => SetReminderAsync(NextWeekReminder);

    [RelayCommand]
    private Task ClearReminderAsync() => SetReminderAsync(null);

    [RelayCommand]
    private Task SetDailyRecurrenceAsync() => SetRecurrenceAsync(new TaskRecurrence(RecurrenceFrequency.Daily));

    [RelayCommand]
    private Task SetWeekdaysRecurrenceAsync() => SetRecurrenceAsync(new TaskRecurrence(RecurrenceFrequency.Weekly, 1, RecurrenceDisplayHelper.Weekdays));

    [RelayCommand]
    private Task SetWeeklyRecurrenceAsync() => SetRecurrenceAsync(CreateWeeklyRecurrence());

    [RelayCommand]
    private Task SetMonthlyRecurrenceAsync() => SetRecurrenceAsync(new TaskRecurrence(RecurrenceFrequency.Monthly));

    [RelayCommand]
    private Task SetYearlyRecurrenceAsync() => SetRecurrenceAsync(new TaskRecurrence(RecurrenceFrequency.Yearly));

    [RelayCommand]
    private Task ClearRecurrenceAsync() => SetRecurrenceAsync(null);

    private TaskRecurrence CreateWeeklyRecurrence() => new(RecurrenceFrequency.Weekly, 1, (DayOfWeek[])[(CurrentTask?.DueDate ?? DateDisplayHelper.Today).DayOfWeek]);

    private static string FormatSuggestion(string resourceName, string value) => string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get(resourceName), value);

    partial void OnCurrentTaskChanged(TaskItemModel? value)
    {
        DueDateValue = DateDisplayHelper.ToDateTimeOffset(value?.DueDate);
        CreatedText = value is null ? string.Empty : string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("CreatedOnFormat"), DateDisplayHelper.FormatLongDate(value.CreatedAt));
        NewStepTitle = string.Empty;
        IsErrorVisible = false;
        CancelNoteSearch();
        NoteSuggestions.Clear();
    }

    /// <summary>
    /// Updates the values the pane keeps apart from the task after the task changed elsewhere.
    /// </summary>
    public void RefreshFromCurrentTask() => DueDateValue = DateDisplayHelper.ToDateTimeOffset(CurrentTask?.DueDate);

    /// <summary>
    /// Shows the notes matching <paramref name="query"/> as suggestions shortly after typing stops. An empty query suggests the most recent notes.
    /// </summary>
    public async Task SearchNoteSuggestionsAsync(string query)
    {
        CancelNoteSearch();
        if (CurrentTask is not { } task) return;

        var cancellationTokenSource = new CancellationTokenSource();
        _noteSearchCancellation = cancellationTokenSource;
        try
        {
            await Task.Delay(s_noteSearchDelay, cancellationTokenSource.Token);
            var notes = _syncManager.Data.GetNotes(query, null, null);
            if (!ReferenceEquals(CurrentTask, task)) return;

            NoteSuggestions.Clear();
            foreach (var note in notes.Where(note => task.LinkedNotes.All(linkedNote => linkedNote.Id != note.Id)).Take(MaximumNoteSuggestionCount)) NoteSuggestions.Add(new NoteItemModel(note));
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested) { }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
    }

    public Task LinkNoteAsync(NoteItemModel note)
    {
        CancelNoteSearch();
        NoteSuggestions.Clear();
        RunNoteLinkOperation(task => _syncManager.Data.LinkNoteToTask(note.Id, task.Id));
        return Task.CompletedTask;
    }

    public Task UnlinkNoteAsync(LinkedNoteModel note)
    {
        RunNoteLinkOperation(task => _syncManager.Data.UnlinkNoteFromTask(note.Id, task.Id));
        return Task.CompletedTask;
    }

    public void OpenNote(LinkedNoteModel note) => _noteNavigationManager.OpenNote(note.Id);

    /// <summary>
    /// Creates a note linked to the task, titled like the task, and opens it on the notes page.
    /// </summary>
    [RelayCommand]
    private void CreateLinkedNote()
    {
        if (CurrentTask is not { } task) return;

        IsErrorVisible = false;
        try
        {
            var note = _syncManager.Data.CreateNote(new CreateNoteRequest(task.Title, Id: Guid.CreateVersion7(), LinkedTaskIds: (Guid[])[task.Id]));
            RefreshTask(task);
            _noteNavigationManager.OpenNote(note.Id);
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
    }

    private void RunNoteLinkOperation(Func<TaskItemModel, NoteResponse> operation)
    {
        if (CurrentTask is not { } task) return;

        IsErrorVisible = false;
        try
        {
            operation(task);
            RefreshTask(task);
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
    }

    /// <summary>
    /// Reads the task again, whose linked notes change when a note is linked or unlinked.
    /// </summary>
    private void RefreshTask(TaskItemModel task)
    {
        if (_syncManager.Data.GetTask(task.Id) is { } response && task.Apply(response))
        {
            TaskChanged?.Invoke(this, task);
        }
    }

    private void CancelNoteSearch()
    {
        _noteSearchCancellation?.Cancel();
        _noteSearchCancellation = null;
    }

    public Task CommitTitleAsync(string title)
    {
        var trimmedTitle = title.Trim();
        return CurrentTask is not { } task || trimmedTitle.Length == 0 || trimmedTitle == task.Title ? Task.CompletedTask : UpdateTaskAsync(new UpdateTaskRequest { Title = trimmedTitle });
    }

    public Task CommitBodyAsync(string body) => CurrentTask is not { } task || body == task.Body ? Task.CompletedTask : UpdateTaskAsync(new UpdateTaskRequest { Body = body });

    public Task SetDueDateAsync(DateTimeOffset? value)
    {
        var dueDate = DateDisplayHelper.ToDateOnly(value);
        return CurrentTask is not { } task || dueDate == task.DueDate ? Task.CompletedTask : UpdateTaskAsync(new UpdateTaskRequest { DueDate = dueDate });
    }

    public Task ToggleStepAsync(TaskStepModel step) => RunAsync(task => _syncManager.Data.UpdateStep(task.Id, step.Id, new UpdateTaskStepRequest { IsCompleted = !step.IsCompleted }));

    public Task CommitStepTitleAsync(TaskStepModel step, string title)
    {
        var trimmedTitle = title.Trim();
        return trimmedTitle.Length == 0 || trimmedTitle == step.Title ? Task.CompletedTask : RunAsync(task => _syncManager.Data.UpdateStep(task.Id, step.Id, new UpdateTaskStepRequest { Title = trimmedTitle }));
    }

    public Task DeleteStepAsync(TaskStepModel step) => RunAsync(task => _syncManager.Data.DeleteStep(task.Id, step.Id));

    [RelayCommand]
    private Task ToggleCompletionAsync() => CurrentTask is { } task ? UpdateTaskAsync(new UpdateTaskRequest { IsCompleted = !task.IsCompleted }) : Task.CompletedTask;

    [RelayCommand]
    private Task ToggleImportanceAsync() => CurrentTask is { } task ? UpdateTaskAsync(new UpdateTaskRequest { IsImportant = !task.IsImportant }) : Task.CompletedTask;

    [RelayCommand]
    private Task ToggleMyDayAsync() => CurrentTask is { } task ? UpdateTaskAsync(new UpdateTaskRequest { IsInMyDay = !task.IsInMyDay }) : Task.CompletedTask;

    [RelayCommand]
    private Task ClearDueDateAsync() => SetDueDateAsync(null);

    [RelayCommand]
    private void AddStep()
    {
        var title = NewStepTitle.Trim();
        if (title.Length == 0) return;

        NewStepTitle = string.Empty;
        if (!Run(task => _syncManager.Data.AddStep(task.Id, title))) NewStepTitle = title;
    }

    [RelayCommand]
    private async Task DeleteTaskAsync()
    {
        if (CurrentTask is not { } task) return;
        var message = string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("DeleteTaskDialogMessage"), task.Title);
        if (!await _dialogService.ConfirmAsync(LocalizedStrings.Get("DeleteTaskDialogTitle"), message, LocalizedStrings.Get("DeleteButtonText"))) return;

        IsErrorVisible = false;
        try
        {
            _syncManager.Data.DeleteTask(task.Id);
            TaskDeleted?.Invoke(this, task);
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private Task UpdateTaskAsync(UpdateTaskRequest request) => RunAsync(task => _syncManager.Data.UpdateTask(task.Id, request));

    private Task RunAsync(Func<TaskItemModel, TaskItemResponse> operation)
    {
        Run(operation);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Applies a change to the current task on this device and shows the result.
    /// </summary>
    /// <returns><see langword="false"/> when the change failed.</returns>
    private bool Run(Func<TaskItemModel, TaskItemResponse> operation)
    {
        if (CurrentTask is not { } task) return false;

        IsErrorVisible = false;
        try
        {
            var response = operation(task);
            if (task.Apply(response)) TaskChanged?.Invoke(this, task);
            if (ReferenceEquals(CurrentTask, task)) DueDateValue = DateDisplayHelper.ToDateTimeOffset(task.DueDate);
            return true;
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception))
        {
            ShowError(exception);
            return false;
        }
    }

    private void ShowError(Exception exception)
    {
        if (ApiErrorMessages.GetMessage(exception) is not { } message) return;
        ErrorMessage = message;
        IsErrorVisible = true;
    }
}
