using Errandesk.Client.Helpers;
using Errandesk.Client.Models;
using Errandesk.Client.Services;
using Errandesk.Contracts;
using Microsoft.UI.Dispatching;

namespace Errandesk.Client.Managers;

/// <summary>
/// Registers this device with the server and keeps the reminders of the signed-in user scheduled on it.
/// When the server delivers reminders by push, the device only registers its push token; otherwise the platform scheduler
/// shows the reminders, reconciled with the tasks stored on this device at startup and after every change to them.
/// In shared lists only the reminders the signed-in user set are shown.
/// </summary>
/// <remarks>Call every member on the UI thread.</remarks>
public sealed class ReminderManager(IReminderScheduler reminderScheduler, AuthenticatedApiClient apiClient, ErrandeskApiClient anonymousApiClient, SessionManager sessionManager, ApplicationSettingsService settingsService, SyncManager syncManager, AssistantNavigationManager assistantNavigationManager)
{
    private readonly Dictionary<Guid, ReminderNotification> _reminders = [];
    private readonly SemaphoreSlim _scheduleGate = new(1, 1);
    private DispatcherQueue? _dispatcherQueue;
    private bool _isStarted;
    private bool _isLoaded;
    private bool _isPrepared;
    private bool _usesPush;
    private int _generation;

    /// <summary>
    /// Raised when a reminder is due on a platform that shows reminders inside the app.
    /// </summary>
    public event EventHandler<ReminderNotification>? InAppReminderRaised;

    /// <summary>
    /// Registers the device and schedules the reminders of the signed-in user. Does nothing when already started.
    /// </summary>
    public async Task StartAsync()
    {
        if (_isStarted) return;

        _isStarted = true;
        _isLoaded = false;
        _isPrepared = false;
        var generation = ++_generation;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        syncManager.DataChanged += OnSyncManagerDataChanged;
        reminderScheduler.InAppReminderRequested += OnReminderSchedulerInAppReminderRequested;
        reminderScheduler.PushTokenChanged += OnReminderSchedulerPushTokenChanged;

        ServerInfoResponse? serverInfo = null;
        try { serverInfo = await anonymousApiClient.GetServerInfoAsync(sessionManager.ServerAddress, CancellationToken.None); }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { }
        if (generation != _generation) return;
        if (serverInfo is not null) assistantNavigationManager.ReportAvailability(serverInfo.IsAiAvailable);

        var pushToken = await reminderScheduler.PrepareAsync(serverInfo, CancellationToken.None);
        if (generation != _generation) return;

        _usesPush = pushToken is not null;
        await RegisterDeviceAsync(pushToken);
        if (generation != _generation) return;

        _isPrepared = true;
        await LoadRemindersAsync();
    }

    /// <summary>
    /// Stops following changes. Scheduled reminders stay, so they still appear while the app is closed.
    /// </summary>
    public void Stop()
    {
        if (!_isStarted) return;

        _isStarted = false;
        _generation++;
        syncManager.DataChanged -= OnSyncManagerDataChanged;
        reminderScheduler.InAppReminderRequested -= OnReminderSchedulerInAppReminderRequested;
        reminderScheduler.PushTokenChanged -= OnReminderSchedulerPushTokenChanged;
    }

    /// <summary>
    /// Removes this device from the server and its reminders from the device. Call it before the session ends, while requests are still authorized.
    /// </summary>
    public async Task SignOutAsync()
    {
        await ClearAsync();
        try { await apiClient.DeleteDeviceAsync(settingsService.DeviceInstallationId, CancellationToken.None); }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { }
    }

    /// <summary>
    /// Removes the reminders from the device without contacting the server, for example after the session ended.
    /// </summary>
    public async Task ClearAsync()
    {
        Stop();
        _reminders.Clear();
        _isLoaded = false;
        await _scheduleGate.WaitAsync();
        try { await reminderScheduler.ClearAsync(); }
        finally { _scheduleGate.Release(); }
    }

    private async Task RegisterDeviceAsync(string? pushToken)
    {
        var request = new RegisterDeviceRequest(settingsService.DeviceInstallationId, reminderScheduler.Platform, DeviceInformationHelper.GetDeviceName(), pushToken, DeviceInformationHelper.GetAppVersion());
        try { await apiClient.RegisterDeviceAsync(request, CancellationToken.None); }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { }
    }

    /// <summary>
    /// Reads the open tasks with reminders stored on this device and schedules them. Before the first synchronization, the reminders scheduled before stay.
    /// </summary>
    private async Task LoadRemindersAsync()
    {
        if (_usesPush)
        {
            await ScheduleAsync();
            return;
        }

        if (!syncManager.IsOpen || !syncManager.HasSynchronized) return;

        _reminders.Clear();
        foreach (var task in syncManager.Data.GetOpenTasksWithReminders()) ApplyTask(task);
        _isLoaded = true;
        await ScheduleAsync();
    }

    private async void OnSyncManagerDataChanged(object? _, ChangesResponse changes)
    {
        if (!_isStarted || !_isPrepared || _usesPush) return;
        if (changes.ResetRequired || !_isLoaded)
        {
            await LoadRemindersAsync();
            return;
        }

        var hasChanged = false;
        foreach (var tombstone in changes.DeletedTasks) hasChanged |= _reminders.Remove(tombstone.Id);
        foreach (var task in changes.Tasks) hasChanged |= ApplyTask(task);
        if (hasChanged) await ScheduleAsync();
    }

    /// <summary>
    /// Records the reminder of <paramref name="task"/>, or forgets it when the task has none, is completed, or the reminder belongs to another user of a shared list.
    /// A reminder without a recorded user was set on this device and is not acknowledged by the server yet.
    /// </summary>
    /// <returns><see langword="true"/> when the recorded reminders changed.</returns>
    private bool ApplyTask(TaskItemResponse task)
    {
        var isSetByAnotherUser = task.ReminderSetByUserId is { } reminderUserId && reminderUserId != sessionManager.CurrentUser?.Id;
        if (task.IsCompleted || task.ReminderAt is not { } reminderAt || isSetByAnotherUser) return _reminders.Remove(task.Id);

        var reminder = new ReminderNotification(task.Id, task.ListId, task.Title, LocalizedStrings.Get("ReminderNotificationText"), reminderAt);
        if (_reminders.TryGetValue(task.Id, out var existingReminder) && existingReminder == reminder) return false;
        _reminders[task.Id] = reminder;
        return true;
    }

    private async Task ScheduleAsync()
    {
        var now = DateTimeOffset.UtcNow;
        List<ReminderNotification> reminders = _usesPush ? [] : [.. _reminders.Values.Where(reminder => reminder.ReminderAt > now).OrderBy(reminder => reminder.ReminderAt)];
        await _scheduleGate.WaitAsync();
        try { await reminderScheduler.ScheduleAsync(reminders, CancellationToken.None); }
        finally { _scheduleGate.Release(); }
    }

    private void OnReminderSchedulerInAppReminderRequested(object? _, ReminderNotification reminder) => InAppReminderRaised?.Invoke(this, reminder);

    /// <summary>
    /// Registers the device again with a push token that changed while the app runs.
    /// </summary>
    private void OnReminderSchedulerPushTokenChanged(object? _, string pushToken)
    {
        async void RegisterChangedToken()
        {
            if (_isStarted && _usesPush)
            {
                await RegisterDeviceAsync(pushToken);
            }
        }

        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess) RegisterChangedToken();
        else _dispatcherQueue.TryEnqueue(RegisterChangedToken);
    }
}
