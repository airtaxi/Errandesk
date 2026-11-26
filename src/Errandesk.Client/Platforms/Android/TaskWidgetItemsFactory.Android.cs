using Android.Content;
using Android.Text;
using Android.Text.Style;
using Android.Util;
using Android.Widget;
using Errandesk.Contracts;

namespace Errandesk.Client.Droid;

/// <summary>
/// Builds the rows of one task widget from the tasks of the view it shows: open tasks, then completed tasks when the widget shows them.
/// </summary>
public sealed class TaskWidgetItemsFactory(Context context, int appWidgetId) : Java.Lang.Object, RemoteViewsService.IRemoteViewsFactory
{
    private IReadOnlyList<TaskItemResponse> _tasks = [];
    private TaskWidgetSettings _settings = TaskWidgetSettings.Default;

    public int Count => _tasks.Count;

    public bool HasStableIds => true;

    public RemoteViews? LoadingView => null;

    public int ViewTypeCount => 2;

    public long GetItemId(int position) => position < _tasks.Count ? BitConverter.ToInt64(_tasks[position].Id.ToByteArray(), 8) : position;

    public RemoteViews? GetViewAt(int position)
    {
        if (position >= _tasks.Count) return null;

        var task = _tasks[position];
        var views = new RemoteViews(context.PackageName, task.IsCompleted ? Resource.Layout.task_widget_item_completed : Resource.Layout.task_widget_item);
        views.SetTextViewText(Resource.Id.task_widget_item_title, task.IsCompleted ? CreateStruckThroughText(task.Title) : new Java.Lang.String(task.Title));
        views.SetTextViewTextSize(Resource.Id.task_widget_item_title, (int)ComplexUnitType.Sp, _settings.TaskTextSize);
        views.SetImageViewResource(Resource.Id.task_widget_item_importance, task.IsImportant ? Resource.Drawable.task_widget_star_filled : Resource.Drawable.task_widget_star);
        views.SetContentDescription(Resource.Id.task_widget_item_importance, context.GetString(task.IsImportant ? Resource.String.TaskWidgetRemoveImportance : Resource.String.TaskWidgetMarkImportant));
        views.SetOnClickFillInIntent(Resource.Id.task_widget_item, TaskWidgetProvider.CreateOpenIntent(task));
        views.SetOnClickFillInIntent(Resource.Id.task_widget_item_complete, TaskWidgetProvider.CreateToggleCompletionIntent(task));
        views.SetOnClickFillInIntent(Resource.Id.task_widget_item_importance, TaskWidgetProvider.CreateToggleImportanceIntent(task));
        return views;
    }

    public void OnCreate() { }

    public void OnDataSetChanged()
    {
        _settings = new TaskWidgetPreferences(context).GetSettings(appWidgetId);
        _tasks = TaskWidgetData.ReadTasks(context, appWidgetId, _settings.ShowsCompletedTasks);
    }

    public void OnDestroy() => _tasks = [];

    private static SpannableString CreateStruckThroughText(string text)
    {
        var spannableText = new SpannableString(text);
        spannableText.SetSpan(new StrikethroughSpan(), 0, spannableText.Length(), SpanTypes.ExclusiveExclusive);
        return spannableText;
    }
}
