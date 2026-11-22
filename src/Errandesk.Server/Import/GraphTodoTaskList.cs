namespace Errandesk.Server.Import;

/// <summary>
/// A Microsoft To Do list (<c>todoTaskList</c>).
/// </summary>
/// <param name="WellknownListName"><c>none</c> for lists the user created, <c>defaultList</c> for the built-in Tasks list, <c>flaggedEmails</c> for flagged email.</param>
public sealed record GraphTodoTaskList(string Id, string? DisplayName, string? WellknownListName)
{
    public const string DefaultListName = "defaultList";
    public const string FlaggedEmailsListName = "flaggedEmails";

    public bool IsDefaultList => string.Equals(WellknownListName, DefaultListName, StringComparison.OrdinalIgnoreCase);

    public bool IsFlaggedEmailsList => string.Equals(WellknownListName, FlaggedEmailsListName, StringComparison.OrdinalIgnoreCase);
}
