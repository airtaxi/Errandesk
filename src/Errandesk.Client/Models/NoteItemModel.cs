using Errandesk.Client.Helpers;
using Errandesk.Contracts;

namespace Errandesk.Client.Models;

/// <summary>
/// A note as shown in the notes list. The values are the ones the server last confirmed; the editor holds the text being typed.
/// </summary>
public sealed partial class NoteItemModel : ObservableObject
{
    private const int ExcerptLength = 200;

    /// <summary>
    /// Creates a new note that exists only on this device until its first save.
    /// </summary>
    public NoteItemModel(Guid id)
    {
        Id = id;
        IsDraft = true;
        CreatedAt = DateTimeOffset.Now;
        UpdatedAt = CreatedAt;
    }

    public NoteItemModel(NoteResponse response)
    {
        Id = response.Id;
        Apply(response);
    }

    public Guid Id { get; }

    /// <summary>
    /// Whether the note has not been created on the server yet.
    /// </summary>
    public bool IsDraft { get; private set; }

    public long ChangeVersion { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<Guid> LinkedTaskIds { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Content { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExcerpt))]
    public partial string Excerpt { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TagsText), nameof(HasTags))]
    public partial IReadOnlyList<string> Tags { get; set; } = [];

    [ObservableProperty]
    public partial bool IsPinned { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatedText))]
    public partial DateTimeOffset UpdatedAt { get; set; }

    public string DisplayTitle => Title.Length > 0 ? Title : LocalizedStrings.Get("UntitledNoteText");

    public bool HasExcerpt => Excerpt.Length > 0;

    public bool HasTags => Tags.Count > 0;

    public string TagsText => string.Join(", ", Tags);

    public string UpdatedText => DateDisplayHelper.FormatShortDate(UpdatedAt);

    /// <summary>
    /// The title, which list items use as their accessible name.
    /// </summary>
    public override string ToString() => DisplayTitle;

    /// <summary>
    /// Copies the values of <paramref name="response"/> unless they are older than the values already shown.
    /// </summary>
    /// <returns><see langword="true"/> when the values were copied.</returns>
    public bool Apply(NoteResponse response)
    {
        if (!IsDraft && response.ChangeVersion < ChangeVersion) return false;

        IsDraft = false;
        ChangeVersion = response.ChangeVersion;
        CreatedAt = response.CreatedAt;
        LinkedTaskIds = response.LinkedTaskIds;
        var isTitleChanged = Title != response.Title;
        Title = response.Title;
        if (isTitleChanged || Content != response.Content || Excerpt.Length == 0)
        {
            Content = response.Content;
            Excerpt = CreateExcerpt(response.Title, response.Content);
        }

        if (!Tags.SequenceEqual(response.Tags)) Tags = response.Tags;
        IsPinned = response.IsPinned;
        UpdatedAt = response.UpdatedAt;
        return true;
    }

    /// <summary>
    /// The plain text of the content, without a leading repetition of the title, as notes often start with their title as a heading.
    /// </summary>
    private static string CreateExcerpt(string title, string content)
    {
        var excerpt = MarkdownText.ToExcerpt(content, ExcerptLength + title.Length);
        if (title.Length > 0 && excerpt.StartsWith(title, StringComparison.CurrentCultureIgnoreCase)) excerpt = excerpt[title.Length..].TrimStart();
        return excerpt.Length > ExcerptLength ? excerpt[..ExcerptLength] : excerpt;
    }
}
