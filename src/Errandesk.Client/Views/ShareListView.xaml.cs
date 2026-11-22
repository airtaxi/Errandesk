using Errandesk.Client.Models;
using Errandesk.Client.ViewModels;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Errandesk.Client.Views;

/// <summary>
/// The content of the share dialog of a task list.
/// </summary>
public sealed partial class ShareListView : UserControl
{
    public ShareListView(ShareListViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public ShareListViewModel ViewModel { get; }

    private void OnShareListViewLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsOwner)
        {
            MemberUserNameTextBox.Focus(FocusState.Programmatic);
        }
    }

    private async void OnMemberUserNameTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        ViewModel.NewMemberUserName = ((TextBox)sender).Text;
        if (ViewModel.AddMemberCommand.CanExecute(null)) await ViewModel.AddMemberCommand.ExecuteAsync(null);
        MemberUserNameTextBox.Focus(FocusState.Programmatic);
    }

    private async void OnRemoveListMemberButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ListMemberModel member }) return;
        await ViewModel.RemoveMemberCommand.ExecuteAsync(member);
        MemberUserNameTextBox.Focus(FocusState.Programmatic);
    }
}
