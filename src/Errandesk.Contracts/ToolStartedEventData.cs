namespace Errandesk.Contracts;

/// <summary>
/// Data of the <see cref="ServerEventNames.ToolStarted"/> event.
/// </summary>
/// <param name="CallId">ID of the tool call, repeated in <see cref="ToolCompletedEventData"/>.</param>
/// <param name="ToolName">Name of the tool, such as <c>create_task</c>.</param>
public sealed record ToolStartedEventData(string CallId, string ToolName);
