namespace Errandesk.Server.Import;

/// <summary>
/// The Microsoft identity platform or Microsoft Graph answered in a way the import cannot continue from. The message is safe to show to the user.
/// </summary>
public sealed class MicrosoftServiceException(string message) : Exception(message);
