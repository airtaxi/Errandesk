namespace Errandesk.Server.Data;

/// <summary>
/// A Markdown note. Links between notes and tasks are stored only here.
/// </summary>
public sealed class NoteDocument : DocumentBase
{
    public string Title { get; set; } = "";

    /// <summary>Markdown content, empty when there is none.</summary>
    public string Content { get; set; } = "";

    /// <summary>Normalized tags: lower case, without a leading <c>#</c>, without duplicates.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>IDs of the linked tasks, including tasks deleted after they were linked.</summary>
    public List<Guid> LinkedTaskIds { get; set; } = [];

    public bool IsPinned { get; set; }
}
