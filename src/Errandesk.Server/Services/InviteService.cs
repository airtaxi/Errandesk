using System.Security.Cryptography;
using Errandesk.Contracts;
using Errandesk.Server.Data;
using MongoDB.Driver;

namespace Errandesk.Server.Services;

/// <summary>
/// Manages the server-wide invite codes that administrators hand out.
/// </summary>
public sealed class InviteService(ErrandeskDatabase database, ChangeVersionGenerator changeVersionGenerator, DocumentUpdater documentUpdater)
{
    public const int DefaultExpiryDays = 7;
    public const int MaximumExpiryDays = 365;
    public const int CodeLength = 12;

    private const string ResourceName = "invite";
    private const int MaximumCodeAttempts = 5;
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Every invite that was not deleted, newest first.</summary>
    public async Task<IReadOnlyList<InviteResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var invites = await database.Invites.Find(invite => !invite.IsDeleted).SortByDescending(invite => invite.CreatedAt).ToListAsync(cancellationToken);
        return [.. invites.Select(invite => invite.ToResponse())];
    }

    public async Task<InviteResponse> CreateAsync(Guid administratorUserId, CreateInviteRequest request, CancellationToken cancellationToken)
    {
        var expiryDays = request.ExpiresInDays ?? DefaultExpiryDays;
        if (expiryDays is < 1 or > MaximumExpiryDays) throw new RequestValidationException($"An invite must expire in 1 to {MaximumExpiryDays} days.");

        for (var attempt = 1; ; attempt++)
        {
            var utcNow = documentUpdater.GetUtcNow();
            var invite = new InviteDocument { Id = Guid.CreateVersion7(), Code = GenerateCode(), ExpiresAt = utcNow.AddDays(expiryDays) };
            invite.StampCreated(administratorUserId, utcNow, await changeVersionGenerator.NextAsync(cancellationToken));
            try
            {
                await database.Invites.InsertOneAsync(invite, cancellationToken: cancellationToken);
                return invite.ToResponse();
            }
            catch (MongoWriteException exception) when (exception.IsDuplicateKey(MongoIndexNames.UniqueInviteCode) && attempt < MaximumCodeAttempts) { }
        }
    }

    public Task DeleteAsync(Guid inviteId, CancellationToken cancellationToken) => documentUpdater.SoftDeleteAsync(database.Invites, Builders<InviteDocument>.Filter.Where(invite => invite.Id == inviteId && !invite.IsDeleted), ResourceName, cancellationToken);

    /// <summary>The upper-case form used for lookups.</summary>
    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private static string GenerateCode() => new(RandomNumberGenerator.GetItems<char>(CodeAlphabet, CodeLength));
}
