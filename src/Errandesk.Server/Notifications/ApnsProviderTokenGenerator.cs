using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Errandesk.Server.Configuration;

namespace Errandesk.Server.Notifications;

/// <summary>
/// Creates the ES256-signed JSON Web Token that authorizes requests to the Apple Push Notification service, and reuses it for <see cref="TokenLifetime"/>.
/// </summary>
public sealed class ApnsProviderTokenGenerator(ApnsSettings settings, TimeProvider timeProvider) : IDisposable
{
    /// <summary>How long a token is reused. APNs accepts tokens up to one hour old and rejects refreshes more often than every 20 minutes.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(50);

    private readonly Lock _lock = new();
    private readonly ECDsa _signingKey = CreateSigningKey(settings.PrivateKey);
    private string? _token;
    private DateTimeOffset _issuedAt;

    /// <summary>The current provider token, creating a new one when none exists or the current one is older than <see cref="TokenLifetime"/>.</summary>
    public string GetToken()
    {
        lock (_lock)
        {
            var utcNow = timeProvider.GetUtcNow();
            if (_token is null || utcNow - _issuedAt >= TokenLifetime || utcNow < _issuedAt)
            {
                _token = CreateToken(utcNow);
                _issuedAt = utcNow;
            }
            return _token;
        }
    }

    /// <summary>Discards <paramref name="token"/> after APNs rejected it, so the next request uses a new token.</summary>
    public void Invalidate(string token)
    {
        lock (_lock)
        {
            if (_token == token)
            {
                _token = null;
            }
        }
    }

    public void Dispose() => _signingKey.Dispose();

    private string CreateToken(DateTimeOffset issuedAt)
    {
        var encodedHeader = Base64Url.EncodeToString(WriteJson(writer =>
        {
            writer.WriteString("alg", "ES256");
            writer.WriteString("kid", settings.KeyId);
        }));
        var encodedClaims = Base64Url.EncodeToString(WriteJson(writer =>
        {
            writer.WriteString("iss", settings.TeamId);
            writer.WriteNumber("iat", issuedAt.ToUnixTimeSeconds());
        }));
        var signingInput = $"{encodedHeader}.{encodedClaims}";
        var signature = _signingKey.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{signingInput}.{Base64Url.EncodeToString(signature)}";
    }

    private static byte[] WriteJson(Action<Utf8JsonWriter> writeProperties)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writeProperties(writer);
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    private static ECDsa CreateSigningKey(string privateKeyPem)
    {
        var signingKey = ECDsa.Create();
        signingKey.ImportFromPem(privateKeyPem);
        return signingKey;
    }
}
