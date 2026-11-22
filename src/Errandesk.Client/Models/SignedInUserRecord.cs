using Errandesk.Contracts;

namespace Errandesk.Client.Models;

/// <summary>
/// The signed-in user saved on the device together with the server they signed in to.
/// </summary>
/// <param name="ServerAddress">Absolute address of the server.</param>
/// <param name="User">The user's profile.</param>
public sealed record SignedInUserRecord(string ServerAddress, UserResponse User);
