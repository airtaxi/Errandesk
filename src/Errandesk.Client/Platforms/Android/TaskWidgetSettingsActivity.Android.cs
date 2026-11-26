using Android.App;
using Android.Appwidget;
using Android.Content.PM;
using Android.OS;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.AppCompat.Widget;

namespace Errandesk.Client.Droid;

/// <summary>
/// A sheet at the bottom of the screen that changes the opacity, the text size and the completed tasks of one task widget. Changes apply right away.
/// </summary>
[Activity(Theme = "@style/Theme.Errandesk.TaskWidgetSheet", Exported = false, ExcludeFromRecents = true, NoHistory = true, TaskAffinity = "", LaunchMode = LaunchMode.SingleTask)]
public sealed class TaskWidgetSettingsActivity : AppCompatActivity
{
    private int _appWidgetId = AppWidgetManager.InvalidAppwidgetId;
    private TaskWidgetPreferences? _preferences;
    private TaskWidgetSettings _settings = TaskWidgetSettings.Default;
    private TextView? _opacityValueTextView;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _appWidgetId = Intent?.GetIntExtra(AppWidgetManager.ExtraAppwidgetId, AppWidgetManager.InvalidAppwidgetId) ?? AppWidgetManager.InvalidAppwidgetId;
        if (_appWidgetId == AppWidgetManager.InvalidAppwidgetId)
        {
            Finish();
            return;
        }

        _preferences = new TaskWidgetPreferences(this);
        _settings = _preferences.GetSettings(_appWidgetId);
        SetContentView(Resource.Layout.task_widget_settings);
        FindViewById(Resource.Id.task_widget_sheet_scrim)!.Click += OnScrimClicked;

        _opacityValueTextView = FindViewById<TextView>(Resource.Id.task_widget_settings_opacity_value)!;
        var opacitySeekBar = FindViewById<SeekBar>(Resource.Id.task_widget_settings_opacity)!;
        opacitySeekBar.Max = TaskWidgetSettings.MaximumOpacity;
        opacitySeekBar.Progress = _settings.Opacity;
        ShowOpacity();
        opacitySeekBar.ProgressChanged += OnOpacitySeekBarProgressChanged;

        var fontSizeRadioGroup = FindViewById<RadioGroup>(Resource.Id.task_widget_settings_font_size)!;
        fontSizeRadioGroup.Check(GetFontSizeButtonId(_settings.FontSize));
        fontSizeRadioGroup.CheckedChange += OnFontSizeRadioGroupCheckedChange;

        var completedTasksSwitch = FindViewById<SwitchCompat>(Resource.Id.task_widget_settings_completed_tasks)!;
        completedTasksSwitch.Checked = _settings.ShowsCompletedTasks;
        completedTasksSwitch.CheckedChange += OnCompletedTasksSwitchCheckedChange;
    }

    private static int GetFontSizeButtonId(TaskWidgetFontSize fontSize) => fontSize switch
    {
        TaskWidgetFontSize.Small => Resource.Id.task_widget_settings_font_size_small,
        TaskWidgetFontSize.Large => Resource.Id.task_widget_settings_font_size_large,
        _ => Resource.Id.task_widget_settings_font_size_normal,
    };

    private static TaskWidgetFontSize GetFontSize(int buttonId)
    {
        if (buttonId == Resource.Id.task_widget_settings_font_size_small) return TaskWidgetFontSize.Small;
        if (buttonId == Resource.Id.task_widget_settings_font_size_large) return TaskWidgetFontSize.Large;
        return TaskWidgetFontSize.Normal;
    }

    private void ShowOpacity() => _opacityValueTextView!.Text = $"{_settings.Opacity}%";

    /// <summary>
    /// Saves <paramref name="settings"/> and redraws the widget; the rows are built again only when <paramref name="reloadsTasks"/> is set.
    /// </summary>
    private void Apply(TaskWidgetSettings settings, bool reloadsTasks)
    {
        _settings = settings;
        _preferences?.SetSettings(_appWidgetId, settings);
        if (AppWidgetManager.GetInstance(this) is { } appWidgetManager) TaskWidgets.Update(this, appWidgetManager, [_appWidgetId], reloadsTasks);
    }

    private void OnOpacitySeekBarProgressChanged(object? sender, SeekBar.ProgressChangedEventArgs e)
    {
        if (!e.FromUser) return;
        Apply(_settings with { Opacity = e.Progress }, reloadsTasks: false);
        ShowOpacity();
    }

    private void OnFontSizeRadioGroupCheckedChange(object? sender, RadioGroup.CheckedChangeEventArgs e) => Apply(_settings with { FontSize = GetFontSize(e.CheckedId) }, reloadsTasks: true);

    private void OnCompletedTasksSwitchCheckedChange(object? sender, Android.Widget.CompoundButton.CheckedChangeEventArgs e) => Apply(_settings with { ShowsCompletedTasks = e.IsChecked }, reloadsTasks: true);

    private void OnScrimClicked(object? sender, EventArgs e) => Finish();
}
