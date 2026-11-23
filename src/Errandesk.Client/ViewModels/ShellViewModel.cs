using System.Collections.ObjectModel;
using Errandesk.Client.Helpers;
using Errandesk.Client.Managers;
using Errandesk.Client.Models;
using Errandesk.Client.Services;
using Errandesk.Client.Sync;
using Errandesk.Contracts;

namespace Errandesk.Client.ViewModels;

/// <summary>
/// The navigation pane: smart lists, the user's lists and creating a list.
/// </summary>
public sealed partial class ShellViewModel(TaskListsManager taskListsManager, SessionManager sessionManager, DialogService dialogService, SyncManager syncManager) : ObservableObject
{
    private readonly TaskListsManager _taskListsManager = taskListsManager;
    private readonly SessionManager _sessionManager = sessionManager;
    private readonly DialogService _dialogService = dialogService;
    private readonly SyncManager _syncManager = syncManager;

    public ObservableCollection<TaskListModel> Lists => _taskListsManager.Lists;

    public TaskListModel? DefaultList => _taskListsManager.DefaultList;

    [ObservableProperty]
    public partial string UserDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The number of open tasks shown next to each smart list and list.
    /// </summary>
    [ObservableProperty]
    public partial OpenTaskCounts OpenTaskCounts { get; set; } = OpenTaskCounts.Empty;

    [ObservableProperty]
    public partial bool IsErrorVisible { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// The reminder shown inside the app on platforms without system notifications for it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InAppReminderTitle), nameof(InAppReminderText))]
    public partial ReminderNotification? InAppReminder { get; set; }

    [ObservableProperty]
    public partial bool IsInAppReminderVisible { get; set; }

    public string InAppReminderTitle => InAppReminder?.Title ?? string.Empty;

    public string InAppReminderText => InAppReminder?.Text ?? string.Empty;

    public void ShowInAppReminder(ReminderNotification reminder)
    {
        InAppReminder = reminder;
        IsInAppReminderVisible = true;
    }

    public void HideInAppReminder()
    {
        IsInAppReminderVisible = false;
        InAppReminder = null;
    }

    /// <summary>
    /// The list that contains the task now on this device; the list of the request when the task is not stored.
    /// </summary>
    public Guid? ResolveTaskListId(TaskNavigationRequest request) => _syncManager.Data.GetTask(request.TaskId)?.ListId ?? request.ListId;

    /// <summary>
    /// Shows the lists stored on this device. Before the first synchronization the data is read from the server first.
    /// </summary>
    /// <returns><see langword="false"/> when there is no stored data yet and the server could not be reached.</returns>
    public async Task<bool> LoadAsync()
    {
        UserDisplayName = _sessionManager.CurrentUser?.DisplayName ?? string.Empty;
        _sessionManager.CurrentUserChanged -= OnSessionManagerCurrentUserChanged;
        _sessionManager.CurrentUserChanged += OnSessionManagerCurrentUserChanged;
        _syncManager.DataChanged -= OnSyncManagerDataChanged;
        _syncManager.DataChanged += OnSyncManagerDataChanged;
        IsErrorVisible = false;
        _taskListsManager.Load();
        RefreshOpenTaskCounts();
        if (_syncManager.HasSynchronized) return true;

        var outcome = await _syncManager.SyncNowAsync();
        if (outcome == SyncOutcome.Completed) return true;
        if (outcome is not null)
        {
            ErrorMessage = LocalizedStrings.Get(outcome == SyncOutcome.Offline ? "ServerUnreachableError" : "ServerUnavailableError");
            IsErrorVisible = true;
        }

        return false;
    }

    public void Unload()
    {
        _sessionManager.CurrentUserChanged -= OnSessionManagerCurrentUserChanged;
        _syncManager.DataChanged -= OnSyncManagerDataChanged;
    }

    /// <summary>
    /// Counts the open tasks again, such as after the date changed and My Day holds other tasks.
    /// </summary>
    public void RefreshOpenTaskCounts() => OpenTaskCounts = _syncManager.Data.GetOpenTaskCounts();

    /// <summary>
    /// Asks for a name and creates a list.
    /// </summary>
    /// <returns>The new list, or <see langword="null"/> when the user canceled or creating failed.</returns>
    public async Task<TaskListModel?> CreateListAsync()
    {
        var name = await _dialogService.RequestTextAsync(LocalizedStrings.Get("NewListDialogTitle"), null, LocalizedStrings.Get("ListNamePlaceholder"), LocalizedStrings.Get("CreateButtonText"));
        if (name is null) return null;

        IsErrorVisible = false;
        try { return _taskListsManager.Create(name); }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception))
        {
            ShowError(exception);
            return null;
        }
    }

    [RelayCommand]
    private async Task RetryAsync() => await LoadAsync();

    private void OnSessionManagerCurrentUserChanged(object? _, EventArgs __) => UserDisplayName = _sessionManager.CurrentUser?.DisplayName ?? string.Empty;

    /// <remarks>
    /// <see cref="TaskListsManager"/> follows the same event from an earlier subscription, so new lists are already shown when the counts change.
    /// </remarks>
    private void OnSyncManagerDataChanged(object? _, ChangesResponse __) => RefreshOpenTaskCounts();

    private void ShowError(Exception exception)
    {
        if (ApiErrorMessages.GetMessage(exception) is not { } message) return;
        ErrorMessage = message;
        IsErrorVisible = true;
    }
}
