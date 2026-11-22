using Errandesk.Server.Data;
using MongoDB.Driver;

namespace Errandesk.Server.Services;

/// <summary>
/// Stores processed idempotency keys per user in <c>idempotency_keys</c>, so a repeated request gets the stored response instead of running again.
/// A key is claimed by inserting a record before the request runs; the record receives the response when the request completes, or is removed when the response must not be stored.
/// </summary>
public sealed class IdempotencyKeyStore(ErrandeskDatabase database, DocumentUpdater documentUpdater)
{
    /// <summary>How long a key and its response are kept.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    /// <summary>A claim older than this without a stored response is treated as abandoned, for example after a crash, and can be taken over.</summary>
    public static readonly TimeSpan ProcessingTimeout = TimeSpan.FromMinutes(1);

    private const int MaximumAttempts = 5;
    private const string IdIndexName = "_id_";

    public async Task<IdempotencyClaim> ClaimAsync(Guid userId, string key, string requestFingerprint, CancellationToken cancellationToken)
    {
        var identifier = IdempotencyKeyDocument.CreateId(userId, key);
        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            var utcNow = documentUpdater.GetUtcNow();
            var claimId = Guid.CreateVersion7();
            try
            {
                await database.IdempotencyKeys.InsertOneAsync(new IdempotencyKeyDocument { Id = identifier, OwnerUserId = userId, Key = key, RequestFingerprint = requestFingerprint, ClaimId = claimId, ClaimedAt = utcNow, CreatedAt = utcNow }, cancellationToken: cancellationToken);
                return new IdempotencyClaim(IdempotencyClaimOutcome.Started, claimId);
            }
            catch (MongoWriteException exception) when (exception.IsDuplicateKey(IdIndexName)) { }

            var existing = await database.IdempotencyKeys.Find(document => document.Id == identifier).FirstOrDefaultAsync(cancellationToken);
            if (existing is null) continue;

            if (existing.CreatedAt < utcNow - Retention)
            {
                await database.IdempotencyKeys.DeleteOneAsync(document => document.Id == identifier && document.ClaimId == existing.ClaimId, cancellationToken);
                continue;
            }

            if (existing.RequestFingerprint != requestFingerprint) return new IdempotencyClaim(IdempotencyClaimOutcome.Mismatch);
            if (existing.CompletedAt is not null) return new IdempotencyClaim(IdempotencyClaimOutcome.Replay, StoredResponse: existing);
            if (existing.ClaimedAt >= utcNow - ProcessingTimeout) return new IdempotencyClaim(IdempotencyClaimOutcome.InProgress);

            var takeOverUpdate = Builders<IdempotencyKeyDocument>.Update.Set(document => document.ClaimId, claimId).Set(document => document.ClaimedAt, utcNow);
            var takenOver = await database.IdempotencyKeys.FindOneAndUpdateAsync(document => document.Id == identifier && document.ClaimId == existing.ClaimId && document.CompletedAt == null, takeOverUpdate, cancellationToken: cancellationToken);
            if (takenOver is not null) return new IdempotencyClaim(IdempotencyClaimOutcome.Started, claimId);
        }
        return new IdempotencyClaim(IdempotencyClaimOutcome.InProgress);
    }

    /// <summary>Stores the response of the claimed request. Does nothing when the claim was taken over in the meantime.</summary>
    public Task CompleteAsync(Guid userId, string key, Guid claimId, int statusCode, string? contentType, string? location, byte[] responseBody, CancellationToken cancellationToken)
    {
        var identifier = IdempotencyKeyDocument.CreateId(userId, key);
        var update = Builders<IdempotencyKeyDocument>.Update.Set(document => document.CompletedAt, documentUpdater.GetUtcNow()).Set(document => document.StatusCode, statusCode).Set(document => document.ContentType, contentType).Set(document => document.Location, location).Set(document => document.ResponseBody, responseBody);
        return database.IdempotencyKeys.UpdateOneAsync(document => document.Id == identifier && document.ClaimId == claimId, update, cancellationToken: cancellationToken);
    }

    /// <summary>Removes the claim without a stored response, so the next request with the key runs again.</summary>
    public Task ReleaseAsync(Guid userId, string key, Guid claimId, CancellationToken cancellationToken)
    {
        var identifier = IdempotencyKeyDocument.CreateId(userId, key);
        return database.IdempotencyKeys.DeleteOneAsync(document => document.Id == identifier && document.ClaimId == claimId && document.CompletedAt == null, cancellationToken);
    }
}
