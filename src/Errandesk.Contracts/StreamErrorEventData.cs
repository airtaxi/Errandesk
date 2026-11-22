namespace Errandesk.Contracts;

/// <summary>
/// Data of the <see cref="ServerEventNames.Error"/> event.
/// </summary>
/// <param name="Message">What went wrong, in English.</param>
public sealed record StreamErrorEventData(string Message);
