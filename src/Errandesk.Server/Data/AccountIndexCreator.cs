using MongoDB.Driver;

namespace Errandesk.Server.Data;

/// <summary>
/// Creates the indexes of the <c>users</c>, <c>invites</c> and <c>refresh_tokens</c> collections.
/// </summary>
public sealed class AccountIndexCreator : IMongoIndexCreator
{
    /// <summary>How long expired refresh tokens are kept before MongoDB removes them.</summary>
    public static readonly TimeSpan ExpiredRefreshTokenRetention = TimeSpan.FromDays(7);

    public async Task CreateIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var users = database.GetCollection<UserDocument>(MongoCollectionNames.Users);
        var userKeys = Builders<UserDocument>.IndexKeys;
        await users.Indexes.CreateManyAsync([new CreateIndexModel<UserDocument>(userKeys.Ascending(user => user.NormalizedUserName), new CreateIndexOptions { Name = MongoIndexNames.UniqueUserName, Unique = true }), new CreateIndexModel<UserDocument>(userKeys.Ascending(user => user.IsFirstUser), new CreateIndexOptions<UserDocument> { Name = MongoIndexNames.UniqueFirstUser, Unique = true, PartialFilterExpression = Builders<UserDocument>.Filter.Eq(user => user.IsFirstUser, true) }),], cancellationToken);

        var invites = database.GetCollection<InviteDocument>(MongoCollectionNames.Invites);
        await invites.Indexes.CreateOneAsync(new CreateIndexModel<InviteDocument>(Builders<InviteDocument>.IndexKeys.Ascending(invite => invite.Code), new CreateIndexOptions { Name = MongoIndexNames.UniqueInviteCode, Unique = true }), cancellationToken: cancellationToken);

        var refreshTokens = database.GetCollection<RefreshTokenDocument>(MongoCollectionNames.RefreshTokens);
        var refreshTokenKeys = Builders<RefreshTokenDocument>.IndexKeys;
        await refreshTokens.Indexes.CreateManyAsync([new CreateIndexModel<RefreshTokenDocument>(refreshTokenKeys.Ascending(token => token.TokenHash), new CreateIndexOptions { Name = MongoIndexNames.UniqueRefreshTokenHash, Unique = true }), new CreateIndexModel<RefreshTokenDocument>(refreshTokenKeys.Ascending(token => token.SessionId)), new CreateIndexModel<RefreshTokenDocument>(refreshTokenKeys.Ascending(token => token.OwnerUserId)), new CreateIndexModel<RefreshTokenDocument>(refreshTokenKeys.Ascending(token => token.ExpiresAt), new CreateIndexOptions { ExpireAfter = ExpiredRefreshTokenRetention }),], cancellationToken);
    }
}
