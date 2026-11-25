using Errandesk.Client.Services;
using Microsoft.UI.Xaml.Navigation;
#if ANDROID
using Microsoft.UI.Xaml.Media;
using Windows.UI.Core;
#endif

namespace Errandesk.Client.Managers;

/// <summary>
/// Passes the Android system back button to the page shown in the main frame. The app listens for the button only while that page can go back,
/// so that otherwise Android handles it, for example by leaving the app; Android 16 and later decide this from the subscription alone.
/// </summary>
public sealed class BackNavigationManager
{
    private Frame? _frame;
#if ANDROID
    private bool _isSubscribed;
#endif

    public void Initialize(Frame frame)
    {
        _frame = frame;
        frame.Navigated += OnFrameNavigated;
        Update();
    }

    /// <summary>
    /// Listens for the system back button while the shown page can go back and stops listening when it cannot. Pages call this whenever that changes.
    /// </summary>
    public void Update()
    {
#if ANDROID
        var canGoBack = GetHandler() is { CanGoBack: true };
        if (canGoBack == _isSubscribed) return;

        var systemNavigationManager = SystemNavigationManager.GetForCurrentView();
        if (canGoBack) systemNavigationManager.BackRequested += OnSystemNavigationManagerBackRequested;
        else systemNavigationManager.BackRequested -= OnSystemNavigationManagerBackRequested;
        _isSubscribed = canGoBack;
#endif
    }

    private IBackNavigationHandler? GetHandler() => _frame?.Content as IBackNavigationHandler;

    private void OnFrameNavigated(object sender, NavigationEventArgs e) => Update();

#if ANDROID
    /// <summary>
    /// Goes back on the shown page. While a dialog or flyout is open the page stays as it is, as the button belongs to the popup.
    /// </summary>
    private void OnSystemNavigationManagerBackRequested(object? _, BackRequestedEventArgs e)
    {
        e.Handled = true;
        if (_frame?.XamlRoot is { } xamlRoot && VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot).Count > 0) return;
        if (GetHandler() is { CanGoBack: true } handler) handler.GoBack();
        Update();
    }
#endif
}
