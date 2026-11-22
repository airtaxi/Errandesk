namespace Errandesk.Client.Sync;

/// <summary>
/// A change made on this device that waits to be sent to the server. Operations are sent in the order they were made.
/// </summary>
/// <param name="Sequence">Position in the outbox; lower values are sent first.</param>
/// <param name="OperationId">Sent as the <c>Idempotency-Key</c> header, so the server applies a repeated send only once.</param>
/// <param name="EntityType">The kind of item the operation changes: <c>list</c>, <c>task</c> or <c>note</c>.</param>
/// <param name="EntityId">ID of the item the operation changes.</param>
/// <param name="Method">HTTP method.</param>
/// <param name="Path">Request path relative to the server address.</param>
/// <param name="Body">JSON request body, or <see langword="null"/> when the request has none.</param>
/// <param name="Attempts">Number of sends that failed so far.</param>
/// <param name="LastError">Why the last send failed.</param>
/// <param name="CreatedAt">When the change was made.</param>
public sealed record OutboxOperation(long Sequence, Guid OperationId, string EntityType, Guid EntityId, string Method, string Path, string? Body, int Attempts, string? LastError, DateTimeOffset CreatedAt);
