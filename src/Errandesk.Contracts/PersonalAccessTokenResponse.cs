namespace Errandesk.Contracts;

/// <summary>
/// A personal access token of the signed-in user, without its secret value.
/// </summary>
/// <param name="Id">Token ID.</param>
/// <param name="Name">Name the user gave the token.</param>
/// <param name="DisplayPrefix">The first characters of the token, shown so the user can recognize it.</param>
/// <param name="CreatedAt">When the token was created.</param>
/// <param name="LastUsedAt">When the token last authenticated a request, or <see langword="null"/> when it was never used.</param>
public sealed record PersonalAccessTokenResponse(Guid Id, string Name, string DisplayPrefix, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);
