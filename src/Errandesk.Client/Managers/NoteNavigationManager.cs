namespace Errandesk.Client.Managers;

/// <summary>
/// Lets views outside the notes page, such as the task details, open a note on the notes page.
/// </summary>
public sealed class NoteNavigationManager
{
    /// <summary>
    /// Raised with the ID of the note to open. The shell handles it by showing the notes page with that note selected.
    /// </summary>
    public event EventHandler<Guid>? NoteOpenRequested;

    public void OpenNote(Guid noteId) => NoteOpenRequested?.Invoke(this, noteId);
}
