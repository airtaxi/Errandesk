namespace Errandesk.Client.Sync;

/// <summary>
/// An item whose server state must be read again, because the server rejected a change to it or a change from the server was held back while local changes were pending.
/// </summary>
/// <param name="EntityType">The kind of item: <c>list</c>, <c>task</c> or <c>note</c>.</param>
/// <param name="EntityId">ID of the item.</param>
public sealed record RefreshItem(string EntityType, Guid EntityId);
