namespace Errandesk.Contracts;

/// <summary>
/// Creates a task list.
/// </summary>
/// <param name="Name">List name.</param>
/// <param name="Id">Client-generated ID. Creating an ID that already exists for the same user returns the existing list.</param>
/// <param name="SortOrder">Position among the user's lists. Defaults to after the last list.</param>
public sealed record CreateTaskListRequest(string Name, Guid? Id = null, double? SortOrder = null);
