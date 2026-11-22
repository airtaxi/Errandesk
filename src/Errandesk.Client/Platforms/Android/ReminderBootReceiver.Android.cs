using Android.Content;
using Errandesk.Client.Models;

namespace Errandesk.Client.Droid;

/// <summary>
/// Sets the reminder alarms again after the device restarts or the app is updated, because the system drops alarms on restart.
/// Reminders that fell due while the device was off are shown right away when they are less than a day late.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[global::Android.App.IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced])]
public sealed class ReminderBootReceiver : BroadcastReceiver
{
    private static readonly TimeSpan s_maximumLateness = TimeSpan.FromDays(1);

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action is not (Intent.ActionBootCompleted or Intent.ActionMyPackageReplaced)) return;

        var store = new ReminderAlarmStore(context);
        var now = DateTimeOffset.UtcNow;
        List<ReminderNotification> remainingReminders = [];
        foreach (var reminder in store.Read())
        {
            if (reminder.ReminderAt > now)
            {
                ReminderAlarms.Set(context, reminder);
                remainingReminders.Add(reminder);
            }
            else if (now - reminder.ReminderAt <= s_maximumLateness) ReminderNotifications.Post(context, reminder);
        }

        store.Write(remainingReminders);
    }
}
