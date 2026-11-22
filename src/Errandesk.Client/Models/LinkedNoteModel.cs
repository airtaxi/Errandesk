using Errandesk.Client.Helpers;
using Errandesk.Contracts;

namespace Errandesk.Client.Models;

/// <summary>
/// A note linked to a task, as listed in the task's details.
/// </summary>
public sealed class LinkedNoteModel(NoteSummaryResponse response)
{
    public Guid Id { get; } = response.Id;

    public string Title { get; } = response.Title;

    public string DisplayTitle => Title.Length > 0 ? Title : LocalizedStrings.Get("UntitledNoteText");

    public bool IsPinned { get; } = response.IsPinned;

    public DateTimeOffset UpdatedAt { get; } = response.UpdatedAt;

    public override string ToString() => DisplayTitle;
}
