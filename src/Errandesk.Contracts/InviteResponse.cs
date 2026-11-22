namespace Errandesk.Contracts;

/// <summary>
/// A single-use invite code for registration.
/// </summary>
/// <param name="Id">Invite ID.</param>
/// <param name="Code">The code to enter at registration.</param>
/// <param name="CreatedByUserId">Administrator who created the invite.</param>
/// <param name="CreatedAt">When the invite was created.</param>
/// <param name="ExpiresAt">When the code stops working.</param>
/// <param name="UsedAt">When the code was used, or <see langword="null"/> while unused.</param>
/// <param name="UsedByUserId">User who registered with the code.</param>
public sealed record InviteResponse(Guid Id, string Code, Guid CreatedByUserId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? UsedAt, Guid? UsedByUserId);
