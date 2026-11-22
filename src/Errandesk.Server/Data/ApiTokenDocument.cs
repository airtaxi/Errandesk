namespace Errandesk.Server.Data;

/// <summary>
/// A personal access token of a user. Only the SHA-256 hash of the token is stored.
/// </summary>
public sealed class ApiTokenDocument : DocumentBase
{
    public string Name { get; set; } = "";

    /// <summary>Lower-case hexadecimal SHA-256 hash of the token, unique across all tokens.</summary>
    public string TokenHash { get; set; } = "";

    /// <summary>The first characters of the token, shown to the user.</summary>
    public string DisplayPrefix { get; set; } = "";

    public DateTime? LastUsedAt { get; set; }

    /// <summary>When the token was revoked; <see langword="null"/> while it is active.</summary>
    public DateTime? RevokedAt { get; set; }
}
