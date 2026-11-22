using Errandesk.Client.Helpers;
using Errandesk.Contracts;

namespace Errandesk.Client.Models;

/// <summary>
/// A person who can see a shared list, as shown in the share dialog.
/// </summary>
/// <param name="member">The owner or a member of the list.</param>
/// <param name="isOwner">Whether this person owns the list.</param>
/// <param name="isCurrentUser">Whether this person is the signed-in user.</param>
/// <param name="canRemove">Whether the signed-in user may remove this person from the list.</param>
public sealed class ListMemberModel(TaskListMemberResponse member, bool isOwner, bool isCurrentUser, bool canRemove)
{
    public Guid UserId { get; } = member.UserId;

    public string DisplayName { get; } = member.DisplayName;

    public string UserName { get; } = member.UserName;

    public bool CanRemove { get; } = canRemove;

    /// <summary>
    /// "Owner" for the owner and "You" for the signed-in user; empty for other members.
    /// </summary>
    public string RoleText { get; } = isOwner ? LocalizedStrings.Get("ListOwnerRoleText") : isCurrentUser ? LocalizedStrings.Get("ListCurrentUserRoleText") : string.Empty;

    public bool HasRole => RoleText.Length > 0;

    public string RemoveButtonName => string.Format(System.Globalization.CultureInfo.CurrentCulture, LocalizedStrings.Get("RemoveListMemberButtonNameFormat"), DisplayName);

    public override string ToString() => DisplayName;
}
