namespace Errandesk.Contracts;

/// <summary>
/// A short form of a note, included in task responses for the notes linked to the task.
/// </summary>
/// <param name="Id">Note ID.</param>
/// <param name="Title">Note title. Empty when the note has none.</param>
/// <param name="Excerpt">The beginning of the Markdown content with runs of white space collapsed into single spaces.</param>
/// <param name="Tags">Normalized tags.</param>
/// <param name="IsPinned">Whether the note is pinned.</param>
/// <param name="UpdatedAt">When the note was last changed.</param>
public sealed record NoteSummaryResponse(Guid Id, string Title, string Excerpt, IReadOnlyList<string> Tags, bool IsPinned, DateTimeOffset UpdatedAt);
