using Android.Content;

namespace Errandesk.Client.Droid;

/// <summary>
/// Keeps what the task widgets read while the app is closed: the local store of the signed-in user, and the view and settings of each widget.
/// </summary>
public sealed class TaskWidgetPreferences(Context context)
{
    private const string PreferencesName = "errandesk_task_widgets";
    private const string DatabasePathKey = "databasePath";
    private const string IsAssistantAvailableKey = "isAssistantAvailable";
    private const string ViewKeyPrefix = "view_";
    private const string OpacityKeyPrefix = "opacity_";
    private const string FontSizeKeyPrefix = "fontSize_";
    private const string ShowsCompletedTasksKeyPrefix = "showsCompletedTasks_";

    private readonly ISharedPreferences? _preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);

    /// <summary>
    /// The database file of the signed-in user, or <see langword="null"/> when nobody is signed in.
    /// </summary>
    public string? DatabasePath
    {
        get => _preferences?.GetString(DatabasePathKey, null);
        set => Edit(editor => value is null ? editor.Remove(DatabasePathKey) : editor.PutString(DatabasePathKey, value));
    }

    /// <summary>
    /// Whether the signed-in server runs the assistant, as the app last read it.
    /// </summary>
    public bool IsAssistantAvailable
    {
        get => _preferences?.GetBoolean(IsAssistantAvailableKey, false) == true;
        set => Edit(editor => editor.PutBoolean(IsAssistantAvailableKey, value));
    }

    /// <summary>
    /// The view key the widget shows, or <see langword="null"/> when the widget shows the default list.
    /// </summary>
    public string? GetView(int appWidgetId) => _preferences?.GetString(GetKey(ViewKeyPrefix, appWidgetId), null);

    public void SetView(int appWidgetId, string viewKey) => Edit(editor => editor.PutString(GetKey(ViewKeyPrefix, appWidgetId), viewKey));

    public TaskWidgetSettings GetSettings(int appWidgetId)
    {
        if (_preferences is null) return TaskWidgetSettings.Default;

        var defaultSettings = TaskWidgetSettings.Default;
        var fontSize = (TaskWidgetFontSize)_preferences.GetInt(GetKey(FontSizeKeyPrefix, appWidgetId), (int)defaultSettings.FontSize);
        return new TaskWidgetSettings(_preferences.GetInt(GetKey(OpacityKeyPrefix, appWidgetId), defaultSettings.Opacity), Enum.IsDefined(fontSize) ? fontSize : defaultSettings.FontSize, _preferences.GetBoolean(GetKey(ShowsCompletedTasksKeyPrefix, appWidgetId), defaultSettings.ShowsCompletedTasks));
    }

    public void SetSettings(int appWidgetId, TaskWidgetSettings settings) => Edit(editor =>
    {
        editor.PutInt(GetKey(OpacityKeyPrefix, appWidgetId), settings.Opacity);
        editor.PutInt(GetKey(FontSizeKeyPrefix, appWidgetId), (int)settings.FontSize);
        return editor.PutBoolean(GetKey(ShowsCompletedTasksKeyPrefix, appWidgetId), settings.ShowsCompletedTasks);
    });

    /// <summary>
    /// Forgets the view and settings of a removed widget.
    /// </summary>
    public void Remove(int appWidgetId) => Edit(editor =>
    {
        foreach (var prefix in (string[])[ViewKeyPrefix, OpacityKeyPrefix, FontSizeKeyPrefix, ShowsCompletedTasksKeyPrefix]) editor.Remove(GetKey(prefix, appWidgetId));
        return editor;
    });

    private static string GetKey(string prefix, int appWidgetId) => $"{prefix}{appWidgetId}";

    private void Edit(Func<ISharedPreferencesEditor, ISharedPreferencesEditor?> change)
    {
        if (_preferences?.Edit() is not { } editor) return;
        change(editor);
        editor.Apply();
    }
}
