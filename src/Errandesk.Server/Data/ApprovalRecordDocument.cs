namespace Errandesk.Server.Data;

/// <summary>
/// An approval request of a destructive tool call, embedded in the assistant message that carries it.
/// </summary>
public sealed class ApprovalRecordDocument
{
    public string ApprovalId { get; set; } = "";

    public string ToolName { get; set; } = "";

    public string EntityType { get; set; } = "";

    public Guid? EntityId { get; set; }

    public string EntityTitle { get; set; } = "";

    /// <summary>Whether the user must decide. Tool calls that need no confirmation are approved automatically when the user decides the others.</summary>
    public bool RequiresConfirmation { get; set; }

    /// <summary>One of the approval status values: pending, approved or denied.</summary>
    public string Status { get; set; } = "";
}
