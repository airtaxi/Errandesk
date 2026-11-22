using Errandesk.Server.Data;
using MongoDB.Driver;

namespace Errandesk.Server.Services;

/// <summary>
/// Removes list, task and note tombstones that were deleted longer ago than the retention period, and records the newest change version removed.
/// Clients whose last synced version is older than that version can no longer learn about every deletion and must resynchronize completely.
/// </summary>
public sealed class TombstonePurger(ErrandeskDatabase database, DocumentUpdater documentUpdater, TombstonePurgeOptions options, TimeProvider timeProvider, ILogger<TombstonePurger> logger) : BackgroundService
{
    /// <summary>ID of the <c>change_counters</c> document that holds the newest change version of a purged tombstone.</summary>
    public const string PurgedThroughCounterId = "purgedTombstones";

    private static readonly UpdateOptions s_upsertOptions = new() { IsUpsert = true };

    /// <summary>The newest change version of a purged tombstone, or 0 when nothing was purged yet.</summary>
    public async Task<long> GetPurgedThroughVersionAsync(CancellationToken cancellationToken)
    {
        var counter = await database.ChangeCounters.Find(counter => counter.Id == PurgedThroughCounterId).FirstOrDefaultAsync(cancellationToken);
        return counter?.Value ?? 0;
    }

    /// <summary>Removes every expired tombstone and returns how many were removed.</summary>
    public async Task<long> PurgeAsync(CancellationToken cancellationToken)
    {
        var cutoff = documentUpdater.GetUtcNow() - options.Retention;
        var purgedCount = await PurgeAsync(database.TaskLists, cutoff, cancellationToken);
        purgedCount += await PurgeAsync(database.Tasks, cutoff, cancellationToken);
        purgedCount += await PurgeAsync(database.Notes, cutoff, cancellationToken);
        return purgedCount;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = options.InitialDelay;
        while (!stoppingToken.IsCancellationRequested && options.Interval != Timeout.InfiniteTimeSpan)
        {
            try { await Task.Delay(delay, timeProvider, stoppingToken); }
            catch (OperationCanceledException) { return; }

            try
            {
                var purgedCount = await PurgeAsync(stoppingToken);
                if (purgedCount > 0) logger.LogInformation("Purged {PurgedCount} tombstones.", purgedCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Purging tombstones failed."); }
            delay = options.Interval;
        }
    }

    /// <summary>
    /// Raises the purged-through version to the newest expired tombstone of the collection before removing the tombstones up to that version,
    /// so a change feed request never misses a removal without being told to resynchronize.
    /// </summary>
    private async Task<long> PurgeAsync<TDocument>(IMongoCollection<TDocument> collection, DateTime cutoff, CancellationToken cancellationToken) where TDocument : DocumentBase
    {
        var filterBuilder = Builders<TDocument>.Filter;
        var expiredFilter = filterBuilder.Eq(document => document.IsDeleted, true) & (filterBuilder.Lt(document => document.DeletedAt, cutoff) | (filterBuilder.Eq(document => document.DeletedAt, null) & filterBuilder.Lt(document => document.UpdatedAt, cutoff)));
        var newestChangeVersion = await collection.Find(expiredFilter).SortByDescending(document => document.ChangeVersion).Project(document => document.ChangeVersion).FirstOrDefaultAsync(cancellationToken);
        if (newestChangeVersion == 0) return 0;

        await database.ChangeCounters.UpdateOneAsync(counter => counter.Id == PurgedThroughCounterId, Builders<ChangeCounterDocument>.Update.Max(counter => counter.Value, newestChangeVersion), s_upsertOptions, cancellationToken);
        var result = await collection.DeleteManyAsync(expiredFilter & filterBuilder.Lte(document => document.ChangeVersion, newestChangeVersion), cancellationToken);
        return result.DeletedCount;
    }
}
