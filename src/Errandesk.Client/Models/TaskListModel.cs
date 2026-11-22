using Errandesk.Client.Helpers;
using Errandesk.Contracts;

namespace Errandesk.Client.Models;

/// <summary>
/// A task list as shown in the navigation pane and list headers.
/// </summary>
public sealed partial class TaskListModel : ObservableObject
{
    private readonly Guid? _currentUserId;

    /// <param name="response">The list.</param>
    /// <param name="currentUserId">The signed-in user, who owns the list unless <see cref="TaskListResponse.Owner"/> names someone else.</param>
    public TaskListModel(TaskListResponse response, Guid? currentUserId)
    {
        Id = response.Id;
        IsDefault = response.IsDefault;
        _currentUserId = currentUserId;
        Apply(response);
    }

    public Guid Id { get; }

    public bool IsDefault { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double SortOrder { get; set; }

    /// <summary>
    /// The owner of the list, or <see langword="null"/> while the server has not reported it, which only happens for lists the signed-in user created.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOwnedByCurrentUser), nameof(IsShared))]
    public partial TaskListMemberResponse? Owner { get; set; }

    /// <summary>
    /// The users the list is shared with, in the order they were added.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShared))]
    public partial IReadOnlyList<TaskListMemberResponse> Members { get; set; } = [];

    public long ChangeVersion { get; private set; }

    /// <summary>
    /// The name shown in the interface. The default list is shown as "Tasks" in the interface language.
    /// </summary>
    public string DisplayName => IsDefault ? LocalizedStrings.Get("DefaultListName") : Name;

    /// <summary>
    /// Whether the signed-in user owns the list and so may rename, delete and share it.
    /// </summary>
    public bool IsOwnedByCurrentUser => Owner is null || Owner.UserId == _currentUserId;

    /// <summary>
    /// Whether other users can see the list: it has members, or someone else shared it with the signed-in user.
    /// </summary>
    public bool IsShared => Members.Count > 0 || !IsOwnedByCurrentUser;

    /// <summary>
    /// Copies the values of <paramref name="response"/> unless it is older than the values already shown.
    /// </summary>
    public void Apply(TaskListResponse response)
    {
        if (response.ChangeVersion < ChangeVersion) return;
        ChangeVersion = response.ChangeVersion;
        Name = response.Name;
        SortOrder = response.SortOrder;
        if (response.Owner is null) return;
        Owner = response.Owner;
        Members = response.Members ?? [];
    }
}
