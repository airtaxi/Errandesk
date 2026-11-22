using System.Collections.Specialized;
using System.ComponentModel;
using Errandesk.Client.Managers;
using Errandesk.Client.Models;
using Errandesk.Client.ViewModels;
using Errandesk.Contracts;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace Errandesk.Client.Pages;

/// <summary>
/// The notes list and the editor of the selected note. On wide windows the editor is a pane on the right; on narrow windows it replaces the list.
/// </summary>
public sealed partial class NotesPage : Page
{
    private const double WideLayoutMinimumWidth = 640;
    private const double ListPaneWidth = 360;

    private readonly SyncManager _syncManager = App.GetService<SyncManager>();

    public NotesPage() => InitializeComponent();

    public NotesViewModel ViewModel { get; } = App.GetService<NotesViewModel>();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Load((e.Parameter as NotesPageParameter)?.NoteId);
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        await ViewModel.SaveAsync();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Notes.CollectionChanged += OnNotesCollectionChanged;
        _syncManager.DataChanged += OnSyncManagerDataChanged;
        SyncListSelection();
        UpdatePaneLayout();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Notes.CollectionChanged -= OnNotesCollectionChanged;
        _syncManager.DataChanged -= OnSyncManagerDataChanged;
    }

    private void OnSyncManagerDataChanged(object? _, ChangesResponse changes) => ViewModel.ApplyChanges(changes);

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) => UpdatePaneLayout();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NotesViewModel.SelectedNote)) return;
        SyncListSelection();
        UpdatePaneLayout();
    }

    /// <summary>
    /// Keeps the selected note selected in the list when the list is reloaded or a note moves.
    /// </summary>
    private void OnNotesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => DispatcherQueue.TryEnqueue(SyncListSelection);

    private void SyncListSelection()
    {
        var selectedNote = ViewModel.SelectedNote;
        if (!ReferenceEquals(NotesListView.SelectedItem, selectedNote)) NotesListView.SelectedItem = selectedNote is not null && ViewModel.Notes.Contains(selectedNote) ? selectedNote : null;
    }

    private void OnNotesListViewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NotesListView.SelectedItem is NoteItemModel note && !ReferenceEquals(ViewModel.SelectedNote, note))
        {
            ViewModel.SelectedNote = note;
        }
    }

    private void OnNewNoteButtonClicked(object sender, RoutedEventArgs e) => CreateNote();

    private void OnNewNoteKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CreateNote();
    }

    private void CreateNote()
    {
        ViewModel.NewNoteCommand.Execute(null);
        DispatcherQueue.TryEnqueue(NoteEditorView.FocusTitle);
    }

    /// <summary>
    /// Whether the page itself is wide enough for the list and the editor side by side. The page narrows when the assistant pane is docked next to it.
    /// </summary>
    private bool IsWideLayout() => ActualWidth >= WideLayoutMinimumWidth;

    private void UpdatePaneLayout()
    {
        var isWideLayout = IsWideLayout();
        var isNoteOpen = ViewModel.SelectedNote is not null;
        var isListVisible = isWideLayout || !isNoteOpen;
        var isEditorVisible = isWideLayout || isNoteOpen;

        ListPanel.Visibility = isListVisible ? Visibility.Visible : Visibility.Collapsed;
        ListColumn.Width = !isListVisible ? new GridLength(0) : isWideLayout ? new GridLength(ListPaneWidth) : new GridLength(1, GridUnitType.Star);
        EditorPanel.Visibility = isEditorVisible ? Visibility.Visible : Visibility.Collapsed;
        EditorPanel.BorderThickness = isWideLayout ? new Thickness(1, 0, 0, 0) : new Thickness(0);
        EditorColumn.Width = isEditorVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        NoteEditorView.Visibility = isNoteOpen ? Visibility.Visible : Visibility.Collapsed;
        NoNoteSelectedTextBlock.Visibility = isNoteOpen ? Visibility.Collapsed : Visibility.Visible;
        NoteEditorView.IsBackButtonVisible = !isWideLayout;
    }
}
