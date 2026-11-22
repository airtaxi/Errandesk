namespace Errandesk.Server.Import;

/// <summary>
/// The repeat rule of a Microsoft To Do task (<c>patternedRecurrence</c>). Only the pattern is read; the range is not.
/// </summary>
public sealed record GraphPatternedRecurrence(GraphRecurrencePattern? Pattern);
