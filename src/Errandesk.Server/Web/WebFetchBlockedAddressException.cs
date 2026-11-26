namespace Errandesk.Server.Web;

/// <summary>
/// A web page request was refused because its host resolves only to addresses the server must not connect to.
/// </summary>
public sealed class WebFetchBlockedAddressException(string host) : IOException($"The host '{host}' resolves to a local or private network address, which the assistant may not read.")
{
    public string Host { get; } = host;
}
