namespace Errandesk.Server.Data;

/// <summary>
/// One refresh token of a device session. Every refresh rotates the token: the used token records its successor and a new token joins the same session.
/// </summary>
public sealed class RefreshTokenDocument : DocumentBase
{
    /// <summary>Lower-case hexadecimal SHA-256 hash of the token.</summary>
    public string TokenHash { get; set; } = "";

    /// <summary>Identifies the rotation chain. All tokens of one sign-in share it.</summary>
    public Guid SessionId { get; set; }

    public string DeviceName { get; set; } = "";

    public DateTime ExpiresAt { get; set; }

    /// <summary>When the token was exchanged for its successor. Presenting a rotated token revokes the whole session.</summary>
    public DateTime? RotatedAt { get; set; }

    public Guid? ReplacedByTokenId { get; set; }

    public DateTime? RevokedAt { get; set; }
}
