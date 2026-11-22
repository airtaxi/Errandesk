namespace Errandesk.Contracts;

/// <summary>
/// Signs in with a user name and password on the named device.
/// </summary>
public sealed record LoginRequest(string UserName, string Password, string? DeviceName = null);
