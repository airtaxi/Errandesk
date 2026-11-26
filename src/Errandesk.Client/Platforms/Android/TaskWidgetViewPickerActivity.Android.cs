using Android.App;
using Android.Appwidget;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;

namespace Errandesk.Client.Droid;

/// <summary>
/// A sheet at the bottom of the screen that chooses which view a task widget shows.
/// </summary>
[Activity(Theme = "@style/Theme.Errandesk.TaskWidgetSheet", Exported = false, ExcludeFromRecents = true, NoHistory = true, TaskAffinity = "", LaunchMode = LaunchMode.SingleTask)]
public sealed class TaskWidgetViewPickerActivity : AppCompatActivity
{
    private int _appWidgetId = AppWidgetManager.InvalidAppwidgetId;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _appWidgetId = Intent?.GetIntExtra(AppWidgetManager.ExtraAppwidgetId, AppWidgetManager.InvalidAppwidgetId) ?? AppWidgetManager.InvalidAppwidgetId;
        var views = TaskWidgetData.ReadViews(this);
        if (_appWidgetId == AppWidgetManager.InvalidAppwidgetId || views.Count == 0)
        {
            Finish();
            return;
        }

        SetContentView(Resource.Layout.task_widget_picker);
        FindViewById(Resource.Id.task_widget_sheet_scrim)!.Click += OnScrimClicked;

        var selectedKey = TaskWidgetData.ReadView(this, _appWidgetId)?.Key;
        var itemsLayout = FindViewById<LinearLayout>(Resource.Id.task_widget_picker_items)!;
        var isFirstCustomList = true;
        foreach (var view in views)
        {
            if (view.IsCustomList && isFirstCustomList)
            {
                isFirstCustomList = false;
                itemsLayout.AddView(LayoutInflater.Inflate(Resource.Layout.task_widget_picker_divider, itemsLayout, false));
            }

            itemsLayout.AddView(CreateRow(itemsLayout, view, view.Key == selectedKey));
        }
    }

    private View CreateRow(ViewGroup parent, TaskWidgetView view, bool isSelected)
    {
        var row = LayoutInflater.Inflate(Resource.Layout.task_widget_picker_item, parent, false)!;
        row.FindViewById<ImageView>(Resource.Id.task_widget_picker_item_icon)!.SetImageResource(view.IconResourceId);
        row.FindViewById<TextView>(Resource.Id.task_widget_picker_item_title)!.Text = view.Title;
        row.Activated = isSelected;
        row.Click += (_, _) => Choose(view.Key);
        return row;
    }

    private void Choose(string viewKey)
    {
        new TaskWidgetPreferences(this).SetView(_appWidgetId, viewKey);
        if (AppWidgetManager.GetInstance(this) is { } appWidgetManager) TaskWidgets.Update(this, appWidgetManager, [_appWidgetId]);
        Finish();
    }

    private void OnScrimClicked(object? sender, EventArgs e) => Finish();
}
