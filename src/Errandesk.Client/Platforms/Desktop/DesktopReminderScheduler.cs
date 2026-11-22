using System.ComponentModel;
using System.Diagnostics;
using Errandesk.Client.Models;
using Errandesk.Contracts;
using Microsoft.UI.Dispatching;

namespace Errandesk.Client.Services;

/// <summary>
/// Shows reminders while the app runs: a timer fires at the next reminder time, and each due reminder is shown inside the app.
/// It is also passed to the operating system where that needs no extra dependency: <c>osascript</c> on macOS and,
/// when it is installed, <c>notify-send</c> on Linux. Reminders that fall due while the app is closed are not shown.
/// </summary>
/// <remarks>Call every member on the UI thread.</remarks>
public sealed class DesktopReminderScheduler : IReminderScheduler
{
    private static readonly TimeSpan s_minimumTimerInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_maximumTimerInterval = TimeSpan.FromMinutes(30);

    private readonly List<ReminderNotification> _pendingReminders = [];
    private DispatcherQueueTimer? _timer;

    public string Platform => DevicePlatforms.Desktop;

    public event EventHandler<ReminderNotification>? InAppReminderRequested;

    public event EventHandler<string>? PushTokenChanged { add { } remove { } }

    public Task<string?> PrepareAsync(ServerInfoResponse? serverInfo, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    public Task ScheduleAsync(IReadOnlyList<ReminderNotification> reminders, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        _pendingReminders.Clear();
        _pendingReminders.AddRange(reminders.Where(reminder => reminder.ReminderAt > now).OrderBy(reminder => reminder.ReminderAt));
        ArmTimer();
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        _pendingReminders.Clear();
        _timer?.Stop();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts the timer for the next reminder. Long waits are split so that a changed clock or a wake from sleep is noticed.
    /// </summary>
    private void ArmTimer()
    {
        if (_timer is null)
        {
            _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _timer.IsRepeating = false;
            _timer.Tick += OnTimerTick;
        }

        _timer.Stop();
        if (_pendingReminders.Count == 0) return;

        var delay = _pendingReminders[0].ReminderAt - DateTimeOffset.UtcNow;
        _timer.Interval = delay < s_minimumTimerInterval ? s_minimumTimerInterval : delay > s_maximumTimerInterval ? s_maximumTimerInterval : delay;
        _timer.Start();
    }

    private void OnTimerTick(DispatcherQueueTimer sender, object args)
    {
        var now = DateTimeOffset.UtcNow;
        var dueReminders = _pendingReminders.TakeWhile(reminder => reminder.ReminderAt <= now).ToList();
        _pendingReminders.RemoveRange(0, dueReminders.Count);
        foreach (var reminder in dueReminders)
        {
            ShowSystemNotification(reminder);
            InAppReminderRequested?.Invoke(this, reminder);
        }

        ArmTimer();
    }

    private static void ShowSystemNotification(ReminderNotification reminder)
    {
        if (OperatingSystem.IsMacOS()) StartProcess("osascript", ["-e", "on run argv", "-e", "display notification (item 2 of argv) with title (item 1 of argv)", "-e", "end run", reminder.Title, reminder.Text]);
        else if (OperatingSystem.IsLinux() && FindExecutable("notify-send") is { } notifySendPath) StartProcess(notifySendPath, ["--app-name=Errandesk", "--", reminder.Title, reminder.Text]);
    }

    private static string? FindExecutable(string fileName)
    {
        var searchPath = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(searchPath)) return null;
        return searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(directory => Path.Combine(directory, fileName)).FirstOrDefault(File.Exists);
    }

    private static void StartProcess(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        try { using var process = Process.Start(startInfo); }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException) { }
    }
}
