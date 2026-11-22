namespace Errandesk.Server.Import;

/// <summary>
/// Microsoft Graph tokens of one import. They live only in memory while the import runs.
/// </summary>
public sealed record MicrosoftTokens(string AccessToken, string? RefreshToken);
