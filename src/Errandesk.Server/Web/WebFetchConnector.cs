using System.Net;
using System.Net.Sockets;

namespace Errandesk.Server.Web;

/// <summary>
/// Opens the connections of web page requests, checking the resolved addresses at connection time so that a host name cannot point the server at a blocked network.
/// </summary>
public static class WebFetchConnector
{
    /// <summary>
    /// Creates the handler of the web page client: no proxy, no cookies, no automatic redirects, and connections only to addresses that <see cref="WebFetchAddressFilter"/> allows.
    /// </summary>
    public static SocketsHttpHandler CreateHandler(bool allowPrivateNetworks) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = (context, cancellationToken) => ConnectAsync(context.DnsEndPoint, allowPrivateNetworks, cancellationToken),
    };

    /// <summary>
    /// Resolves the host and connects to the first reachable allowed address.
    /// </summary>
    /// <exception cref="WebFetchBlockedAddressException">The host resolves only to blocked addresses.</exception>
    public static async ValueTask<Stream> ConnectAsync(DnsEndPoint endPoint, bool allowPrivateNetworks, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(endPoint.Host, cancellationToken);
        IPAddress[] allowedAddresses = [.. addresses.Where(address => WebFetchAddressFilter.IsAllowed(address, allowPrivateNetworks))];
        if (allowedAddresses.Length == 0) throw new WebFetchBlockedAddressException(endPoint.Host);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowedAddresses, endPoint.Port, cancellationToken);
            return new NetworkStream(socket, true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
