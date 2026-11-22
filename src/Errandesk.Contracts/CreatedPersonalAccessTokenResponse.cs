namespace Errandesk.Contracts;

/// <summary>
/// A newly created personal access token. <paramref name="Token"/> is returned only in this response; the server keeps a hash of it.
/// </summary>
/// <param name="PersonalAccessToken">The stored token details.</param>
/// <param name="Token">The secret token value, starting with <see cref="PersonalAccessTokenFormat.Prefix"/>.</param>
public sealed record CreatedPersonalAccessTokenResponse(PersonalAccessTokenResponse PersonalAccessToken, string Token);
