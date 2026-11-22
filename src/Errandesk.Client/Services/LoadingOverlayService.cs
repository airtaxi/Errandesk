using Errandesk.Client.Views;

namespace Errandesk.Client.Services;

/// <summary>
/// Shows and hides the main window's loading overlay.
/// </summary>
public sealed class LoadingOverlayService
{
    public void Show(string? message = null) => MainWindow.ShowLoading(message);

    public void Hide() => MainWindow.HideLoading();
}
