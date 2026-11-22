using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Configuration;
using Errandesk.Server.Data;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;

namespace Errandesk.Server.Services;

/// <summary>
/// Registration, sign-in, refresh token rotation, sign-out and password changes.
/// </summary>
public sealed class AccountService(ErrandeskDatabase database, ServerSettings settings, ChangeVersionGenerator changeVersionGenerator, DocumentUpdater documentUpdater, IPasswordHasher<UserDocument> passwordHasher, AccessTokenIssuer accessTokenIssuer, TaskListService taskListService)
{
    private const string UserResourceName = "user";
    private const string InvalidCredentialsMessage = "The user name or password is incorrect.";
    private const string InvalidRefreshTokenMessage = "The refresh token is invalid, expired or revoked.";
    private const string UserNameTakenMessage = "This user name is already taken.";

    private readonly string _unknownUserPasswordHash = passwordHasher.HashPassword(new UserDocument(), Guid.NewGuid().ToString());

    /// <summary>
    /// Creates an account with its default list and signs it in. The first user becomes the administrator regardless of the registration mode; later users follow the mode.
    /// </summary>
    public async Task<AuthenticationResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var userName = InputValidation.UserName(request.UserName);
        var password = InputValidation.Password(request.Password);
        var timeZone = InputValidation.TimeZone(request.TimeZone);
        var language = InputValidation.Language(request.Language);
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? userName : InputValidation.RequireText(request.DisplayName, "display name", InputValidation.MaximumDisplayNameLength);
        var deviceName = InputValidation.DeviceName(request.DeviceName);
        var normalizedUserName = InputValidation.NormalizeUserName(userName);

