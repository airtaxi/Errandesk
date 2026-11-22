using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Data;
using MongoDB.Driver;

namespace Errandesk.Server.Services;

/// <summary>
/// Creates, lists, revokes and validates the personal access tokens of users.
/// </summary>
public sealed class PersonalAccessTokenService(ErrandeskDatabase database, ChangeVersionGenerator changeVersionGenerator, DocumentUpdater documentUpdater)
{
    public const int MaximumNameLength = 100;
    public const int MaximumActiveTokenCount = 50;

    /// <summary>The last-used time is written at most once per this interval for each token.</summary>
    public static readonly TimeSpan LastUsedPrecision = TimeSpan.FromMinutes(1);

    private const string ResourceName = "token";

    /// <summary>The user's active tokens, newest first.</summary>
    public async Task<IReadOnlyList<PersonalAccessTokenResponse>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var tokens = await database.ApiTokens.Find(ActiveTokensFilter(userId)).SortByDescending(token => token.CreatedAt).ThenByDescending(token => token.Id).ToListAsync(cancellationToken);
        return [.. tokens.Select(ToResponse)];
    }

    /// <summary>Creates a token and returns its secret value, which is not stored and cannot be read again.</summary>
    public async Task<CreatedPersonalAccessTokenResponse> CreateAsync(Guid userId, CreatePersonalAccessTokenRequest request, CancellationToken cancellationToken)
    {
        var name = InputValidation.RequireText(request.Name, "name", MaximumNameLength);
        var activeTokenCount = await database.ApiTokens.CountDocumentsAsync(ActiveTokensFilter(userId), cancellationToken: cancellationToken);
        if (activeTokenCount >= MaximumActiveTokenCount) throw new RequestValidationException($"A user can have at most {MaximumActiveTokenCount} active tokens. Revoke a token first.");

        var token = PersonalAccessTokens.Generate();
        var document = new ApiTokenDocument
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            TokenHash = PersonalAccessTokens.Hash(token),
            DisplayPrefix = PersonalAccessTokens.CreateDisplayPrefix(token),
        };
        document.StampCreated(userId, documentUpdater.GetUtcNow(), await changeVersionGenerator.NextAsync(cancellationToken));
        await database.ApiTokens.InsertOneAsync(document, cancellationToken: cancellationToken);
        return new CreatedPersonalAccessTokenResponse(ToResponse(document), token);
    }

    /// <summary>Revokes one of the user's active tokens, so it no longer authenticates requests.</summary>
    public async Task RevokeAsync(Guid userId, Guid tokenId, CancellationToken cancellationToken)
    {
        var utcNow = documentUpdater.GetUtcNow();
        var changeVersion = await changeVersionGenerator.NextAsync(cancellationToken);
        var filter = ActiveTokensFilter(userId) & Builders<ApiTokenDocument>.Filter.Eq(token => token.Id, tokenId);
        var update = Builders<ApiTokenDocument>.Update.Set(token => token.RevokedAt, utcNow).Set(token => token.UpdatedAt, utcNow).Set(token => token.ChangeVersion, changeVersion);
        var result = await database.ApiTokens.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        if (result.MatchedCount == 0) throw new ResourceNotFoundException(ResourceName);
    }

    /// <summary>
    /// Finds the active token with the given value and its user, and records when it was used.
    /// Returns <see langword="null"/> when the token is unknown, revoked, or its user no longer exists.
    /// </summary>
    public async Task<PersonalAccessTokenAuthentication?> AuthenticateAsync(string token, CancellationToken cancellationToken)
    {
        if (!PersonalAccessTokens.HasPrefix(token) || token.Length != PersonalAccessTokenFormat.Prefix.Length + PersonalAccessTokens.RandomCharacterCount) return null;

        var tokenHash = PersonalAccessTokens.Hash(token);
        var document = await database.ApiTokens.Find(apiToken => apiToken.TokenHash == tokenHash && apiToken.RevokedAt == null && !apiToken.IsDeleted).FirstOrDefaultAsync(cancellationToken);
        if (document is null) return null;

        var user = await database.Users.Find(user => user.Id == document.OwnerUserId && !user.IsDeleted).FirstOrDefaultAsync(cancellationToken);
        if (user is null) return null;

        var utcNow = documentUpdater.GetUtcNow();
        if (document.LastUsedAt is not { } lastUsedAt || utcNow - lastUsedAt >= LastUsedPrecision)
        {
            var staleBefore = utcNow - LastUsedPrecision;
            var filterBuilder = Builders<ApiTokenDocument>.Filter;
            var filter = filterBuilder.Eq(apiToken => apiToken.Id, document.Id) & (filterBuilder.Eq(apiToken => apiToken.LastUsedAt, null) | filterBuilder.Lte(apiToken => apiToken.LastUsedAt, staleBefore));
            await database.ApiTokens.UpdateOneAsync(filter, Builders<ApiTokenDocument>.Update.Set(apiToken => apiToken.LastUsedAt, utcNow), cancellationToken: cancellationToken);
        }

        return new PersonalAccessTokenAuthentication(document.Id, user);
    }

    private static FilterDefinition<ApiTokenDocument> ActiveTokensFilter(Guid userId)
    {
        var filterBuilder = Builders<ApiTokenDocument>.Filter;
        return filterBuilder.Eq(token => token.OwnerUserId, userId) & filterBuilder.Eq(token => token.RevokedAt, null) & filterBuilder.Eq(token => token.IsDeleted, false);
    }

    private static PersonalAccessTokenResponse ToResponse(ApiTokenDocument token) => new(token.Id, token.Name, token.DisplayPrefix, ResponseMappings.ToUtcOffset(token.CreatedAt), ResponseMappings.ToUtcOffset(token.LastUsedAt));
}
