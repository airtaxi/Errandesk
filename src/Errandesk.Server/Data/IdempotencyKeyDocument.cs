namespace Errandesk.Server.Data;

/// <summary>
/// A processed or in-progress request identified by the client's <c>Idempotency-Key</c>, with the stored response once it completed.
/// Removed by a TTL index on <see cref="CreatedAt"/>.
/// </summary>
public sealed class IdempotencyKeyDocument
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>The owner's user ID and the key, see <see cref="CreateId"/>.</summary>
    public string Id { get; set; } = "";

    public Guid OwnerUserId { get; set; }

    public string Key { get; set; } = "";

    /// <summary>SHA-256 of the method, path, query and body of the first request that used the key.</summary>
    public string RequestFingerprint { get; set; } = "";

    /// <summary>Identifies the request currently processing the key, so only that request completes or releases it.</summary>
    public Guid ClaimId { get; set; }

    public DateTime ClaimedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Set once the response is stored; <see langword="null"/> while the request is still being processed.</summary>
    public DateTime? CompletedAt { get; set; }

    public int StatusCode { get; set; }

    public string? ContentType { get; set; }

    public string? Location { get; set; }

    public byte[] ResponseBody { get; set; } = [];

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public static string CreateId(Guid ownerUserId, string key) => $"{ownerUserId:N}:{key}";
}
