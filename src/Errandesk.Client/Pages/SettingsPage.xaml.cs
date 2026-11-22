using System.ComponentModel;
using Errandesk.Client.Models;
using Errandesk.Client.ViewModels;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Errandesk.Client.Pages;

/// <summary>
/// Profile, session, synchronization, language, theme, import, personal access token and invite settings.
/// </summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage() => InitializeComponent();

    public SettingsViewModel ViewModel { get; } = App.GetService<SettingsViewModel>();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        await ViewModel.InitializeAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Unload();
    }

    /// <summary>
    /// Keeps the keyboard focus in the import section: it moves from the import button, which is disabled while the import waits for the sign-in, to the button that copies the sign-in code, and back when the code is hidden.
    /// </summary>
    private void OnViewModelPropertyChanged(object? _, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsNewAccessTokenVisible))
        {
            if (ViewModel.IsNewAccessTokenVisible) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => CopyAccessTokenButton.Focus(FocusState.Programmatic));
            return;
        }

        if (e.PropertyName != nameof(SettingsViewModel.IsImportCodeVisible)) return;
        if (ViewModel.IsImportCodeVisible && StartMicrosoftTodoImportButton.FocusState != FocusState.Unfocused) DispatcherQueue.TryEnqueue(() => CopyImportCodeButton.Focus(FocusState.Programmatic));
        else if (!ViewModel.IsImportCodeVisible && CopyImportCodeButton.FocusState != FocusState.Unfocused) DispatcherQueue.TryEnqueue(() => StartMicrosoftTodoImportButton.Focus(FocusState.Programmatic));
    }

    private void OnCopyImportCodeButtonClicked(object sender, RoutedEventArgs e) => ViewModel.CopyImportCode();

    private void OnCopyMcpEndpointButtonClicked(object sender, RoutedEventArgs e) => ViewModel.CopyMcpEndpoint();

    private void OnCopyAccessTokenButtonClicked(object sender, RoutedEventArgs e) => ViewModel.CopyNewAccessToken();

    private void OnNewAccessTokenInfoBarClosed(InfoBar sender, InfoBarClosedEventArgs args) => ViewModel.DismissNewAccessToken();

    private void OnAccessTokenNameTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || !ViewModel.IsCreateAccessTokenEnabled) return;
        e.Handled = true;
        ViewModel.CreateAccessTokenCommand.Execute(null);
    }

    /// <summary>Revokes the token of the row; when the row is removed, the keyboard focus moves to the token name box instead of leaving the section.</summary>
    private async void OnRevokeAccessTokenButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PersonalAccessTokenModel accessToken }) return;
        await ViewModel.RevokeAccessTokenAsync(accessToken);
        if (!ViewModel.AccessTokens.Contains(accessToken)) AccessTokenNameTextBox.Focus(FocusState.Programmatic);
    }

    private void OnCopyInviteButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: InviteModel invite })
        {
            ViewModel.CopyInvite(invite);
        }
    }

    private async void OnDeleteInviteButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: InviteModel invite })
        {
            await ViewModel.DeleteInviteAsync(invite);
        }
    }
}
