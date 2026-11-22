using MongoDB.Driver;

namespace Errandesk.Server.Data;

/// <summary>
/// Creates the indexes of one or more collections. Every registered implementation runs at startup and must be idempotent.
/// </summary>
public interface IMongoIndexCreator
{
    Task CreateIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken);
}
