namespace Errandesk.Contracts;

/// <summary>
/// Creates a personal access token.
/// </summary>
/// <param name="Name">Name that tells the user what the token is used for.</param>
public sealed record CreatePersonalAccessTokenRequest(string Name);
