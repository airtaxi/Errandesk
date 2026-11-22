using Errandesk.Client.ViewModels;
using Microsoft.UI.Xaml.Navigation;

namespace Errandesk.Client.Pages;

/// <summary>
/// Creates an account on the saved server.
/// </summary>
public sealed partial class SignUpPage : Page
{
    public SignUpPage() => InitializeComponent();

    public SignUpViewModel ViewModel { get; } = App.GetService<SignUpViewModel>();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync();
    }
}
