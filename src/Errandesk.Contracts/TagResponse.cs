namespace Errandesk.Contracts;

/// <summary>
/// A tag used by the user's notes.
/// </summary>
/// <param name="Name">Normalized tag.</param>
/// <param name="NoteCount">Number of notes that carry the tag.</param>
public sealed record TagResponse(string Name, int NoteCount);
