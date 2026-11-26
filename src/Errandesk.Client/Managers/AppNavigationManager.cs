using Errandesk.Client.Helpers;
using Errandesk.Client.Models;
using Errandesk.Client.Pages;
using Errandesk.Client.Services;
using Microsoft.UI.Dispatching;

namespace Errandesk.Client.Managers;

/// <summary>
/// Chooses the top-level page from the saved server address and session: the connection page, the sign-in page or the shell.
/// </summary>
public sealed class AppNavigationManager(SessionManager sessionManager, NavigationService navigationService, LoadingOverlayService loadingOverlayService, TaskListsManager taskListsManager, ReminderManager reminderManager, TaskNavigationManager taskNavigationManager, AssistantNavigationManager assistantNavigationManager, SyncManager syncManager)
{
    private DispatcherQueue? _dispatcherQueue;

    /// <summary>
    /// Opens the first page. A stored session is resumed; when the server cannot be reached, the shell opens with the data stored on this device,
    /// or the sign-in page offers to try again when there is none.
    /// </summary>
    public async Task StartAsync()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        sessionManager.SessionEnded -= OnSessionManagerSessionEnded;
        sessionManager.SessionEnded += OnSessionManagerSessionEnded;

        if (!sessionManager.TryGetServerAddress(out _))
        {
            navigationService.Navigate<ServerConnectionPage>();
            return;
        }

        if (!sessionManager.HasStoredSession)
        {
            navigationService.Navigate<SignInPage>();
            return;
        }

        loadingOverlayService.Show(LocalizedStrings.Get("SigningInMessage"));
        try
        {
            if (await sessionManager.TryRestoreAsync(CancellationToken.None)) navigationService.Navigate<ShellPage>();
            else navigationService.Navigate<SignInPage>();
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception))
        {
            if (sessionManager.TryResumeOffline() && sessionManager.CurrentUser is { } user && SyncManager.HasLocalData(sessionManager.ServerAddress, user.Id)) navigationService.Navigate<ShellPage>();
            else navigationService.Navigate<SignInPage>(new SignInPageParameter(ApiErrorMessages.GetMessage(exception), IsRestoreRetryAvailable: true));
        }
        finally { loadingOverlayService.Hide(); }
    }

    public void ShowShell() => navigationService.Navigate<ShellPage>();

    public void ShowSignIn() => navigationService.Navigate<SignInPage>();

    public void ShowSignUp() => navigationService.Navigate<SignUpPage>();

    public void ShowServerConnection() => navigationService.Navigate<ServerConnectionPage>();

    public async Task SignOutAsync()
    {
        await EndSessionAsync();
        navigationService.Navigate<SignInPage>();
    }

    /// <summary>
    /// Signs out and opens the connection page so the user can enter another server.
    /// </summary>
    public async Task ChangeServerAsync()
    {
        await EndSessionAsync();
        navigationService.Navigate<ServerConnectionPage>();
    }

    /// <summary>
    /// Removes this device and its reminders while the session is still valid, deletes the user's data stored on this device, then signs out.
    /// </summary>
    private async Task EndSessionAsync()
    {
        loadingOverlayService.Show(LocalizedStrings.Get("SigningOutMessage"));
        try
        {
            await reminderManager.SignOutAsync();
            syncManager.DeleteLocalData();
            await sessionManager.SignOutAsync();
        }
        finally { loadingOverlayService.Hide(); }

        taskListsManager.Clear();
        taskNavigationManager.Clear();
        assistantNavigationManager.Clear();
    }

    private void OnSessionManagerSessionEnded(object? _, EventArgs __)
    {
        async void ShowSignInAfterSessionEnded()
        {
            syncManager.Stop();
            taskListsManager.Clear();
            taskNavigationManager.Clear();
            assistantNavigationManager.Clear();
            navigationService.Navigate<SignInPage>(new SignInPageParameter(LocalizedStrings.Get("SessionEndedMessage"), IsRestoreRetryAvailable: false));
            await reminderManager.ClearAsync();
        }

        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess) ShowSignInAfterSessionEnded();
        else _dispatcherQueue.TryEnqueue(ShowSignInAfterSessionEnded);
    }
}
