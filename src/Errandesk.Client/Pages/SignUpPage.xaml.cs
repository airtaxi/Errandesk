using Errandesk.Client.Services;
using Errandesk.Client.ViewModels;
using Microsoft.UI.Xaml.Navigation;

namespace Errandesk.Client.Pages;

/// <summary>
/// Creates an account on the saved server. Going back returns to sign-in.
/// </summary>
public sealed partial class SignUpPage : Page, IBackNavigationHandler
{
    public SignUpPage() => InitializeComponent();

    public SignUpViewModel ViewModel { get; } = App.GetService<SignUpViewModel>();

    public bool CanGoBack => true;

    public void GoBack() => ViewModel.ShowSignInCommand.Execute(null);

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync();
    }
}
