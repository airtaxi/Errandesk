using Android.Content;

namespace Errandesk.Client.Droid;

/// <summary>
/// Posts the reminder notification when its alarm fires.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class ReminderAlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null || !ReminderAlarms.TryReadReminder(intent, out var reminder)) return;

        ReminderNotifications.Post(context, reminder);
        new ReminderAlarmStore(context).Remove(reminder.TaskId);
    }
}
