namespace Errandesk.Contracts;

/// <summary>
/// A message from the user to the assistant. Approval requests that are still pending are denied.
/// </summary>
/// <param name="Text">Message text.</param>
public sealed record SendMessageRequest(string Text);
