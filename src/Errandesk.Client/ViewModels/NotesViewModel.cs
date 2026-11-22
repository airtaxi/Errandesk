using System.Collections.ObjectModel;
using System.Globalization;
using Errandesk.Client.Helpers;
using Errandesk.Client.Managers;
using Errandesk.Client.Models;
using Errandesk.Client.Services;
using Errandesk.Contracts;

namespace Errandesk.Client.ViewModels;

/// <summary>
/// The notes page: the searchable list of notes with a tag filter, pinned notes first, and the editor of the selected note.
/// </summary>
public sealed partial class NotesViewModel : ObservableObject
{
    private static readonly TimeSpan s_searchDelay = TimeSpan.FromMilliseconds(300);

    private readonly SyncManager _syncManager;
    private readonly List<string?> _tagFilterValues = [null];
    private CancellationTokenSource? _searchCancellation;
    private bool _isUpdatingTagFilter;

    public NotesViewModel(SyncManager syncManager, NoteEditorViewModel editor)
    {
        _syncManager = syncManager;
        Editor = editor;
        Editor.NoteSaved += OnEditorNoteSaved;
        Editor.TagsChanged += OnEditorTagsChanged;
        Editor.NoteDeleted += OnEditorNoteDeleted;
        Editor.CloseRequested += OnEditorCloseRequested;
        TagFilterOptions.Add(LocalizedStrings.Get("AllTagsFilterText"));
    }

    public NoteEditorViewModel Editor { get; }

    public ObservableCollection<NoteItemModel> Notes { get; } = [];

