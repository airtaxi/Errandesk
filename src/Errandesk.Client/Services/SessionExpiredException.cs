namespace Errandesk.Client.Services;

/// <summary>
/// The session could not be renewed, so the user was signed out.
/// </summary>
public sealed class SessionExpiredException() : Exception("The session has ended. Sign in again.");
