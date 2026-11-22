namespace Errandesk.Server.Import;

/// <summary>
/// The outcome of one token request of the device code flow, with the tokens when the user signed in.
/// </summary>
public sealed record DeviceCodeTokenResult(DeviceCodeTokenStatus Status, MicrosoftTokens? Tokens = null);