    /// <summary>
    /// The choices of the tag filter: all notes first, then each tag with its note count.
    /// </summary>
    public ObservableCollection<string> TagFilterOptions { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedTagFilterIndex { get; set; }

    [ObservableProperty]
    public partial NoteItemModel? SelectedNote { get; set; }

    [ObservableProperty]
    public partial bool IsEmptyStateVisible { get; set; }

    [ObservableProperty]
    public partial string EmptyStateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsErrorVisible { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    private bool IsFiltered => !string.IsNullOrWhiteSpace(SearchText) || SelectedTagFilterIndex > 0;

    /// <summary>
    /// Shows the notes and tags stored on this device, then selects the note with <paramref name="noteIdToOpen"/> when given.
    /// </summary>
    public void Load(Guid? noteIdToOpen)
    {
        LoadNotes();
        LoadTags();
        if (noteIdToOpen is not { } noteId) return;

        var note = Notes.FirstOrDefault(note => note.Id == noteId);
        if (note is null)
        {
            if (_syncManager.Data.GetNote(noteId) is not { } response)
            {
                ErrorMessage = LocalizedStrings.Get("ItemNotFoundError");
                IsErrorVisible = true;
                return;
            }

            note = new NoteItemModel(response);
            Notes.Insert(0, note);
            UpdateEmptyState();
        }

        SelectedNote = note;
    }

    /// <summary>
    /// Applies notes changed or deleted on this device or elsewhere. New notes are added when no search or tag filter is active;
    /// the note open in the editor takes the change only when it has no unsaved edits. A replaced data set loads the list again.
    /// </summary>
    public void ApplyChanges(ChangesResponse changes)
    {
        if (changes.ResetRequired)
        {
            if (SelectedNote is { IsDraft: false } selectedNote && _syncManager.Data.GetNote(selectedNote.Id) is null)
            {
                Editor.DiscardPendingChanges();
                SelectedNote = null;
            }

            LoadNotes();
            LoadTags();
            return;
        }

        if (changes.Notes.Count == 0 && changes.DeletedNotes.Count == 0) return;

        var isListChanged = false;
        foreach (var tombstone in changes.DeletedNotes)
        {
            if (Notes.FirstOrDefault(note => note.Id == tombstone.Id) is not { } deletedNote) continue;
            if (ReferenceEquals(deletedNote, SelectedNote))
            {
                Editor.DiscardPendingChanges();
                SelectedNote = null;
            }

            Notes.Remove(deletedNote);
            isListChanged = true;
        }

        foreach (var response in changes.Notes)
        {
            var note = Notes.FirstOrDefault(note => note.Id == response.Id);
            if (note is null)
            {
                if (IsFiltered) continue;
                InsertNote(new NoteItemModel(response));
                isListChanged = true;
            }
            else if (ReferenceEquals(note, SelectedNote)) Editor.ApplyRemoteChange(response);
            else if (note.Apply(response)) isListChanged = true;
        }

        if (!isListChanged) return;
        UpdateEmptyState();
        LoadTags();
    }

    /// <summary>
    /// Saves pending edits before the page is left.
    /// </summary>
    public Task SaveAsync() => Editor.FlushAsync();

    partial void OnSelectedNoteChanged(NoteItemModel? oldValue, NoteItemModel? newValue)
    {
        var abandonedDraft = oldValue is { IsDraft: true, IsPinned: false } && Editor.IsEmpty ? oldValue : null;
        Editor.CurrentNote = newValue;
        if (abandonedDraft is null) return;

        Notes.Remove(abandonedDraft);
        UpdateEmptyState();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        _ = LoadAfterDelayAsync(_searchCancellation.Token);
    }

    partial void OnSelectedTagFilterIndexChanged(int value)
    {
        if (!_isUpdatingTagFilter && value >= 0)
        {
            LoadNotes();
        }
    }

    [RelayCommand]
    private void NewNote()
    {
        var note = new NoteItemModel(Guid.CreateVersion7());
        Notes.Insert(0, note);
        UpdateEmptyState();
        SelectedNote = note;
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadNotes();
        LoadTags();
    }

    private async Task LoadAfterDelayAsync(CancellationToken cancellationToken)
    {
        try { await Task.Delay(s_searchDelay, cancellationToken); }
        catch (OperationCanceledException) { return; }
        LoadNotes();
    }

    private void LoadNotes()
    {
        var tag = SelectedTagFilterIndex > 0 && SelectedTagFilterIndex < _tagFilterValues.Count ? _tagFilterValues[SelectedTagFilterIndex] : null;
        IsErrorVisible = false;
        try
        {
            var responses = _syncManager.Data.GetNotes(SearchText, tag, null);
            var selectedNote = SelectedNote;
            Notes.Clear();
            foreach (var response in responses)
            {
                if (selectedNote?.Id == response.Id)
                {
                    selectedNote.Apply(response);
                    Notes.Add(selectedNote);
                }
                else Notes.Add(new NoteItemModel(response));
            }

            if (selectedNote is { IsDraft: true }) Notes.Insert(0, selectedNote);
        }
        catch (InvalidOperationException) { }
        finally { UpdateEmptyState(); }
    }

    private void LoadTags()
    {
        try
        {
            var tags = _syncManager.Data.GetTags();
            var selectedTag = _tagFilterValues[Math.Clamp(SelectedTagFilterIndex, 0, _tagFilterValues.Count - 1)];

            _isUpdatingTagFilter = true;
            try
            {
                while (TagFilterOptions.Count > 1) TagFilterOptions.RemoveAt(TagFilterOptions.Count - 1);
                _tagFilterValues.RemoveRange(1, _tagFilterValues.Count - 1);
                foreach (var tag in tags)
                {
                    TagFilterOptions.Add(string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("TagFilterOptionFormat"), tag.Name, tag.NoteCount));
                    _tagFilterValues.Add(tag.Name);
                }

                SelectedTagFilterIndex = Math.Max(0, _tagFilterValues.IndexOf(selectedTag));
            }
            finally { _isUpdatingTagFilter = false; }

            if (selectedTag is not null && SelectedTagFilterIndex == 0) LoadNotes();
        }
        catch (InvalidOperationException) { }
    }

    /// <summary>
    /// Keeps pinned notes above the others after a note was pinned or unpinned. Other notes keep their place until the list reloads.
    /// </summary>
    private void OnEditorNoteSaved(object? _, NoteItemModel note)
    {
        var index = Notes.IndexOf(note);
        if (index < 0) return;

        var lastPinnedIndex = -1;
        for (var otherIndex = 0; otherIndex < Notes.Count; otherIndex++)
        {
            if (otherIndex != index && Notes[otherIndex].IsPinned)
            {
                lastPinnedIndex = otherIndex;
            }
        }

        if (note.IsPinned && Notes.Take(index).Any(other => !other.IsPinned)) Notes.Move(index, 0);
        else if (!note.IsPinned && lastPinnedIndex > index) Notes.Move(index, lastPinnedIndex);
    }

    /// <summary>
    /// Adds a note at the top of its group: pinned notes first, then the others.
    /// </summary>
    private void InsertNote(NoteItemModel note)
    {
        var index = 0;
        if (!note.IsPinned)
        {
            while (index < Notes.Count && (Notes[index].IsPinned || Notes[index].IsDraft))
            {
                index++;
            }
        }

        Notes.Insert(index, note);
    }

    private void OnEditorTagsChanged(object? _, EventArgs __) => LoadTags();

    private void OnEditorNoteDeleted(object? _, NoteItemModel note)
    {
        SelectedNote = null;
        Notes.Remove(note);
        UpdateEmptyState();
        LoadTags();
    }

    private void OnEditorCloseRequested(object? _, EventArgs __) => SelectedNote = null;

    private void UpdateEmptyState()
    {
        IsEmptyStateVisible = !IsErrorVisible && Notes.Count == 0;
        EmptyStateText = LocalizedStrings.Get(IsFiltered ? "NoMatchingNotesText" : "EmptyNotesText");
    }
}
