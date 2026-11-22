namespace Errandesk.Server.Data;

/// <summary>
/// A change made by an assistant tool, with snapshots of every affected document so it can be undone.
/// </summary>
public sealed class AgentActionDocument : DocumentBase
{
    public Guid? ConversationId { get; set; }

    public string ToolName { get; set; } = "";

    /// <summary>The tool arguments as JSON.</summary>
    public string ArgumentsJson { get; set; } = "{}";

    /// <summary>Type of the primary affected item.</summary>
    public string EntityType { get; set; } = "";

    /// <summary>ID of the primary affected item.</summary>
    public Guid EntityId { get; set; }

    /// <summary>Every affected document, the primary item first.</summary>
    public List<AgentActionEntityDocument> Entities { get; set; } = [];

    public DateTime? UndoneAt { get; set; }
}
