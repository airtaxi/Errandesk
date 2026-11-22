namespace Errandesk.Contracts;

/// <summary>
/// A user who owns a task list or shares it.
/// </summary>
/// <param name="UserId">User ID.</param>
/// <param name="UserName">User name.</param>
/// <param name="DisplayName">Display name.</param>
/// <param name="AddedAt">When the user was added to the list; for the owner, when the list was created.</param>
public sealed record TaskListMemberResponse(Guid UserId, string UserName, string DisplayName, DateTimeOffset AddedAt);
