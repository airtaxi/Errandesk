namespace Errandesk.Server.Data;

/// <summary>
/// A single-use invite code. <see cref="DocumentBase.OwnerUserId"/> is the administrator who created it.
/// </summary>
public sealed class InviteDocument : DocumentBase
{
    /// <summary>Upper-case code, unique across invites.</summary>
    public string Code { get; set; } = "";

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public Guid? UsedByUserId { get; set; }
}
