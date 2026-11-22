namespace Errandesk.Server.Data;

/// <summary>
/// A task list. The owner and the members can read and change its tasks; only the owner renames, deletes and shares it.
/// </summary>
public sealed class TaskListDocument : DocumentBase
{
    public const string DefaultListName = "Tasks";

    public string Name { get; set; } = "";

    public double SortOrder { get; set; }

    /// <summary>Whether this is the owner's default list. Each user has exactly one, which cannot be renamed, deleted or shared.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Service the list was imported from, one of <see cref="Errandesk.Contracts.ImportSources"/>, or <see langword="null"/> for lists created in Errandesk.</summary>
    public string? ExternalSource { get; set; }

    /// <summary>ID of the list in <see cref="ExternalSource"/>.</summary>
    public string? ExternalId { get; set; }

    /// <summary>Users the list is shared with, in the order they were added.</summary>
    public List<TaskListMemberDocument> Members { get; set; } = [];

    /// <summary>Users who were members and were removed or left. The change feed tells them to drop the list and its tasks. A user added again is taken off this list.</summary>
    public List<Guid> FormerMemberUserIds { get; set; } = [];

    /// <summary>Whether <paramref name="userId"/> owns the list or is a member.</summary>
    public bool CanAccess(Guid userId) => OwnerUserId == userId || Members.Exists(member => member.UserId == userId);

    /// <summary>The owner followed by every member.</summary>
    public IEnumerable<Guid> GetAudience() =>
    [OwnerUserId, .. Members.Select(member => member.UserId)];
}
