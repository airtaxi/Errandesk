namespace Errandesk.Contracts;

/// <summary>
/// Languages a user can choose.
/// </summary>
public static class UserLanguages
{
    public const string English = "en";

    public const string Korean = "ko";

    public static IReadOnlyList<string> All { get; } = [English, Korean];
}
