namespace Errandesk.Server.Agent;

/// <summary>
/// The result of a delete tool.
/// </summary>
/// <param name="Deleted">Type of the deleted item.</param>
/// <param name="Id">ID of the deleted item.</param>
/// <param name="Title">Title or name of the deleted item.</param>
public sealed record AgentDeletionResult(string Deleted, Guid Id, string Title);
