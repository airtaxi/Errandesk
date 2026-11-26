using Android.Appwidget;
using Android.Content;
using Errandesk.Client.Models;
using Errandesk.Contracts;

namespace Errandesk.Client.Droid;

/// <summary>
/// The home screen widget that lists the open tasks of one view. Rows complete a task, mark it important or open it in the app.
/// </summary>
[BroadcastReceiver(Label = "@string/TaskWidgetLabel", Exported = true)]
[global::Android.App.IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[global::Android.App.MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/task_widget_info")]
public sealed class TaskWidgetProvider : AppWidgetProvider
{
    /// <summary>The action of the refresh button.</summary>
    public const string RefreshAction = "errandesk.taskwidget.REFRESH";

    /// <summary>The action of every row button; <see cref="OperationExtra"/> tells which one.</summary>
    public const string ItemAction = "errandesk.taskwidget.ITEM";

    private const string OperationExtra = "operation";
    private const string IsImportantExtra = "isImportant";
    private const string IsCompletedExtra = "isCompleted";
    private const string OpenOperation = "open";
    private const string SetCompletionOperation = "setCompletion";
    private const string SetImportanceOperation = "setImportance";

    /// <summary>
    /// The fill-in intent of the row of <paramref name="task"/> that opens it in the app.
    /// </summary>
    public static Intent CreateOpenIntent(TaskItemResponse task) => CreateItemIntent(OpenOperation, task);

    /// <summary>
    /// The fill-in intent of the row button that completes <paramref name="task"/>, or opens it again when it is completed.
    /// </summary>
    public static Intent CreateToggleCompletionIntent(TaskItemResponse task) => CreateItemIntent(SetCompletionOperation, task).PutExtra(IsCompletedExtra, !task.IsCompleted)!;

    /// <summary>
    /// The fill-in intent of the row button that marks <paramref name="task"/> important, or removes the mark.
    /// </summary>
    public static Intent CreateToggleImportanceIntent(TaskItemResponse task) => CreateItemIntent(SetImportanceOperation, task).PutExtra(IsImportantExtra, !task.IsImportant)!;

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;

        switch (intent.Action)
        {
            case RefreshAction:
                TaskWidgets.RequestSync();
                TaskWidgets.RefreshAll(context);
                break;
            case ItemAction:
                HandleItem(context, intent);
                break;
            default:
                base.OnReceive(context, intent);
                break;
        }
    }

    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context is null || appWidgetManager is null || appWidgetIds is null || appWidgetIds.Length == 0) return;
        TaskWidgets.Update(context, appWidgetManager, appWidgetIds);
    }

    public override void OnDeleted(Context? context, int[]? appWidgetIds)
    {
        if (context is null || appWidgetIds is null) return;

        var preferences = new TaskWidgetPreferences(context);
        foreach (var appWidgetId in appWidgetIds) preferences.Remove(appWidgetId);
    }

    /// <summary>
    /// Fits the header to the new size: a narrow widget leaves out the app icon and the refresh button.
    /// </summary>
    public override void OnAppWidgetOptionsChanged(Context? context, AppWidgetManager? appWidgetManager, int appWidgetId, global::Android.OS.Bundle? newOptions)
    {
        if (context is null || appWidgetManager is null) return;
        TaskWidgets.Update(context, appWidgetManager, [appWidgetId], reloadsTasks: false);
    }

    private static Intent CreateItemIntent(string operation, TaskItemResponse task)
    {
        var intent = new Intent();
        intent.PutExtra(OperationExtra, operation);
        intent.PutExtra(ReminderNotifications.TaskIdExtra, task.Id.ToString());
        intent.PutExtra(ReminderNotifications.ListIdExtra, task.ListId.ToString());
        return intent;
    }

    private static void HandleItem(Context context, Intent intent)
    {
        if (!ReminderNotifications.TryReadTarget(intent, out var request)) return;

        switch (intent.GetStringExtra(OperationExtra))
        {
            case OpenOperation:
                TaskWidgets.OpenTask(context, request);
                break;
            case SetCompletionOperation:
                TaskWidgetData.TryUpdateTask(context, request.TaskId, new UpdateTaskRequest { IsCompleted = intent.GetBooleanExtra(IsCompletedExtra, true) });
                TaskWidgets.RefreshAll(context);
                break;
            case SetImportanceOperation:
                TaskWidgetData.TryUpdateTask(context, request.TaskId, new UpdateTaskRequest { IsImportant = intent.GetBooleanExtra(IsImportantExtra, true) });
                TaskWidgets.RefreshAll(context);
                break;
        }
    }
}
