namespace Errandesk.Server.Tests.Data;

public sealed class SampleDocument
{
    public Guid Id { get; set; }

    public DateOnly DueDate { get; set; }

    public SamplePriority Priority { get; set; }
}
