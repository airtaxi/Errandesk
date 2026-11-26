using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Errandesk.Client.Managers;
using Errandesk.Client.Models;
using Errandesk.Client.Sync;
using Errandesk.Contracts;
using Microsoft.UI.Dispatching;

namespace Errandesk.Client.Droid;

/// <summary>
/// Draws the task widgets and keeps them current. The app reports which local store belongs to the signed-in user and every change to it;
/// the widgets read that store themselves, so they keep working while the app is closed.
/// </summary>
public static class TaskWidgets
{
    private const long RefreshDelayMilliseconds = 300;

    /// <summary>Below this width the header has no room for the app icon and the refresh button.</summary>
    private const int NarrowWidthDp = 220;

    private static readonly Handler s_handler = new(Looper.MainLooper!);
    private static SyncManager? s_syncManager;
    private static AssistantNavigationManager? s_assistantNavigationManager;
    private static DispatcherQueue? s_dispatcherQueue;
    private static bool s_wasStoreOpen;
    private static bool s_isRefreshPending;

    private static Context ApplicationContext => global::Android.App.Application.Context;

    /// <summary>
    /// Follows the app's local store: the widgets show the signed-in user's tasks and redraw after every change. Call once on the UI thread.
    /// </summary>
    public static void Attach(SyncManager syncManager, AssistantNavigationManager assistantNavigationManager)
    {
        if (s_syncManager is not null) return;

        s_syncManager = syncManager;
        s_assistantNavigationManager = assistantNavigationManager;
        s_dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        syncManager.StatusChanged += OnSyncManagerStatusChanged;
        syncManager.DataChanged += OnSyncManagerDataChanged;
        assistantNavigationManager.AvailabilityChanged += OnAssistantNavigationManagerAvailabilityChanged;
        RecordStore();
    }

    /// <summary>
    /// Runs <paramref name="change"/> on the app's local data when the app has the store at <paramref name="databasePath"/> open.
    /// </summary>
    /// <returns><see langword="false"/> when the app does not have that store open.</returns>
    public static bool TryChangeInApp(string databasePath, Action<LocalDataService> change)
    {
        if (s_syncManager is not { IsOpen: true } syncManager || syncManager.DatabasePath != databasePath) return false;

        void Change()
        {
            if (syncManager.IsOpen && syncManager.DatabasePath == databasePath)
            {
                change(syncManager.Data);
            }
        }

        return RunOnAppThread(Change);
    }

    /// <summary>
    /// Synchronizes with the server now when the app has a local store open.
    /// </summary>
    public static void RequestSync()
    {
        if (s_syncManager is not { } syncManager) return;

        void Sync()
        {
            if (syncManager.IsOpen)
            {
                _ = syncManager.SyncNowAsync();
            }
        }

        RunOnAppThread(Sync);
    }

    /// <summary>
    /// Redraws every task widget and reloads its tasks.
    /// </summary>
    public static void RefreshAll(Context context)
    {
        if (AppWidgetManager.GetInstance(context) is not { } appWidgetManager) return;

        var appWidgetIds = appWidgetManager.GetAppWidgetIds(new ComponentName(context, Java.Lang.Class.FromType(typeof(TaskWidgetProvider))));
        if (appWidgetIds is null || appWidgetIds.Length == 0) return;

        Update(context, appWidgetManager, appWidgetIds);
    }

    /// <summary>
    /// Redraws the given task widgets, and builds their task rows again when <paramref name="reloadsTasks"/> is set.
    /// </summary>
    public static void Update(Context context, AppWidgetManager appWidgetManager, int[] appWidgetIds, bool reloadsTasks = true)
    {
        foreach (var appWidgetId in appWidgetIds) appWidgetManager.UpdateAppWidget(appWidgetId, CreateViews(context, appWidgetManager, appWidgetId));
        if (reloadsTasks) appWidgetManager.NotifyAppWidgetViewDataChanged(appWidgetIds, Resource.Id.task_widget_list);
    }

