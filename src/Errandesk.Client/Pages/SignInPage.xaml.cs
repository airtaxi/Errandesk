using Errandesk.Client.Models;
using Errandesk.Client.ViewModels;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Errandesk.Client.Pages;

/// <summary>
/// Signs in to the saved server.
/// </summary>
public sealed partial class SignInPage : Page
{
    public SignInPage() => InitializeComponent();

    public SignInViewModel ViewModel { get; } = App.GetService<SignInViewModel>();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync(e.Parameter as SignInPageParameter);
    }

    private void OnPasswordBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || !ViewModel.SignInCommand.CanExecute(null)) return;
        e.Handled = true;
        ViewModel.Password = ((PasswordBox)sender).Password;
        ViewModel.SignInCommand.Execute(null);
    }
}
