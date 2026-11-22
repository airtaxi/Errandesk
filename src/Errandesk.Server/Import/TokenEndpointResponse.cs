using System.Text.Json.Serialization;

namespace Errandesk.Server.Import;

/// <summary>
/// The answer of the Microsoft identity platform <c>/token</c> endpoint: tokens on success, or an OAuth error code such as <c>authorization_pending</c>.
/// </summary>
public sealed record TokenEndpointResponse([property: JsonPropertyName("access_token")] string? AccessToken, [property: JsonPropertyName("refresh_token")] string? RefreshToken, [property: JsonPropertyName("expires_in")] int ExpiresIn, [property: JsonPropertyName("error")] string? Error);