        while (true)
        {
            if (await database.Users.Find(user => user.NormalizedUserName == normalizedUserName).AnyAsync(cancellationToken)) throw new ResourceConflictException(UserNameTakenMessage);

            var userId = Guid.CreateVersion7();
            var isFirstUser = !await database.Users.Find(FilterDefinition<UserDocument>.Empty).AnyAsync(cancellationToken);
            var claimedInvite = isFirstUser ? null : await ClaimRegistrationPermissionAsync(request.InviteCode, userId, cancellationToken);

            var user = new UserDocument
            {
                Id = userId,
                UserName = userName,
                NormalizedUserName = normalizedUserName,
                DisplayName = displayName,
                IsAdministrator = isFirstUser,
                IsFirstUser = isFirstUser,
                TimeZone = timeZone,
                Language = language,
            };
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            user.StampCreated(userId, documentUpdater.GetUtcNow(), await changeVersionGenerator.NextAsync(cancellationToken));

            try { await database.Users.InsertOneAsync(user, cancellationToken: cancellationToken); }
            catch (MongoWriteException exception) when (exception.IsDuplicateKey())
            {
                if (claimedInvite is not null) await ReleaseInviteAsync(claimedInvite, cancellationToken);
                if (exception.IsDuplicateKey(MongoIndexNames.UniqueFirstUser)) continue;
                throw new ResourceConflictException(UserNameTakenMessage);
            }

            await taskListService.EnsureDefaultListAsync(userId, cancellationToken);
            return await CreateSessionAsync(user, deviceName, Guid.CreateVersion7(), null, cancellationToken);
        }
    }

    public async Task<AuthenticationResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedUserName = InputValidation.NormalizeUserName(request.UserName ?? "");
        var password = request.Password ?? "";
        var user = await database.Users.Find(user => user.NormalizedUserName == normalizedUserName && !user.IsDeleted).FirstOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            passwordHasher.VerifyHashedPassword(new UserDocument(), _unknownUserPasswordHash, password);
            throw new AuthenticationFailedException(InvalidCredentialsMessage);
        }

        var verificationResult = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verificationResult == PasswordVerificationResult.Failed) throw new AuthenticationFailedException(InvalidCredentialsMessage);
        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded) await StorePasswordHashAsync(user, passwordHasher.HashPassword(user, password), cancellationToken);

        return await CreateSessionAsync(user, InputValidation.DeviceName(request.DeviceName), Guid.CreateVersion7(), null, cancellationToken);
    }

    /// <summary>
    /// Exchanges a refresh token for a new token pair in the same session. Presenting a token that was already exchanged revokes the whole session.
    /// </summary>
    public async Task<AuthenticationResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var storedToken = await FindRefreshTokenAsync(request.RefreshToken, cancellationToken) ?? throw new AuthenticationFailedException(InvalidRefreshTokenMessage);
        if (storedToken.RevokedAt is not null || storedToken.IsDeleted) throw new AuthenticationFailedException(InvalidRefreshTokenMessage);
        if (storedToken.RotatedAt is not null)
        {
            await RevokeSessionAsync(storedToken.SessionId, cancellationToken);
            throw new AuthenticationFailedException(InvalidRefreshTokenMessage);
        }

        var utcNow = documentUpdater.GetUtcNow();
        if (storedToken.ExpiresAt <= utcNow) throw new AuthenticationFailedException(InvalidRefreshTokenMessage);

        var user = await database.Users.Find(user => user.Id == storedToken.OwnerUserId && !user.IsDeleted).FirstOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            await RevokeSessionAsync(storedToken.SessionId, cancellationToken);
            throw new AuthenticationFailedException(InvalidRefreshTokenMessage);
        }

        var successorTokenId = Guid.CreateVersion7();
        var unusedTokenFilter = Builders<RefreshTokenDocument>.Filter.Where(token => token.Id == storedToken.Id && token.RotatedAt == null && token.RevokedAt == null);
        var rotation = Builders<RefreshTokenDocument>.Update.Set(token => token.RotatedAt, utcNow).Set(token => token.ReplacedByTokenId, successorTokenId).Set(token => token.UpdatedAt, utcNow).Set(token => token.ChangeVersion, await changeVersionGenerator.NextAsync(cancellationToken));
        var rotationResult = await database.RefreshTokens.UpdateOneAsync(unusedTokenFilter, rotation, cancellationToken: cancellationToken);
        if (rotationResult.ModifiedCount == 0)
        {
            await RevokeSessionAsync(storedToken.SessionId, cancellationToken);
            throw new AuthenticationFailedException(InvalidRefreshTokenMessage);
        }

        return await CreateSessionAsync(user, storedToken.DeviceName, storedToken.SessionId, successorTokenId, cancellationToken);
    }

    /// <summary>Revokes every refresh token of the session the given token belongs to. Unknown tokens are ignored.</summary>
    public async Task LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var storedToken = await FindRefreshTokenAsync(request.RefreshToken, cancellationToken);
        if (storedToken is not null) await RevokeSessionAsync(storedToken.SessionId, cancellationToken);
    }

    /// <summary>Changes the password after checking the current one and revokes every session except <paramref name="currentSessionId"/>.</summary>
    public async Task ChangePasswordAsync(Guid userId, Guid? currentSessionId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var newPassword = InputValidation.Password(request.NewPassword, "new password");
        var currentPassword = request.CurrentPassword ?? "";
        var userFilter = Builders<UserDocument>.Filter.Where(user => user.Id == userId && !user.IsDeleted);

        await documentUpdater.UpdateAsync(database.Users, userFilter, UserResourceName, null, current => current.ToResponse(), current =>
        {
            if (passwordHasher.VerifyHashedPassword(current, current.PasswordHash, currentPassword) == PasswordVerificationResult.Failed) throw new RequestValidationException("The current password is incorrect.");
            current.PasswordHash = passwordHasher.HashPassword(current, newPassword);
        }, cancellationToken);

        var otherSessionsFilter = Builders<RefreshTokenDocument>.Filter.Where(token => token.OwnerUserId == userId && token.RevokedAt == null);
        if (currentSessionId is { } sessionId) otherSessionsFilter &= Builders<RefreshTokenDocument>.Filter.Ne(token => token.SessionId, sessionId);
        await RevokeRefreshTokensAsync(otherSessionsFilter, cancellationToken);
    }

    private async Task<InviteDocument?> ClaimRegistrationPermissionAsync(string? inviteCode, Guid userId, CancellationToken cancellationToken)
    {
        if (settings.RegistrationMode == RegistrationMode.Open) return null;
        if (settings.RegistrationMode == RegistrationMode.Disabled) throw new OperationNotAllowedException("Registration is disabled on this server.");
        if (string.IsNullOrWhiteSpace(inviteCode)) throw new RequestValidationException("An invite code is required to register on this server.");

        var code = InviteService.NormalizeCode(inviteCode);
        var utcNow = documentUpdater.GetUtcNow();
        var usableInviteFilter = Builders<InviteDocument>.Filter.Where(invite => invite.Code == code && !invite.IsDeleted && invite.UsedAt == null && invite.ExpiresAt > utcNow);
        var claim = Builders<InviteDocument>.Update.Set(invite => invite.UsedAt, utcNow).Set(invite => invite.UsedByUserId, userId).Set(invite => invite.UpdatedAt, utcNow).Set(invite => invite.ChangeVersion, await changeVersionGenerator.NextAsync(cancellationToken));
        return await database.Invites.FindOneAndUpdateAsync(usableInviteFilter, claim, cancellationToken: cancellationToken) ?? throw new RequestValidationException("The invite code is invalid, expired or already used.");
    }

    private async Task ReleaseInviteAsync(InviteDocument invite, CancellationToken cancellationToken)
    {
        var utcNow = documentUpdater.GetUtcNow();
        var release = Builders<InviteDocument>.Update.Set(stored => stored.UsedAt, null).Set(stored => stored.UsedByUserId, null).Set(stored => stored.UpdatedAt, utcNow).Set(stored => stored.ChangeVersion, await changeVersionGenerator.NextAsync(cancellationToken));
        await database.Invites.UpdateOneAsync(stored => stored.Id == invite.Id, release, cancellationToken: cancellationToken);
    }

    private async Task StorePasswordHashAsync(UserDocument user, string passwordHash, CancellationToken cancellationToken)
    {
        var utcNow = documentUpdater.GetUtcNow();
        var update = Builders<UserDocument>.Update.Set(stored => stored.PasswordHash, passwordHash).Set(stored => stored.UpdatedAt, utcNow).Set(stored => stored.ChangeVersion, await changeVersionGenerator.NextAsync(cancellationToken));
        await database.Users.UpdateOneAsync(stored => stored.Id == user.Id, update, cancellationToken: cancellationToken);
    }

    private async Task<AuthenticationResponse> CreateSessionAsync(UserDocument user, string deviceName, Guid sessionId, Guid? refreshTokenId, CancellationToken cancellationToken)
    {
        var refreshToken = RefreshTokens.Generate();
        var utcNow = documentUpdater.GetUtcNow();
        var storedToken = new RefreshTokenDocument
        {
            Id = refreshTokenId ?? Guid.CreateVersion7(),
            TokenHash = RefreshTokens.Hash(refreshToken),
            SessionId = sessionId,
            DeviceName = deviceName,
            ExpiresAt = utcNow + settings.RefreshTokenLifetime,
        };
        storedToken.StampCreated(user.Id, utcNow, await changeVersionGenerator.NextAsync(cancellationToken));
        await database.RefreshTokens.InsertOneAsync(storedToken, cancellationToken: cancellationToken);

        var accessToken = accessTokenIssuer.Issue(user, sessionId);
        return new AuthenticationResponse(accessToken.Token, accessToken.ExpiresAt, refreshToken, new DateTimeOffset(storedToken.ExpiresAt, TimeSpan.Zero), user.ToResponse());
    }

    private async Task<RefreshTokenDocument?> FindRefreshTokenAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken)) return null;
        var tokenHash = RefreshTokens.Hash(refreshToken);
        return await database.RefreshTokens.Find(token => token.TokenHash == tokenHash).FirstOrDefaultAsync(cancellationToken);
    }

    private Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken) => RevokeRefreshTokensAsync(Builders<RefreshTokenDocument>.Filter.Where(token => token.SessionId == sessionId && token.RevokedAt == null), cancellationToken);

    private async Task RevokeRefreshTokensAsync(FilterDefinition<RefreshTokenDocument> filter, CancellationToken cancellationToken)
    {
        var utcNow = documentUpdater.GetUtcNow();
        var revocation = Builders<RefreshTokenDocument>.Update.Set(token => token.RevokedAt, utcNow).Set(token => token.UpdatedAt, utcNow).Set(token => token.ChangeVersion, await changeVersionGenerator.NextAsync(cancellationToken));
        await database.RefreshTokens.UpdateManyAsync(filter, revocation, cancellationToken: cancellationToken);
    }
}
