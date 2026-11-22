namespace Errandesk.Server.Authentication;

/// <summary>
/// Rate limit applied to adding list members by user name, per signed-in user, so user names cannot be probed in bulk.
/// </summary>
public static class ListSharingRateLimiting
{
    public const string PolicyName = "list-sharing";

    public const int PermitLimit = 10;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
}
