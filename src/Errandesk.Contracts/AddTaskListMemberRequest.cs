namespace Errandesk.Contracts;

/// <summary>
/// Shares a task list with another user.
/// </summary>
/// <param name="UserName">The exact user name of the user to add, ignoring case.</param>
public sealed record AddTaskListMemberRequest(string UserName);
