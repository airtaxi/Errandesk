namespace Errandesk.Contracts;

/// <summary>
/// Describes the server version, its registration policy and the optional features it has configured.
/// </summary>
/// <param name="ServerVersion">The server application version.</param>
/// <param name="RegistrationMode">The configured registration mode.</param>
/// <param name="IsRegistrationAvailable">Whether a new account can be registered right now.</param>
/// <param name="IsAiAvailable">Whether the assistant has a chat model configured.</param>
/// <param name="IsAndroidPushAvailable">Whether the server can send push notifications to Android devices.</param>
/// <param name="IsIosPushAvailable">Whether the server can send push notifications to iOS devices.</param>
/// <param name="IsMicrosoftTodoImportAvailable">Whether users can import their Microsoft To Do lists and tasks.</param>
public sealed record ServerInfoResponse(string ServerVersion, RegistrationMode RegistrationMode, bool IsRegistrationAvailable, bool IsAiAvailable, bool IsAndroidPushAvailable, bool IsIosPushAvailable, bool IsMicrosoftTodoImportAvailable = false);
