namespace Errandesk.Server.Agent;

/// <summary>
/// What a tool returns to the model, and the recorded change when it changed data.
/// </summary>
public sealed record AgentToolOutcome(object Result, Guid? AgentActionId = null);
