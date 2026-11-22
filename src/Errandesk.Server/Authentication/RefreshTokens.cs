using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Errandesk.Server.Authentication;

/// <summary>
/// Generates opaque refresh tokens and the SHA-256 hashes stored in their place.
/// </summary>
public static class RefreshTokens
{
    private const int TokenByteCount = 32;

    /// <summary>A new random token, Base64Url encoded.</summary>
    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenByteCount));

    /// <summary>The lower-case hexadecimal SHA-256 hash of the token.</summary>
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
