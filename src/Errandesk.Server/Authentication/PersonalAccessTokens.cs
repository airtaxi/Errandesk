using System.Security.Cryptography;
using System.Text;
using Errandesk.Contracts;

namespace Errandesk.Server.Authentication;

/// <summary>
/// Generates personal access tokens (<c>edk_</c> plus random letters and digits) and the SHA-256 hashes stored in their place.
/// </summary>
public static class PersonalAccessTokens
{
    public const int RandomCharacterCount = 40;

    private const int DisplayRandomCharacterCount = 6;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>A new random token: the prefix followed by <see cref="RandomCharacterCount"/> letters and digits.</summary>
    public static string Generate() => PersonalAccessTokenFormat.Prefix + RandomNumberGenerator.GetString(Alphabet, RandomCharacterCount);

    /// <summary>The lower-case hexadecimal SHA-256 hash of the token.</summary>
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>The prefix and the first random characters of the token, which identify it to the user without revealing it.</summary>
    public static string CreateDisplayPrefix(string token) => token[..(PersonalAccessTokenFormat.Prefix.Length + DisplayRandomCharacterCount)];

    /// <summary>Whether the value has the personal access token prefix.</summary>
    public static bool HasPrefix(string? value) => value is not null && value.StartsWith(PersonalAccessTokenFormat.Prefix, StringComparison.Ordinal);

    /// <summary>The token of an <c>Authorization: Bearer</c> header, or <see langword="null"/> when the request has none.</summary>
    public static string? ReadBearerToken(HttpRequest request)
    {
        const string BearerPrefix = "Bearer ";
        var authorization = request.Headers.Authorization.ToString();
        if (!authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)) return null;

        var token = authorization[BearerPrefix.Length..].Trim();
        return token.Length == 0 ? null : token;
    }
}
