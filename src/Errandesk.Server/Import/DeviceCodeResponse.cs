using System.Text.Json.Serialization;

namespace Errandesk.Server.Import;

/// <summary>
/// The answer of the Microsoft identity platform <c>/devicecode</c> endpoint.
/// </summary>
public sealed record DeviceCodeResponse([property: JsonPropertyName("device_code")] string? DeviceCode, [property: JsonPropertyName("user_code")] string? UserCode, [property: JsonPropertyName("verification_uri")] string? VerificationUri, [property: JsonPropertyName("expires_in")] int ExpiresIn, [property: JsonPropertyName("interval")] int Interval);
