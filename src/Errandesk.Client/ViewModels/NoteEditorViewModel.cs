using System.Globalization;
using Errandesk.Client.Helpers;
using Errandesk.Client.Managers;
using Errandesk.Client.Models;
using Errandesk.Client.Services;
using Errandesk.Contracts;

namespace Errandesk.Client.ViewModels;

/// <summary>
/// The editor of the selected note: title, Markdown content, tags, pin, preview and deletion.
/// Edits are saved shortly after typing stops, and before another note is shown.
/// </summary>
public sealed partial class NoteEditorViewModel(SyncManager syncManager, DialogService dialogService) : ObservableObject
{
    private static readonly TimeSpan s_autosaveDelay = TimeSpan.FromMilliseconds(800);
    private static readonly char[] s_tagSeparators = [' ', ',', '\t', '\r', '\n'];

    private readonly SyncManager _syncManager = syncManager;
    private readonly DialogService _dialogService = dialogService;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private CancellationTokenSource? _autosaveCancellation;
    private bool _isLoadingNote;
    private bool _hasPendingChanges;

    /// <summary>
    /// Raised after a change to a note was stored on this device.
    /// </summary>
    public event EventHandler<NoteItemModel>? NoteSaved;

    /// <summary>
    /// Raised after a save changed the tags of a note.
    /// </summary>
    public event EventHandler? TagsChanged;

    public event EventHandler<NoteItemModel>? NoteDeleted;

    public event EventHandler? CloseRequested;

