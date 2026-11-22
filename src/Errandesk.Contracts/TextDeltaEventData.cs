namespace Errandesk.Contracts;

/// <summary>
/// Data of the <see cref="ServerEventNames.TextDelta"/> event.
/// </summary>
/// <param name="Text">Text to append to the assistant message.</param>
public sealed record TextDeltaEventData(string Text);
