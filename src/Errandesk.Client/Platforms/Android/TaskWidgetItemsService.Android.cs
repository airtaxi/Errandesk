using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;

namespace Errandesk.Client.Droid;

/// <summary>
/// Supplies the task rows of each task widget.
/// </summary>
[Service(Permission = "android.permission.BIND_REMOTEVIEWS", Exported = false)]
public sealed class TaskWidgetItemsService : RemoteViewsService
{
    public override IRemoteViewsFactory? OnGetViewFactory(Intent? intent) => new TaskWidgetItemsFactory(ApplicationContext!, intent?.GetIntExtra(AppWidgetManager.ExtraAppwidgetId, AppWidgetManager.InvalidAppwidgetId) ?? AppWidgetManager.InvalidAppwidgetId);
}
