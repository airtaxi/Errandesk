using MongoDB.Driver;

namespace Errandesk.Server.Data;

/// <summary>
/// Creates the indexes of the <c>api_tokens</c> collection: a unique token hash for authentication and the owner for listing.
/// </summary>
public sealed class ApiTokenIndexCreator : IMongoIndexCreator
{
    public async Task CreateIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var apiTokens = database.GetCollection<ApiTokenDocument>(MongoCollectionNames.ApiTokens);
        await apiTokens.Indexes.CreateOneAsync(new CreateIndexModel<ApiTokenDocument>(Builders<ApiTokenDocument>.IndexKeys.Ascending(token => token.TokenHash), new CreateIndexOptions { Name = MongoIndexNames.UniqueApiTokenHash, Unique = true }), cancellationToken: cancellationToken);
        await apiTokens.Indexes.CreateOneAsync(new CreateIndexModel<ApiTokenDocument>(Builders<ApiTokenDocument>.IndexKeys.Ascending(token => token.OwnerUserId).Ascending(token => token.RevokedAt)), cancellationToken: cancellationToken);
    }
}
