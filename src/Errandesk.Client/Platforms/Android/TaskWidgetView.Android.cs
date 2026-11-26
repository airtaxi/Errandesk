namespace Errandesk.Client.Droid;

/// <summary>
/// A view a task widget can show: My Day, Important, Planned or one list.
/// </summary>
/// <param name="Key">The view as <see cref="Sync.LocalDataService.GetTasks"/> takes it: a smart view name or a list ID.</param>
/// <param name="Title">The name shown in the widget header and the view picker.</param>
/// <param name="IconResourceId">The drawable shown beside the title in the view picker.</param>
/// <param name="IsCustomList">Whether the view is a list other than the default list.</param>
public sealed record TaskWidgetView(string Key, string Title, int IconResourceId, bool IsCustomList);
