namespace Errandesk.Server.Import;

/// <summary>
/// A step inside a Microsoft To Do task (<c>checklistItem</c>).
/// </summary>
public sealed record GraphChecklistItem(string Id, string? DisplayName, bool? IsChecked, DateTimeOffset? CreatedDateTime);
