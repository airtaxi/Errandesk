using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using Errandesk.Client.Helpers;
using Errandesk.Client.Managers;
using Errandesk.Client.Models;
using Errandesk.Client.Services;
using Errandesk.Contracts;

namespace Errandesk.Client.ViewModels;

/// <summary>
/// The share dialog of a task list: the owner adds people by their exact user name and removes them; a member sees who has access and can leave.
/// </summary>
public sealed partial class ShareListViewModel(AuthenticatedApiClient apiClient, SyncManager syncManager, SessionManager sessionManager) : ObservableObject
{
    /// <summary>
    /// The ProblemDetails <c>detail</c> of the 404 the server answers when no user has the requested name.
    /// </summary>
    private const string UnknownUserProblemDetail = "The user was not found.";

    private TaskListModel? _list;

    public ObservableCollection<ListMemberModel> Members { get; } = [];

    /// <summary>
    /// Whether the signed-in user owns the list and so may add and remove people.
    /// </summary>
    [ObservableProperty]
    public partial bool IsOwner { get; set; }

    [ObservableProperty]
    public partial string DialogTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DescriptionText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddMemberCommand))]
    public partial string NewMemberUserName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddMemberCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsEmptyTextVisible { get; set; }

    [ObservableProperty]
    public partial bool IsErrorVisible { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Shows the people of <paramref name="list"/> and follows changes to it until <see cref="Close"/>.
    /// </summary>
    public void Initialize(TaskListModel list)
    {
        _list = list;
        list.PropertyChanged += OnListPropertyChanged;
        Refresh();
    }

    public void Close()
    {
        if (_list is not null)
        {
            _list.PropertyChanged -= OnListPropertyChanged;
        }
    }

    /// <summary>
    /// Leaves the shared list and removes it with its tasks from this device. Pending changes are sent first, so edits made before leaving reach the server.
    /// </summary>
    /// <exception cref="ApiException">The server refused the request.</exception>
    /// <exception cref="HttpRequestException">The server could not be reached.</exception>
    public async Task LeaveAsync()
    {
        if (_list is not { } list || sessionManager.CurrentUser is not { } user) return;

        if (syncManager.PendingOperationCount > 0) await syncManager.SyncNowAsync();
        try { await apiClient.RemoveTaskListMemberAsync(list.Id, user.Id, CancellationToken.None); }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound) { }

        if (syncManager.IsOpen) syncManager.Data.ForgetList(list.Id);
    }

    private bool CanAddMember() => !IsBusy && NewMemberUserName.Trim().Length > 0;

    /// <summary>
    /// Shares the list with the user whose name matches the entered name exactly, ignoring case.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddMember))]
    private async Task AddMemberAsync()
    {
        if (_list is not { } list) return;

        var userName = NewMemberUserName.Trim();
        IsErrorVisible = false;
        if (string.Equals(userName, sessionManager.CurrentUser?.UserName, StringComparison.OrdinalIgnoreCase))
        {
            ShowError(LocalizedStrings.Get("ShareWithSelfError"));
            return;
        }

        if (list.Members.Any(member => string.Equals(member.UserName, userName, StringComparison.OrdinalIgnoreCase)))
        {
            NewMemberUserName = string.Empty;
            return;
        }

        IsBusy = true;
        try
        {
            if (syncManager.PendingOperationCount > 0) await syncManager.SyncNowAsync();
            var response = await apiClient.AddTaskListMemberAsync(list.Id, new AddTaskListMemberRequest(userName), CancellationToken.None);
            NewMemberUserName = string.Empty;
            if (!syncManager.IsOpen) return;
            if (!syncManager.Data.StoreServerList(response)) await syncManager.SyncNowAsync();
        }
        catch (ApiException exception) when (exception is { StatusCode: HttpStatusCode.NotFound, ProblemDetail: UnknownUserProblemDetail }) { ShowError(string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("ShareUserNotFoundErrorFormat"), userName)); }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(ApiErrorMessages.GetMessage(exception)); }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Removes a member. The list on this device is updated by the next synchronization, which starts right away.
    /// </summary>
    [RelayCommand]
    private async Task RemoveMemberAsync(ListMemberModel member)
    {
        if (_list is not { } list || !IsOwner) return;

        IsErrorVisible = false;
        IsBusy = true;
        try
        {
            try { await apiClient.RemoveTaskListMemberAsync(list.Id, member.UserId, CancellationToken.None); }
            catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound) { }

            Members.Remove(member);
            UpdateEmptyText();
            await syncManager.SyncNowAsync();
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(ApiErrorMessages.GetMessage(exception)); }
        finally { IsBusy = false; }
    }

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TaskListModel.Owner) or nameof(TaskListModel.Members) or nameof(TaskListModel.Name))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        if (_list is not { } list) return;

        var currentUserId = sessionManager.CurrentUser?.Id;
        IsOwner = list.IsOwnedByCurrentUser;
        var owner = list.Owner ?? (sessionManager.CurrentUser is { } user ? new TaskListMemberResponse(user.Id, user.UserName, user.DisplayName, DateTimeOffset.UtcNow) : null);
        DialogTitle = string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get(IsOwner ? "ShareListDialogTitleFormat" : "ListMembersDialogTitleFormat"), list.DisplayName);
        DescriptionText = IsOwner ? LocalizedStrings.Get("ShareListOwnerDescription") : string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("ShareListMemberDescriptionFormat"), owner?.DisplayName);

        Members.Clear();
        if (owner is not null) Members.Add(new ListMemberModel(owner, isOwner: true, isCurrentUser: owner.UserId == currentUserId, canRemove: false));
        foreach (var member in list.Members) Members.Add(new ListMemberModel(member, isOwner: false, isCurrentUser: member.UserId == currentUserId, canRemove: IsOwner));
        UpdateEmptyText();
    }

    private void UpdateEmptyText() => IsEmptyTextVisible = IsOwner && Members.Count <= 1;

    private void ShowError(string? message)
    {
        if (message is null) return;
        ErrorMessage = message;
        IsErrorVisible = true;
    }
}
