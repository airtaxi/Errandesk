namespace Errandesk.Contracts;

/// <summary>
/// A change the assistant made through a tool.
/// </summary>
/// <param name="Id">Agent action ID.</param>
/// <param name="ConversationId">Conversation in which the change was made.</param>
/// <param name="ToolName">Name of the tool.</param>
/// <param name="EntityType">Type of the changed item, one of <see cref="EntityTypes"/>.</param>
/// <param name="EntityId">ID of the changed item.</param>
/// <param name="CreatedAt">When the change was made.</param>
/// <param name="UndoneAt">When the change was undone, or <see langword="null"/>.</param>
public sealed record AgentActionResponse(Guid Id, Guid? ConversationId, string ToolName, string EntityType, Guid EntityId, DateTimeOffset CreatedAt, DateTimeOffset? UndoneAt);
