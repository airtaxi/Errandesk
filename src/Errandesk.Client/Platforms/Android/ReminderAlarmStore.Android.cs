using System.Text.Json;
using Android.Content;
using Errandesk.Client.Helpers;
using Errandesk.Client.Models;

namespace Errandesk.Client.Droid;

/// <summary>
/// Remembers the reminders that have alarms, so that they can be cancelled later and set again after the device restarts.
/// </summary>
public sealed class ReminderAlarmStore(Context context)
{
    private const string PreferencesName = "errandesk_reminder_alarms";
    private const string RemindersKey = "reminders";

    private readonly ISharedPreferences? _preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);

    public List<ReminderNotification> Read()
    {
        var json = _preferences?.GetString(RemindersKey, null);
        if (string.IsNullOrEmpty(json)) return [];

        try { return JsonSerializer.Deserialize(json, ClientJsonSerializerContext.Default.ListReminderNotification) ?? []; }
        catch (JsonException) { return []; }
    }

    public void Write(List<ReminderNotification> reminders)
    {
        if (_preferences?.Edit() is not { } editor) return;
        editor.PutString(RemindersKey, JsonSerializer.Serialize(reminders, ClientJsonSerializerContext.Default.ListReminderNotification));
        editor.Apply();
    }

    public void Remove(Guid taskId)
    {
        var reminders = Read();
        if (reminders.RemoveAll(reminder => reminder.TaskId == taskId) > 0) Write(reminders);
    }
}
