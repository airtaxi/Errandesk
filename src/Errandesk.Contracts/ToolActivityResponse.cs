namespace Errandesk.Contracts;

/// <summary>
/// A tool the assistant ran while writing a message.
/// </summary>
/// <param name="CallId">ID of the tool call.</param>
/// <param name="ToolName">Name of the tool.</param>
/// <param name="Succeeded">Whether the tool finished without an error.</param>
/// <param name="AgentActionId">The recorded change that can be undone, or <see langword="null"/> when the tool changed nothing.</param>
/// <param name="IsUndone">Whether the recorded change was undone.</param>
public sealed record ToolActivityResponse(string CallId, string ToolName, bool Succeeded, Guid? AgentActionId, bool IsUndone);
