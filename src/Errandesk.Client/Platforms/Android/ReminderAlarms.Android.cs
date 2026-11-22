using Android.App;
using Android.Content;
using Errandesk.Client.Models;

namespace Errandesk.Client.Droid;

/// <summary>
/// Sets and cancels the alarms that post reminder notifications. Each task has one alarm, identified by the task ID in the intent data.
/// </summary>
public static class ReminderAlarms
{
    private const string AlarmAction = "errandesk.intent.action.REMINDER";
    private const string TitleExtra = "title";
    private const string TextExtra = "text";
    private const string ReminderAtExtra = "reminderAt";

    /// <summary>
    /// Sets the alarm of <paramref name="reminder"/>, replacing an earlier alarm of the same task.
    /// Uses an exact alarm when the app may set one, and an alarm that the system may defer slightly otherwise.
    /// </summary>
    public static void Set(Context context, ReminderNotification reminder)
    {
        if (context.GetSystemService(Context.AlarmService) is not AlarmManager alarmManager) return;

        var intent = CreateIntent(context, reminder.TaskId);
        intent.PutExtra(ReminderNotifications.TaskIdExtra, reminder.TaskId.ToString());
        intent.PutExtra(ReminderNotifications.ListIdExtra, reminder.ListId.ToString());
        intent.PutExtra(TitleExtra, reminder.Title);
        intent.PutExtra(TextExtra, reminder.Text);
        intent.PutExtra(ReminderAtExtra, reminder.ReminderAt.ToUnixTimeMilliseconds());
        var pendingIntent = PendingIntent.GetBroadcast(context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        if (pendingIntent is null) return;

        var triggerAtMilliseconds = reminder.ReminderAt.ToUnixTimeMilliseconds();
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(31) || alarmManager.CanScheduleExactAlarms()) alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMilliseconds, pendingIntent);
            else alarmManager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMilliseconds, pendingIntent);
        }
        catch (Java.Lang.SecurityException) { alarmManager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMilliseconds, pendingIntent); }
    }

    public static void Cancel(Context context, Guid taskId)
    {
        var pendingIntent = PendingIntent.GetBroadcast(context, 0, CreateIntent(context, taskId), PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);
        if (pendingIntent is null) return;

        if (context.GetSystemService(Context.AlarmService) is AlarmManager alarmManager) alarmManager.Cancel(pendingIntent);
        pendingIntent.Cancel();
    }

    /// <summary>
    /// Reads the reminder an alarm intent carries.
    /// </summary>
    public static bool TryReadReminder(Intent intent, out ReminderNotification reminder)
    {
        reminder = null!;
        if (intent.Action != AlarmAction || !ReminderNotifications.TryReadTarget(intent, out var target)) return false;

        var reminderAt = DateTimeOffset.FromUnixTimeMilliseconds(intent.GetLongExtra(ReminderAtExtra, 0));
        reminder = new ReminderNotification(target.TaskId, target.ListId ?? Guid.Empty, intent.GetStringExtra(TitleExtra) ?? string.Empty, intent.GetStringExtra(TextExtra) ?? string.Empty, reminderAt);
        return true;
    }

    private static Intent CreateIntent(Context context, Guid taskId)
    {
        var intent = new Intent(context, typeof(ReminderAlarmReceiver));
        intent.SetAction(AlarmAction);
        intent.SetData(global::Android.Net.Uri.Parse($"errandesk-reminder://task/{taskId:N}"));
        return intent;
    }
}
