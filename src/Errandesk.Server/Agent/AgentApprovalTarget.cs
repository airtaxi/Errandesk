namespace Errandesk.Server.Agent;

/// <summary>
/// The item a destructive tool call would affect.
/// </summary>
/// <param name="EntityType">Type of the item.</param>
/// <param name="EntityId">ID of the item, or <see langword="null"/> when the call named no valid ID.</param>
/// <param name="EntityTitle">Title or name of the item, empty when it was not found.</param>
public sealed record AgentApprovalTarget(string EntityType, Guid? EntityId, string EntityTitle);
