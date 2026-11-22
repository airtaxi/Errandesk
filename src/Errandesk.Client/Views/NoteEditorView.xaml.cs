using Errandesk.Client.ViewModels;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Errandesk.Client.Views;

/// <summary>
/// The editor of the selected note, used as the right pane on wide windows and as a full-width view on narrow ones.
/// </summary>
public sealed partial class NoteEditorView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(NoteEditorViewModel), typeof(NoteEditorView), new PropertyMetadata(null, OnViewModelChanged));

    public NoteEditorView() => InitializeComponent();

    public NoteEditorViewModel? ViewModel
    {
        get => (NoteEditorViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>
    /// Shows a back button instead of the close button, for the narrow layout where the editor replaces the list.
    /// </summary>
    public bool IsBackButtonVisible
    {
        get => NoteBackButton.Visibility == Visibility.Visible;
        set
        {
            NoteBackButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            NoteCloseButton.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public void FocusTitle() => NoteTitleTextBox.Focus(FocusState.Programmatic);

    private static void OnViewModelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) => ((NoteEditorView)dependencyObject).Bindings.Update();

    /// <summary>
    /// Moves from the title to the content on Enter, as the title is a single line.
    /// </summary>
    private void OnNoteTitleTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        if (ViewModel is { IsPreviewVisible: false }) NoteContentTextBox.Focus(FocusState.Keyboard);
    }

    private void OnNoteTagsTextBoxLostFocus(object sender, RoutedEventArgs e) => ViewModel?.NormalizeTagsText();
}
