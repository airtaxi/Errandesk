namespace Errandesk.Contracts;

/// <summary>
/// Data of the <see cref="ServerEventNames.ToolCompleted"/> event.
/// </summary>
/// <param name="CallId">ID of the tool call.</param>
/// <param name="ToolName">Name of the tool.</param>
/// <param name="Succeeded">Whether the tool finished without an error.</param>
/// <param name="AgentActionId">The recorded change that can be undone, or <see langword="null"/> when the tool changed nothing.</param>
/// <param name="Error">Why the tool failed, or <see langword="null"/> when it succeeded.</param>
public sealed record ToolCompletedEventData(string CallId, string ToolName, bool Succeeded, Guid? AgentActionId, string? Error);