    /// <summary>
    /// Opens the app on a task, as tapping its reminder notification does.
    /// </summary>
    public static void OpenTask(Context context, TaskNavigationRequest request)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        intent.PutExtra(ReminderNotifications.TaskIdExtra, request.TaskId.ToString());
        if (request.ListId is { } listId) intent.PutExtra(ReminderNotifications.ListIdExtra, listId.ToString());
        context.StartActivity(intent);
    }

    private static RemoteViews CreateViews(Context context, AppWidgetManager appWidgetManager, int appWidgetId)
    {
        var hasStore = TaskWidgetData.HasStore(context);
        var view = hasStore ? TaskWidgetData.ReadView(context, appWidgetId) : null;
        var preferences = new TaskWidgetPreferences(context);
        var settings = preferences.GetSettings(appWidgetId);
        var isNarrow = appWidgetManager.GetAppWidgetOptions(appWidgetId)?.GetInt(AppWidgetManager.OptionAppwidgetMinWidth) is > 0 and < NarrowWidthDp;
        var views = new RemoteViews(context.PackageName, Resource.Layout.task_widget);

        views.SetInt(Resource.Id.task_widget_background, "setImageAlpha", settings.BackgroundAlpha);
        views.SetTextViewText(Resource.Id.task_widget_title, view?.Title ?? context.GetString(Resource.String.ApplicationName));
        views.SetTextViewTextSize(Resource.Id.task_widget_title, (int)ComplexUnitType.Sp, settings.HeaderTextSize);
        views.SetTextViewCompoundDrawablesRelative(Resource.Id.task_widget_title, 0, 0, view is null ? 0 : Resource.Drawable.task_widget_chevron, 0);
        views.SetViewVisibility(Resource.Id.task_widget_app_icon, isNarrow ? ViewStates.Gone : ViewStates.Visible);
        views.SetViewVisibility(Resource.Id.task_widget_refresh, hasStore && !isNarrow ? ViewStates.Visible : ViewStates.Gone);
        views.SetViewVisibility(Resource.Id.task_widget_add, view is null ? ViewStates.Gone : ViewStates.Visible);
        views.SetViewVisibility(Resource.Id.task_widget_assistant, view is not null && preferences.IsAssistantAvailable ? ViewStates.Visible : ViewStates.Gone);
        views.SetOnClickPendingIntent(Resource.Id.task_widget_assistant, CreateOpenAssistantIntent(context));
        views.SetOnClickPendingIntent(Resource.Id.task_widget_settings, CreateActivityIntent(context, appWidgetId, typeof(TaskWidgetSettingsActivity)));
        views.SetTextViewText(Resource.Id.task_widget_empty, context.GetString(hasStore ? Resource.String.TaskWidgetNoTasks : Resource.String.TaskWidgetSignedOut));

        var openAppIntent = CreateOpenAppIntent(context);
        views.SetOnClickPendingIntent(Resource.Id.task_widget_app_icon, openAppIntent);
        views.SetOnClickPendingIntent(Resource.Id.task_widget_empty, openAppIntent);
        views.SetOnClickPendingIntent(Resource.Id.task_widget_title, view is null ? openAppIntent : CreateActivityIntent(context, appWidgetId, typeof(TaskWidgetViewPickerActivity)));
        views.SetOnClickPendingIntent(Resource.Id.task_widget_add, CreateActivityIntent(context, appWidgetId, typeof(TaskWidgetAddActivity)));
        views.SetOnClickPendingIntent(Resource.Id.task_widget_refresh, CreateBroadcastIntent(context, appWidgetId, TaskWidgetProvider.RefreshAction, PendingIntentFlags.Immutable));

        var serviceIntent = new Intent(context, typeof(TaskWidgetItemsService));
        serviceIntent.PutExtra(AppWidgetManager.ExtraAppwidgetId, appWidgetId);
        serviceIntent.SetData(global::Android.Net.Uri.Parse(serviceIntent.ToUri(IntentUriType.Scheme)));
        // RemoteViews.RemoteCollectionItems replaces the adapter service only from Android 12, below the app's minimum version.
#pragma warning disable CS0618, CA1422
        views.SetRemoteAdapter(Resource.Id.task_widget_list, serviceIntent);
#pragma warning restore CS0618, CA1422
        views.SetEmptyView(Resource.Id.task_widget_list, Resource.Id.task_widget_empty);
        views.SetPendingIntentTemplate(Resource.Id.task_widget_list, CreateBroadcastIntent(context, appWidgetId, TaskWidgetProvider.ItemAction, OperatingSystem.IsAndroidVersionAtLeast(31) ? PendingIntentFlags.Mutable : 0));
        return views;
    }

    private static PendingIntent CreateOpenAppIntent(Context context)
    {
        var intent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!) ?? new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ResetTaskIfNeeded);
        return PendingIntent.GetActivity(context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    private static PendingIntent CreateOpenAssistantIntent(Context context)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetAction(MainActivity.OpenAssistantAction);
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        return PendingIntent.GetActivity(context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    private static PendingIntent CreateActivityIntent(Context context, int appWidgetId, Type activityType)
    {
        var intent = new Intent(context, activityType);
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
        intent.PutExtra(AppWidgetManager.ExtraAppwidgetId, appWidgetId);
        return PendingIntent.GetActivity(context, appWidgetId, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    private static PendingIntent CreateBroadcastIntent(Context context, int appWidgetId, string action, PendingIntentFlags mutabilityFlag)
    {
        var intent = new Intent(context, typeof(TaskWidgetProvider));
        intent.SetAction(action);
        intent.PutExtra(AppWidgetManager.ExtraAppwidgetId, appWidgetId);
        return PendingIntent.GetBroadcast(context, appWidgetId, intent, PendingIntentFlags.UpdateCurrent | mutabilityFlag)!;
    }

    private static bool RunOnAppThread(Action action)
    {
        if (s_dispatcherQueue is not { } dispatcherQueue) return false;
        if (dispatcherQueue.HasThreadAccess) action();
        else if (!dispatcherQueue.TryEnqueue(action.Invoke)) return false;
        return true;
    }

    /// <summary>
    /// Records the store the app opened for the signed-in user, or forgets it once the app closes it at sign-out.
    /// </summary>
    private static void RecordStore()
    {
        if (s_syncManager is not { } syncManager) return;

        var preferences = new TaskWidgetPreferences(ApplicationContext);
        if (syncManager.IsOpen && syncManager.DatabasePath is { } databasePath)
        {
            s_wasStoreOpen = true;
            if (preferences.DatabasePath == databasePath) return;
            preferences.DatabasePath = databasePath;
        }
        else
        {
            if (!s_wasStoreOpen) return;
            s_wasStoreOpen = false;
            preferences.DatabasePath = null;
        }

        RequestRefresh();
    }

    /// <summary>
    /// Redraws the widgets shortly, once for a burst of changes.
    /// </summary>
    private static void RequestRefresh()
    {
        if (s_isRefreshPending) return;

        s_isRefreshPending = true;
        s_handler.PostDelayed(() =>
        {
            s_isRefreshPending = false;
            RefreshAll(ApplicationContext);
        }, RefreshDelayMilliseconds);
    }

    private static void OnSyncManagerStatusChanged(object? _, EventArgs __) => RecordStore();

    private static void OnSyncManagerDataChanged(object? _, ChangesResponse __) => RequestRefresh();

    /// <summary>
    /// Shows the assistant button only while the signed-in server runs the assistant.
    /// </summary>
    private static void OnAssistantNavigationManagerAvailabilityChanged(object? _, EventArgs __)
    {
        if (s_assistantNavigationManager?.IsAssistantAvailable is not { } isAvailable) return;
        new TaskWidgetPreferences(ApplicationContext).IsAssistantAvailable = isAvailable;
        RequestRefresh();
    }
}
