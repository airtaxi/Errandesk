namespace Errandesk.Server.Services;

/// <summary>
/// Marks an endpoint whose responses are streamed and therefore never stored or replayed for an <c>Idempotency-Key</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class IdempotencyExemptAttribute : Attribute;
