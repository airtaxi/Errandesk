namespace Errandesk.Contracts;

/// <summary>
/// Data of the <see cref="ServerEventNames.Changed"/> event: data the user can see changed.
/// </summary>
/// <param name="Version">The global change version of the change.</param>
public sealed record ChangedEventData(long Version);
