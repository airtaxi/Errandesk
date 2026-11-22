namespace Errandesk.Client.Models;

/// <summary>
/// The navigation parameter of the task list page.
/// </summary>
/// <param name="View">The view to show.</param>
/// <param name="SelectedTaskId">A task to open in the detail pane once the view is loaded.</param>
public sealed record TaskListPageParameter(TaskViewDescriptor View, Guid? SelectedTaskId = null);
