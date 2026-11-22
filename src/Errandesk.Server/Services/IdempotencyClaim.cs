using Errandesk.Server.Data;

namespace Errandesk.Server.Services;

/// <summary>
/// The result of claiming an idempotency key.
/// </summary>
/// <param name="Outcome">How the request proceeds.</param>
/// <param name="ClaimId">Identifies this request's claim when <paramref name="Outcome"/> is <see cref="IdempotencyClaimOutcome.Started"/>.</param>
/// <param name="StoredResponse">The completed record when <paramref name="Outcome"/> is <see cref="IdempotencyClaimOutcome.Replay"/>.</param>
public sealed record IdempotencyClaim(IdempotencyClaimOutcome Outcome, Guid ClaimId = default, IdempotencyKeyDocument? StoredResponse = null);
