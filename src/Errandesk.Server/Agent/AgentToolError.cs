namespace Errandesk.Server.Agent;

/// <summary>
/// The result a tool returns to the model when the request was invalid or the item was not found.
/// </summary>
/// <param name="Error">What went wrong.</param>
public sealed record AgentToolError(string Error);
