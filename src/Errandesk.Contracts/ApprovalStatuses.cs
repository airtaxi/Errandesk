namespace Errandesk.Contracts;

/// <summary>
/// Values of <see cref="ApprovalRequestResponse.Status"/>.
/// </summary>
public static class ApprovalStatuses
{
    /// <summary>The user has not decided yet.</summary>
    public const string Pending = "pending";

    /// <summary>The user allowed the tool to run.</summary>
    public const string Approved = "approved";

    /// <summary>The user refused, or sent another message instead of deciding.</summary>
    public const string Denied = "denied";
}
