namespace Errandesk.Contracts;

/// <summary>
/// A Markdown note.
/// </summary>
/// <param name="Id">Note ID.</param>
/// <param name="Title">Note title. Empty when the note has none.</param>
/// <param name="Content">Markdown content.</param>
/// <param name="Tags">Normalized tags: lower case, without a leading <c>#</c>.</param>
/// <param name="LinkedTaskIds">Tasks the note is linked to, limited to tasks that still exist.</param>
/// <param name="IsPinned">Whether the note is pinned, which lists it before unpinned notes.</param>
/// <param name="CreatedAt">When the note was created.</param>
/// <param name="UpdatedAt">When the note was last changed.</param>
/// <param name="ChangeVersion">Global change version of the last change.</param>
public sealed record NoteResponse(Guid Id, string Title, string Content, IReadOnlyList<string> Tags, IReadOnlyList<Guid> LinkedTaskIds, bool IsPinned, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long ChangeVersion);
