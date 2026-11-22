namespace Errandesk.Client.Models;

/// <summary>
/// Opens the sign-in page with a message, for example after the session ended.
/// </summary>
/// <param name="Message">The message shown above the form, or <see langword="null"/> for none.</param>
/// <param name="IsRestoreRetryAvailable">Whether a stored session exists that could not be resumed because the server was unreachable.</param>
public sealed record SignInPageParameter(string? Message, bool IsRestoreRetryAvailable);
