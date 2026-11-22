using MongoDB.Driver;

namespace Errandesk.Server.Data;

/// <summary>
/// Hands out values of the single global change counter, incremented atomically in MongoDB and created on first use.
/// </summary>
public sealed class ChangeVersionGenerator(ErrandeskDatabase database)
{
    public const string GlobalCounterId = "global";

    private static readonly FindOneAndUpdateOptions<ChangeCounterDocument> s_incrementOptions = new() { IsUpsert = true, ReturnDocument = ReturnDocument.After };

    public async Task<long> NextAsync(CancellationToken cancellationToken)
    {
        var counter = await database.ChangeCounters.FindOneAndUpdateAsync(counter => counter.Id == GlobalCounterId, Builders<ChangeCounterDocument>.Update.Inc(counter => counter.Value, 1L), s_incrementOptions, cancellationToken);
        return counter.Value;
    }

    /// <summary>The most recently handed out change version, or 0 when none was handed out yet.</summary>
    public async Task<long> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var counter = await database.ChangeCounters.Find(counter => counter.Id == GlobalCounterId).FirstOrDefaultAsync(cancellationToken);
        return counter?.Value ?? 0;
    }
}
