namespace Errandesk.Server.Services;

/// <summary>
/// The outcome of an idempotent create: the resource and whether this call created it.
/// </summary>
public sealed record CreateResult<TResource>(TResource Resource, bool IsCreated);
