using Android.App;
using Android.Appwidget;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using AndroidX.AppCompat.App;

namespace Errandesk.Client.Droid;

/// <summary>
/// A sheet at the bottom of the screen that adds a task to the view a task widget shows.
/// </summary>
[Activity(Theme = "@style/Theme.Errandesk.TaskWidgetSheet", Exported = false, ExcludeFromRecents = true, NoHistory = true, TaskAffinity = "", LaunchMode = LaunchMode.SingleTask, WindowSoftInputMode = SoftInput.StateAlwaysVisible | SoftInput.AdjustResize)]
public sealed class TaskWidgetAddActivity : AppCompatActivity
{
    private int _appWidgetId = AppWidgetManager.InvalidAppwidgetId;
    private TaskWidgetView? _view;
    private EditText? _titleEditText;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _appWidgetId = Intent?.GetIntExtra(AppWidgetManager.ExtraAppwidgetId, AppWidgetManager.InvalidAppwidgetId) ?? AppWidgetManager.InvalidAppwidgetId;
        _view = _appWidgetId == AppWidgetManager.InvalidAppwidgetId ? null : TaskWidgetData.ReadView(this, _appWidgetId);
        if (_view is null)
        {
            Finish();
            return;
        }

        SetContentView(Resource.Layout.task_widget_add);
        FindViewById(Resource.Id.task_widget_sheet_scrim)!.Click += OnScrimClicked;
        FindViewById<TextView>(Resource.Id.task_widget_add_view)!.Text = _view.Title;
        FindViewById(Resource.Id.task_widget_add_button)!.Click += OnAddButtonClicked;
        _titleEditText = FindViewById<EditText>(Resource.Id.task_widget_add_title)!;
        _titleEditText.EditorAction += OnTitleEditTextEditorAction;
        _titleEditText.RequestFocus();
    }

    private void Add()
    {
        var title = _titleEditText?.Text?.Trim();
        if (_view is null || string.IsNullOrEmpty(title)) return;

        TaskWidgetData.TryAddTask(this, _view.Key, title);
        TaskWidgets.RefreshAll(this);
        Finish();
    }

    private void OnTitleEditTextEditorAction(object? sender, TextView.EditorActionEventArgs e)
    {
        e.Handled = e.ActionId == ImeAction.Done;
        if (e.Handled) Add();
    }

    private void OnAddButtonClicked(object? sender, EventArgs e) => Add();

    private void OnScrimClicked(object? sender, EventArgs e) => Finish();
}
