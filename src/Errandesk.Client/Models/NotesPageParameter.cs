namespace Errandesk.Client.Models;

/// <summary>
/// Opens the notes page, optionally with a note selected.
/// </summary>
/// <param name="NoteId">The note to open, or <see langword="null"/> to show the list only.</param>
public sealed record NotesPageParameter(Guid? NoteId = null);
