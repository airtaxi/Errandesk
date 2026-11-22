using Errandesk.Client.Helpers;
using Errandesk.Client.Views;
using Microsoft.UI.Xaml.Automation;
using Windows.System;

namespace Errandesk.Client.Services;

/// <summary>
/// Shows confirmation and text input dialogs over the main window.
/// </summary>
public sealed class DialogService
{
    private bool _isDialogOpen;

    /// <summary>
    /// Asks the user to confirm an action. Returns <see langword="false"/> without asking while another dialog is open.
    /// </summary>
    /// <returns><see langword="true"/> when the user chose the primary button.</returns>
    public async Task<bool> ConfirmAsync(string title, string message, string primaryButtonText)
    {
        if (_isDialogOpen) return false;

        var contentDialog = CreateDialog(title, primaryButtonText);
        contentDialog.Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        return await ShowAsync(contentDialog) == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Asks the user for a line of text.
    /// </summary>
    /// <returns>The trimmed text, or <see langword="null"/> when the user canceled, entered nothing or another dialog is open.</returns>
    public async Task<string?> RequestTextAsync(string title, string? initialText, string placeholderText, string primaryButtonText)
    {
        if (_isDialogOpen) return null;

        var textBox = new TextBox { Text = initialText ?? string.Empty, PlaceholderText = placeholderText, MaxLength = 200 };
        AutomationProperties.SetAutomationId(textBox, "DialogTextBox");
        AutomationProperties.SetName(textBox, title);
        textBox.SelectAll();

        var contentDialog = CreateDialog(title, primaryButtonText);
        contentDialog.Content = textBox;
        textBox.KeyDown += (_, keyEventArgs) =>
        {
            if (keyEventArgs.Key != VirtualKey.Enter) return;
            keyEventArgs.Handled = true;
            contentDialog.Tag = textBox.Text;
            contentDialog.Hide();
        };

        var result = await ShowAsync(contentDialog);
        var text = result == ContentDialogResult.Primary || contentDialog.Tag is string ? textBox.Text.Trim() : string.Empty;
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Shows <paramref name="content"/> in a dialog with a primary and a cancel button. Returns <see langword="false"/> without showing it while another dialog is open.
    /// </summary>
    /// <returns><see langword="true"/> when the user chose the primary button.</returns>
    public async Task<bool> ShowContentAsync(string title, UIElement content, string primaryButtonText)
    {
        if (_isDialogOpen) return false;

        var contentDialog = CreateDialog(title, primaryButtonText);
        contentDialog.Content = content;
        return await ShowAsync(contentDialog) == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Shows <paramref name="content"/> in a dialog with a close button and, when <paramref name="primaryButtonText"/> is set, a primary button that is not the default button.
    /// Returns <see langword="false"/> without showing it while another dialog is open.
    /// </summary>
    /// <returns><see langword="true"/> when the user chose the primary button.</returns>
    public async Task<bool> ShowPanelAsync(string title, UIElement content, string closeButtonText, string? primaryButtonText)
    {
        if (_isDialogOpen) return false;

        var contentDialog = CreateDialog(title, primaryButtonText ?? string.Empty);
        contentDialog.CloseButtonText = closeButtonText;
        contentDialog.DefaultButton = ContentDialogButton.None;
        contentDialog.Content = content;
        return await ShowAsync(contentDialog) == ContentDialogResult.Primary;
    }

    private async Task<ContentDialogResult> ShowAsync(ContentDialog contentDialog)
    {
        _isDialogOpen = true;
        try { return await contentDialog.ShowAsync(); }
        finally { _isDialogOpen = false; }
    }

    private static ContentDialog CreateDialog(string title, string primaryButtonText)
    {
        var contentDialog = new ContentDialog
        {
            Title = title,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = LocalizedStrings.Get("DialogCancelButtonText"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = MainWindow.DialogXamlRoot,
            RequestedTheme = MainWindow.ActualRootTheme,
        };

        AutomationProperties.SetAutomationId(contentDialog, "ContentDialog");
        return contentDialog;
    }
}
