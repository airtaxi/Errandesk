using Errandesk.Client.ViewModels;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Errandesk.Client.Pages;

/// <summary>
/// Lets the user enter the address of an Errandesk server and check the connection.
/// </summary>
public sealed partial class ServerConnectionPage : Page
{
    public ServerConnectionPage() => InitializeComponent();

    public ServerConnectionViewModel ViewModel { get; } = App.GetService<ServerConnectionViewModel>();

    private void OnServerAddressTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || !ViewModel.ConnectCommand.CanExecute(null)) return;
        e.Handled = true;
        ViewModel.ConnectCommand.Execute(null);
    }
}
