namespace Errandesk.Contracts;

/// <summary>
/// Registers or updates a client device of the signed-in user.
/// </summary>
/// <param name="Id">ID the client generated once for its installation. Registering it again updates the device.</param>
/// <param name="Platform">One of <see cref="DevicePlatforms.All"/>.</param>
/// <param name="DeviceName">Name shown for the device.</param>
/// <param name="PushToken">FCM registration token on Android, hexadecimal APNs device token on iOS; <see langword="null"/> on other platforms or when push is not set up.</param>
/// <param name="AppVersion">Version of the installed app.</param>
public sealed record RegisterDeviceRequest(Guid Id, string Platform, string? DeviceName = null, string? PushToken = null, string? AppVersion = null);
