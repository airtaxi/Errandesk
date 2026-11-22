namespace Errandesk.Server.Data;

/// <summary>
/// A step embedded in a <see cref="TaskDocument"/>.
/// </summary>
public sealed class TaskStepDocument
{
    public Guid Id { get; set; }

    public string Title { get; set; } = "";

    public bool IsCompleted { get; set; }

    public double SortOrder { get; set; }
}
