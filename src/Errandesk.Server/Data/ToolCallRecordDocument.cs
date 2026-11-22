namespace Errandesk.Server.Data;

/// <summary>
/// A tool invocation of an assistant run, embedded in the message that carries its result.
/// </summary>
public sealed class ToolCallRecordDocument
{
    public string CallId { get; set; } = "";

    public string ToolName { get; set; } = "";

    public bool Succeeded { get; set; }

    /// <summary>The recorded change, or <see langword="null"/> when the tool changed nothing.</summary>
    public Guid? AgentActionId { get; set; }
}
