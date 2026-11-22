namespace Errandesk.Server.Data;

/// <summary>
/// Fields shared by every stored document: identity, ownership, timestamps, the global change version, soft deletion and the schema version.
/// </summary>
public abstract class DocumentBase
{
    public const int CurrentSchemaVersion = 1;

    public Guid Id { get; set; }

    public Guid OwnerUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>Value of the global change counter at the last change.</summary>
    public long ChangeVersion { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Sets the owner, both timestamps and the change version of a new document.</summary>
    public void StampCreated(Guid ownerUserId, DateTime utcNow, long changeVersion)
    {
        OwnerUserId = ownerUserId;
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
        ChangeVersion = changeVersion;
        SchemaVersion = CurrentSchemaVersion;
    }

    /// <summary>Sets the update time and change version after a change.</summary>
    public void StampUpdated(DateTime utcNow, long changeVersion)
    {
        UpdatedAt = utcNow;
        ChangeVersion = changeVersion;
    }
}
