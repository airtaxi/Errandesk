namespace Errandesk.Contracts;

/// <summary>
/// Creates a note.
/// </summary>
/// <param name="Title">Note title. May be empty.</param>
/// <param name="Content">Markdown content. May be empty.</param>
/// <param name="Id">Client-generated ID. Creating an ID that already exists for the same user returns the existing note.</param>
/// <param name="Tags">Tags. A leading <c>#</c> is removed, tags are stored in lower case, and duplicates are dropped.</param>
/// <param name="IsPinned">Whether the note starts pinned.</param>
/// <param name="LinkedTaskIds">Tasks of the user to link the note to.</param>
public sealed record CreateNoteRequest(string? Title = null, string? Content = null, Guid? Id = null, IReadOnlyList<string>? Tags = null, bool IsPinned = false, IReadOnlyList<Guid>? LinkedTaskIds = null);
