namespace Errandesk.Server.Agent;

/// <summary>
/// A recently changed note as the morning briefing request describes it to the model.
/// </summary>
/// <param name="Title">Title of the note.</param>
/// <param name="Tags">Tags of the note.</param>
/// <param name="Excerpt">The beginning of the note's Markdown content.</param>
/// <param name="ChangedAt">Local date and time of the last change as <c>yyyy-MM-dd HH:mm</c>.</param>
public sealed record MorningBriefingNoteData(string Title, IReadOnlyList<string> Tags, string Excerpt, string ChangedAt);
