using Errandesk.Contracts;

namespace Errandesk.Client.Models;

/// <summary>
/// Identifies a task view: one of the smart lists, or a task list by its ID.
/// </summary>
public sealed record TaskViewDescriptor(TaskViewKind Kind, Guid? ListId = null)
{
    public static TaskViewDescriptor MyDay { get; } = new(TaskViewKind.MyDay);

    public static TaskViewDescriptor Important { get; } = new(TaskViewKind.Important);

    public static TaskViewDescriptor Planned { get; } = new(TaskViewKind.Planned);

    public bool IsSmartView => Kind != TaskViewKind.List;

    /// <summary>
    /// The value of the <c>view</c> query parameter of the task query.
    /// </summary>
    public string QueryValue => Kind switch
    {
        TaskViewKind.MyDay => TaskViews.MyDay,
        TaskViewKind.Important => TaskViews.Important,
        TaskViewKind.Planned => TaskViews.Planned,
        _ => ListId?.ToString() ?? throw new InvalidOperationException("A list view needs a list ID."),
    };

    public static TaskViewDescriptor ForList(Guid listId) => new(TaskViewKind.List, listId);
}
