namespace Errandesk.Server.Data;

/// <summary>
/// The My Day date of a task for a user who is not the task's owner.
/// </summary>
public sealed class TaskMyDayDocument
{
    public Guid UserId { get; set; }

    public DateOnly Date { get; set; }
}
