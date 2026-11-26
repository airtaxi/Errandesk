using System.Text.Json;
using Android.Content;
using Errandesk.Client.Sync;
using Errandesk.Contracts;
using Microsoft.Data.Sqlite;

namespace Errandesk.Client.Droid;

/// <summary>
/// Reads and changes the signed-in user's tasks for the task widgets. Reads open the local store on their own connection.
/// While the app has the same store open, changes go through the app, which shows them and sends them to the server;
/// otherwise they are stored with their outbox entries on this device and sent when the app synchronizes next.
/// </summary>
public static class TaskWidgetData
{
    /// <summary>
    /// Whether a signed-in user's local store exists for the widgets to read.
    /// </summary>
    public static bool HasStore(Context context) => new TaskWidgetPreferences(context).DatabasePath is { } databasePath && File.Exists(databasePath);

    /// <summary>
    /// The view the widget shows. A list that no longer exists falls back to the default list.
    /// </summary>
    /// <returns><see langword="null"/> when the store cannot be read or holds no lists yet.</returns>
    public static TaskWidgetView? ReadView(Context context, int appWidgetId)
    {
        var viewKey = new TaskWidgetPreferences(context).GetView(appWidgetId);
        return Read(context, data => ResolveView(context, data, viewKey));
    }

    /// <summary>
    /// The tasks of the view the widget shows, in the order the app shows them: open tasks, then up to
    /// <see cref="TaskWidgetSettings.MaximumCompletedTaskCount"/> completed tasks when <paramref name="includesCompleted"/> is set.
    /// </summary>
    public static IReadOnlyList<TaskItemResponse> ReadTasks(Context context, int appWidgetId, bool includesCompleted)
    {
        var viewKey = new TaskWidgetPreferences(context).GetView(appWidgetId);
        return Read(context, data => ResolveView(context, data, viewKey) is { } view ? LimitCompleted(data.GetTasks(view.Key, includesCompleted)) : []) ?? [];
    }

    /// <summary>
    /// The views a widget can show: My Day, Important, Planned and the default list, then the other lists in their order.
    /// </summary>
    public static IReadOnlyList<TaskWidgetView> ReadViews(Context context) => Read(context, data => (IReadOnlyList<TaskWidgetView>)[.. GetSmartViews(context), .. data.GetLists().OrderBy(list => !list.IsDefault).Select(list => CreateListView(context, list))]) ?? [];

    /// <summary>
    /// Adds a task to a view as adding it in that view of the app does: My Day, Important and Planned add to the default list and mark the task for the view.
    /// </summary>
    public static bool TryAddTask(Context context, string viewKey, string title) => TryChange(context, data => data.CreateTask(new CreateTaskRequest(title, Guid.CreateVersion7(), ListId: Guid.TryParse(viewKey, out var listId) ? listId : null, IsImportant: viewKey == TaskViews.Important, DueDate: viewKey == TaskViews.Planned ? data.Today : null, IsInMyDay: viewKey == TaskViews.MyDay)));

    public static bool TryUpdateTask(Context context, Guid taskId, UpdateTaskRequest request) => TryChange(context, data => data.UpdateTask(taskId, request));

    private static bool TryChange(Context context, Action<LocalDataService> change)
    {
        if (new TaskWidgetPreferences(context).DatabasePath is not { } databasePath) return false;
        if (TaskWidgets.TryChangeInApp(databasePath, data => TryRun(() => change(data)))) return true;
        if (!File.Exists(databasePath)) return false;

        return TryRun(() =>
        {
            using var store = LocalStore.Open(databasePath);
            change(new LocalDataService(store, TimeProvider.System));
        });
    }

    private static T? Read<T>(Context context, Func<LocalDataService, T> read) where T : class?
    {
        if (new TaskWidgetPreferences(context).DatabasePath is not { } databasePath || !File.Exists(databasePath)) return null;

        try
        {
            using var store = LocalStore.Open(databasePath);
            return read(new LocalDataService(store, TimeProvider.System));
        }
        catch (Exception exception) when (IsStoreFailure(exception)) { return null; }
    }

    private static bool TryRun(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception exception) when (IsStoreFailure(exception) || exception is LocalItemNotFoundException) { return false; }
    }

    private static List<TaskItemResponse> LimitCompleted(IReadOnlyList<TaskItemResponse> tasks) =>
    [.. tasks.Where(task => !task.IsCompleted), .. tasks.Where(task => task.IsCompleted).Take(TaskWidgetSettings.MaximumCompletedTaskCount)];

    private static bool IsStoreFailure(Exception exception) => exception is SqliteException or JsonException or IOException or ArgumentException or InvalidOperationException;

    private static TaskWidgetView? ResolveView(Context context, LocalDataService data, string? viewKey)
    {
        if (GetSmartViews(context).FirstOrDefault(view => view.Key == viewKey) is { } smartView) return smartView;

        var list = (Guid.TryParse(viewKey, out var listId) ? data.GetList(listId) : null) ?? data.GetDefaultList();
        return list is null ? null : CreateListView(context, list);
    }

    private static TaskWidgetView[] GetSmartViews(Context context) =>
    [
        new(TaskViews.MyDay, context.GetString(Resource.String.TaskWidgetMyDay), Resource.Drawable.task_widget_my_day, false),
        new(TaskViews.Important, context.GetString(Resource.String.TaskWidgetImportant), Resource.Drawable.task_widget_star, false),
        new(TaskViews.Planned, context.GetString(Resource.String.TaskWidgetPlanned), Resource.Drawable.task_widget_planned, false),
    ];

    private static TaskWidgetView CreateListView(Context context, TaskListResponse list) => list.IsDefault ? new(list.Id.ToString("D"), context.GetString(Resource.String.TaskWidgetDefaultList), Resource.Drawable.task_widget_tasks, false) : new(list.Id.ToString("D"), list.Name, Resource.Drawable.task_widget_list, true);
}
