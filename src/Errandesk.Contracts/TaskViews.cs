namespace Errandesk.Contracts;

/// <summary>
/// Values of the <c>view</c> query parameter of <c>GET /api/tasks</c> besides a list ID.
/// </summary>
public static class TaskViews
{
    /// <summary>Tasks added to My Day today, in the user's time zone.</summary>
    public const string MyDay = "myday";

    /// <summary>Tasks marked important, from every list.</summary>
    public const string Important = "important";

    /// <summary>Tasks with a due date, from every list, earliest first.</summary>
    public const string Planned = "planned";
}
