namespace Errandesk.Client.Sync;

/// <summary>
/// A change refers to a list, task or note that is not stored on this device, for example because it was deleted elsewhere.
/// </summary>
public sealed class LocalItemNotFoundException(string entityType, Guid id) : Exception($"The {entityType} {id} is not stored on this device.")
{
    /// <summary>The kind of item: <c>list</c>, <c>task</c> or <c>note</c>.</summary>
    public string EntityType { get; } = entityType;

    public Guid Id { get; } = id;
}
