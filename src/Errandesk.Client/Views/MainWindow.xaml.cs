using Errandesk.Client.Helpers;
using Errandesk.Client.Managers;
using Errandesk.Client.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Errandesk.Client.Views;

/// <summary>
/// The application window: a custom title bar on platforms that support it, the navigation frame and a loading overlay.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const double FocusedElementInputPaneSpacing = 20;

    private static MainWindow? s_instance;

    private readonly SyncManager _syncManager;
    private UIElement? _pendingBringIntoViewElement;

    public MainWindow(NavigationService navigationService, BackNavigationManager backNavigationManager, SyncManager syncManager)
    {
        InitializeComponent();
        s_instance = this;
        _syncManager = syncManager;
        Title = LocalizedStrings.Get("ApplicationName");
        navigationService.Initialize(AppFrame);
        backNavigationManager.Initialize(AppFrame);
        ConfigureTitleBar();
        ConfigureInputPane();
        _syncManager.StatusChanged += OnSyncManagerStatusChanged;
    }

    /// <summary>
    /// The XAML root that hosts dialogs.
    /// </summary>
    public static XamlRoot? DialogXamlRoot => s_instance?.RootGrid.XamlRoot;

    /// <summary>
    /// The theme the window content currently uses, for popups that do not inherit it.
    /// </summary>
    public static ElementTheme ActualRootTheme => s_instance?.RootGrid.ActualTheme ?? ElementTheme.Default;

    /// <summary>
    /// Sets the theme of the window content; <see cref="ElementTheme.Default"/> follows the system.
    /// </summary>
    public static void SetRequestedTheme(ElementTheme theme)
    {
        if (s_instance is not { } instance) return;
        instance.RootGrid.RequestedTheme = theme;
        instance.UpdateCaptionButtonColors();
    }

    /// <summary>
    /// Covers the page with the loading overlay and disables it until <see cref="HideLoading"/> is called.
    /// </summary>
    public static void ShowLoading(string? message = null)
    {
        if (s_instance is not { } instance) return;
        instance.DispatcherQueue.TryEnqueue(() =>
        {
            instance.AppFrame.IsEnabled = false;

            if (string.IsNullOrWhiteSpace(message)) instance.LoadingTextBlock.Visibility = Visibility.Collapsed;
            else
            {
                instance.LoadingTextBlock.Visibility = Visibility.Visible;
                instance.LoadingTextBlock.Text = message;
            }

            instance.LoadingGrid.Visibility = Visibility.Visible;
        });
    }

    /// <summary>
    /// Removes the loading overlay and enables the page again.
    /// </summary>
    public static void HideLoading()
    {
        if (s_instance is not { } instance) return;
        instance.DispatcherQueue.TryEnqueue(() =>
        {
            instance.LoadingGrid.Visibility = Visibility.Collapsed;
            instance.AppFrame.IsEnabled = true;
        });
    }

    private void ConfigureTitleBar()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            AppTitleBar.Visibility = Visibility.Collapsed;
            return;
        }

        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SetTitleBar(AppTitleBar);
        UpdateCaptionButtonColors();
    }

    /// <summary>
    /// Follows the on-screen keyboard on mobile platforms, where it covers the bottom of the window.
    /// </summary>
    private void ConfigureInputPane()
    {
        if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS()) return;

        var inputPane = InputPane.GetForCurrentView();
        inputPane.Showing += OnInputPaneShowing;
        inputPane.Hiding += OnInputPaneHiding;
    }

    /// <summary>
    /// Shrinks the window content so that it ends at the top of the keyboard, instead of the whole content moving up and out of view.
    /// Text boxes in popups such as dialogs are left to the framework, which moves the popup above the keyboard.
    /// </summary>
    private void OnInputPaneShowing(InputPane sender, InputPaneVisibilityEventArgs args)
    {
        if (RootGrid.XamlRoot is not { } xamlRoot || FocusManager.GetFocusedElement(xamlRoot) is not UIElement focusedElement || !IsInRootGrid(focusedElement))
        {
            RootGrid.Margin = default;
            return;
        }

        args.EnsuredFocusedElementInView = true;
        _pendingBringIntoViewElement = focusedElement;
        var margin = new Thickness(0, 0, 0, Math.Max(0, xamlRoot.Size.Height - sender.OccludedRect.Y));
        if (RootGrid.Margin == margin) DispatcherQueue.TryEnqueue(BringPendingElementIntoView);
        else RootGrid.Margin = margin;
    }

    private void OnInputPaneHiding(InputPane sender, InputPaneVisibilityEventArgs args)
    {
        _pendingBringIntoViewElement = null;
        RootGrid.Margin = default;
    }

    /// <summary>
    /// Scrolls the focused text box into view once the content has been laid out above the keyboard.
    /// </summary>
    private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_pendingBringIntoViewElement is not null)
        {
            DispatcherQueue.TryEnqueue(BringPendingElementIntoView);
        }
    }

    private void BringPendingElementIntoView()
    {
        if (_pendingBringIntoViewElement is not { } element) return;
        _pendingBringIntoViewElement = null;

        var size = element.RenderSize;
        var targetRect = new Rect(0, 0, size.Width, size.Height);
        if (size.Height + (2 * FocusedElementInputPaneSpacing) <= RootGrid.ActualHeight) targetRect = new Rect(0, -FocusedElementInputPaneSpacing, size.Width, size.Height + (2 * FocusedElementInputPaneSpacing));
        element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, TargetRect = targetRect });
    }

    private bool IsInRootGrid(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, RootGrid))
            {
                return true;
            }
        }

        return false;
    }

    private void OnRootGridActualThemeChanged(FrameworkElement sender, object e) => UpdateCaptionButtonColors();

    /// <summary>
    /// Shows the offline indicator in the title bar while the signed-in user's data cannot be synchronized.
    /// </summary>
    private void OnSyncManagerStatusChanged(object? _, EventArgs __) => OfflineIndicator.Visibility = _syncManager.IsOpen && !_syncManager.IsOnline ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateCaptionButtonColors()
    {
        if (!ExtendsContentIntoTitleBar) return;

        var titleBar = AppWindow.TitleBar;
        var isDarkTheme = RootGrid.ActualTheme == ElementTheme.Dark;
        var foregroundColor = isDarkTheme ? Colors.White : Colors.Black;
        titleBar.ButtonForegroundColor = foregroundColor;
        titleBar.ButtonHoverForegroundColor = foregroundColor;
        titleBar.ButtonPressedForegroundColor = foregroundColor;
        titleBar.ButtonInactiveForegroundColor = isDarkTheme ? Color.FromArgb(0xFF, 0x9A, 0x9A, 0x9A) : Color.FromArgb(0xFF, 0x70, 0x70, 0x70);
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonHoverBackgroundColor = isDarkTheme ? Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x09, 0x00, 0x00, 0x00);
        titleBar.ButtonPressedBackgroundColor = isDarkTheme ? Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x06, 0x00, 0x00, 0x00);
    }
}
