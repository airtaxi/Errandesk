namespace Errandesk.Server.Data;

/// <summary>
/// A user a <see cref="TaskListDocument"/> is shared with.
/// </summary>
public sealed class TaskListMemberDocument
{
    public Guid UserId { get; set; }

    public DateTime AddedAt { get; set; }

    /// <summary>Global change version taken when the user was added. The change feed returns every task of the list to a member whose version is newer than its <c>since</c>.</summary>
    public long ChangeVersion { get; set; }
}
