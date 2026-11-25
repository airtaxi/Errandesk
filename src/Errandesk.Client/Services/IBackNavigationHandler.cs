namespace Errandesk.Client.Services;

/// <summary>
/// A page that can go one step back within itself, such as closing a pane that replaced its list, when the system back button is pressed.
/// </summary>
public interface IBackNavigationHandler
{
    /// <summary>
    /// Whether the page shows something that going back would close.
    /// </summary>
    bool CanGoBack { get; }

    /// <summary>
    /// Closes what the page shows on top of its main content, the same as the page's own back or close button.
    /// </summary>
    void GoBack();
}
