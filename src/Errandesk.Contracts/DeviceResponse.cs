namespace Errandesk.Contracts;

/// <summary>
/// A registered client device.
/// </summary>
/// <param name="Id">Device ID chosen by the client.</param>
/// <param name="Platform">One of <see cref="DevicePlatforms.All"/>.</param>
/// <param name="DeviceName">Name shown for the device.</param>
/// <param name="AppVersion">Version of the installed app, empty when unknown.</param>
/// <param name="HasPushToken">Whether the server can send push notifications to the device.</param>
/// <param name="LastSeenAt">When the device last registered.</param>
/// <param name="CreatedAt">When the device first registered.</param>
public sealed record DeviceResponse(Guid Id, string Platform, string DeviceName, string AppVersion, bool HasPushToken, DateTimeOffset LastSeenAt, DateTimeOffset CreatedAt);