    [ObservableProperty]
    public partial NoteItemModel? CurrentNote { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Content { get; set; } = string.Empty;

    /// <summary>
    /// The tags as typed, separated by spaces or commas.
    /// </summary>
    [ObservableProperty]
    public partial string TagsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsPreviewVisible { get; set; }

    /// <summary>
    /// The Markdown shown in the preview. It is only set while the preview is visible, so hidden previews are not rebuilt.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewEmpty))]
    public partial string PreviewMarkdown { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SaveStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsErrorVisible { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public bool IsPreviewEmpty => string.IsNullOrWhiteSpace(PreviewMarkdown);

    /// <summary>
    /// Whether the editor shows no title, content or tags.
    /// </summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Content) && ParseTags(TagsText).Count == 0;

    /// <summary>
    /// Saves edits that are waiting for the autosave delay.
    /// </summary>
    public Task FlushAsync() => TakePendingEdit() is { } edit ? SaveAsync(edit) : Task.CompletedTask;

    /// <summary>
    /// Drops edits that wait for the autosave delay, for a note that no longer exists.
    /// </summary>
    public void DiscardPendingChanges()
    {
        CancelAutosave();
        _hasPendingChanges = false;
    }

    /// <summary>
    /// Shows a newer version of the open note that was saved elsewhere. Ignored while edits are unsaved or a save is running, so typing is never lost.
    /// </summary>
    public void ApplyRemoteChange(NoteResponse response)
    {
        if (CurrentNote is not { IsDraft: false } note || _hasPendingChanges || _saveGate.CurrentCount == 0 || response.ChangeVersion <= note.ChangeVersion) return;

        note.Apply(response);
        _isLoadingNote = true;
        Title = note.Title;
        Content = note.Content;
        TagsText = string.Join(' ', note.Tags);
        _isLoadingNote = false;
        UpdatePreview();
    }

    /// <summary>
    /// Shows the tags in the form they are stored in, after the tags box lost focus.
    /// </summary>
    public void NormalizeTagsText()
    {
        var normalizedText = string.Join(' ', ParseTags(TagsText));
        if (normalizedText != TagsText) TagsText = normalizedText;
    }

    partial void OnCurrentNoteChanging(NoteItemModel? value)
    {
        if (TakePendingEdit() is { } edit)
        {
            _ = SaveAsync(edit);
        }
    }

    partial void OnCurrentNoteChanged(NoteItemModel? value)
    {
        _isLoadingNote = true;
        Title = value?.Title ?? string.Empty;
        Content = value?.Content ?? string.Empty;
        TagsText = value is null ? string.Empty : string.Join(' ', value.Tags);
        _isLoadingNote = false;
        _hasPendingChanges = false;
        SaveStatusText = string.Empty;
        IsErrorVisible = false;
        if (value is { IsDraft: true }) IsPreviewVisible = false;
        UpdatePreview();
    }

    partial void OnTitleChanged(string value) => OnTextEdited();

    partial void OnContentChanged(string value) => OnTextEdited();

    partial void OnTagsTextChanged(string value) => OnTextEdited();

    partial void OnIsPreviewVisibleChanged(bool value) => UpdatePreview();

    [RelayCommand]
    private async Task TogglePinAsync()
    {
        if (CurrentNote is not { } note) return;
        if (note.IsDraft)
        {
            note.IsPinned = !note.IsPinned;
            _hasPendingChanges = true;
            await FlushAsync();
            NoteSaved?.Invoke(this, note);
            return;
        }

        await FlushAsync();
        var request = new UpdateNoteRequest { IsPinned = !note.IsPinned };
        await _saveGate.WaitAsync();
        try { await RunSaveOperationAsync(note, () => Task.FromResult(_syncManager.Data.UpdateNote(note.Id, request))); }
        finally { _saveGate.Release(); }
    }

    [RelayCommand]
    private async Task DeleteNoteAsync()
    {
        if (CurrentNote is not { } note) return;
        if (!note.IsDraft || !IsEmpty)
        {
            var title = Title.Trim() is { Length: > 0 } trimmedTitle ? trimmedTitle : LocalizedStrings.Get("UntitledNoteText");
            var message = string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("DeleteNoteDialogMessage"), title);
            if (!await _dialogService.ConfirmAsync(LocalizedStrings.Get("DeleteNoteDialogTitle"), message, LocalizedStrings.Get("DeleteButtonText"))) return;
        }

        CancelAutosave();
        _hasPendingChanges = false;
        IsErrorVisible = false;
        await _saveGate.WaitAsync();
        try
        {
            if (!note.IsDraft) _syncManager.Data.DeleteNote(note.Id);
            NoteDeleted?.Invoke(this, note);
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(exception); }
        finally { _saveGate.Release(); }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnTextEdited()
    {
        if (_isLoadingNote || CurrentNote is null) return;

        _hasPendingChanges = true;
        SaveStatusText = string.Empty;
        CancelAutosave();
        _autosaveCancellation = new CancellationTokenSource();
        _ = SaveAfterDelayAsync(_autosaveCancellation.Token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try { await Task.Delay(s_autosaveDelay, cancellationToken); }
        catch (OperationCanceledException) { return; }
        await FlushAsync();
    }

    private void CancelAutosave()
    {
        _autosaveCancellation?.Cancel();
        _autosaveCancellation?.Dispose();
        _autosaveCancellation = null;
    }

    private NoteEdit? TakePendingEdit()
    {
        CancelAutosave();
        if (!_hasPendingChanges || CurrentNote is not { } note) return null;

        _hasPendingChanges = false;
        return new NoteEdit(note, Title.Trim(), MarkdownText.NormalizeLineEndings(Content), ParseTags(TagsText));
    }

    private async Task SaveAsync(NoteEdit edit)
    {
        await _saveGate.WaitAsync();
        try
        {
            if ((edit.Note.IsDraft ? CreateDraftOperation(edit) : CreateUpdateOperation(edit)) is { } operation)
            {
                await RunSaveOperationAsync(edit.Note, operation);
            }
        }
        finally { _saveGate.Release(); }
    }

    private Func<Task<NoteResponse>>? CreateDraftOperation(NoteEdit edit)
    {
        if (edit.IsEmpty && !edit.Note.IsPinned) return null;
        var request = new CreateNoteRequest(edit.Title, edit.Content, edit.Note.Id, edit.Tags, edit.Note.IsPinned);
        return () => Task.FromResult(_syncManager.Data.CreateNote(request));
    }

    /// <summary>
    /// A PATCH with only the values that differ from the stored note, or <see langword="null"/> when nothing changed.
    /// </summary>
    private Func<Task<NoteResponse>>? CreateUpdateOperation(NoteEdit edit)
    {
        var note = edit.Note;
        var request = new UpdateNoteRequest();
        var hasChanges = false;
        if (edit.Title != note.Title)
        {
            request = request with { Title = edit.Title };
            hasChanges = true;
        }

        if (edit.Content != MarkdownText.NormalizeLineEndings(note.Content))
        {
            request = request with { Content = edit.Content };
            hasChanges = true;
        }

        if (!edit.Tags.SequenceEqual(note.Tags))
        {
            request = request with { Tags = new Optional<IReadOnlyList<string>?>(edit.Tags) };
            hasChanges = true;
        }

        return hasChanges ? () => Task.FromResult(_syncManager.Data.UpdateNote(note.Id, request)) : null;
    }

    private async Task RunSaveOperationAsync(NoteItemModel note, Func<Task<NoteResponse>> operation)
    {
        if (ReferenceEquals(CurrentNote, note)) SaveStatusText = LocalizedStrings.Get("NoteSavingText");
        try
        {
            var response = await operation();
            var areTagsChanged = !note.Tags.SequenceEqual(response.Tags);
            note.Apply(response);
            NoteSaved?.Invoke(this, note);
            if (areTagsChanged) TagsChanged?.Invoke(this, EventArgs.Empty);
            if (ReferenceEquals(CurrentNote, note) && !_hasPendingChanges) SaveStatusText = LocalizedStrings.Get("NoteSavedText");
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception))
        {
            if (ReferenceEquals(CurrentNote, note))
            {
                _hasPendingChanges = true;
                SaveStatusText = LocalizedStrings.Get("NoteNotSavedText");
            }

            ShowError(exception);
        }
    }

    private void UpdatePreview() => PreviewMarkdown = IsPreviewVisible ? Content : string.Empty;

    private void ShowError(Exception exception)
    {
        if (ApiErrorMessages.GetMessage(exception) is not { } message) return;
        ErrorMessage = message;
        IsErrorVisible = true;
    }

    private static List<string> ParseTags(string text) =>
    [.. text.Split(s_tagSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(tag => tag.TrimStart('#').ToLowerInvariant()).Where(tag => tag.Length > 0).Distinct()];

    /// <summary>
    /// The editor's values for a note at the moment a save was started.
    /// </summary>
    private sealed record NoteEdit(NoteItemModel Note, string Title, string Content, IReadOnlyList<string> Tags)
    {
        public bool IsEmpty => Title.Length == 0 && string.IsNullOrWhiteSpace(Content) && Tags.Count == 0;
    }
}
