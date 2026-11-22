namespace Errandesk.Contracts;

/// <summary>
/// A task list.
/// </summary>
/// <param name="Id">List ID.</param>
/// <param name="Name">List name. The default list is stored as <c>Tasks</c> and shown in the user's language.</param>
/// <param name="SortOrder">Ascending position among the owner's lists.</param>
/// <param name="IsDefault">Whether this is the owner's default list, which cannot be renamed, deleted or shared.</param>
/// <param name="CreatedAt">When the list was created.</param>
/// <param name="UpdatedAt">When the list was last changed.</param>
/// <param name="ChangeVersion">Global change version of the last change.</param>
/// <param name="Owner">The user who owns the list. Only the owner renames, deletes and shares it.</param>
/// <param name="Members">The users the list is shared with, in the order they were added. Empty when the list is not shared.</param>
public sealed record TaskListResponse(Guid Id, string Name, double SortOrder, bool IsDefault, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long ChangeVersion, TaskListMemberResponse? Owner = null, IReadOnlyList<TaskListMemberResponse>? Members = null);
