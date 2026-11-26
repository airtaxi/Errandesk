using System.Net;
using System.Net.Sockets;

namespace Errandesk.Server.Web;

/// <summary>
/// Decides which IP addresses the server may connect to when it reads a web page for the assistant.
/// </summary>
/// <remarks>
/// Loopback, unspecified, link-local (including cloud metadata addresses), multicast and reserved addresses are always blocked.
/// Private ranges (RFC 1918, carrier-grade NAT and IPv6 unique local addresses) are blocked unless private networks are allowed.
/// IPv4 addresses embedded in IPv4-mapped, NAT64 and 6to4 IPv6 addresses are checked as IPv4 addresses.
/// </remarks>
public static class WebFetchAddressFilter
{
    private static readonly IPNetwork[] s_alwaysBlockedNetworks =
    [
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("192.0.0.0/24"),
        IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("224.0.0.0/4"),
        IPNetwork.Parse("240.0.0.0/4"),
        IPNetwork.Parse("::/128"),
        IPNetwork.Parse("::1/128"),
        IPNetwork.Parse("fe80::/10"),
        IPNetwork.Parse("ff00::/8"),
    ];

    private static readonly IPNetwork[] s_privateNetworks =
    [
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("fc00::/7"),
        IPNetwork.Parse("fec0::/10"),
    ];

    private static readonly IPNetwork s_nat64Network = IPNetwork.Parse("64:ff9b::/96");
    private static readonly IPNetwork s_sixToFourNetwork = IPNetwork.Parse("2002::/16");

    /// <summary>Whether the server may connect to <paramref name="address"/>.</summary>
    public static bool IsAllowed(IPAddress address, bool allowPrivateNetworks)
    {
        var normalizedAddress = Normalize(address);
        if (s_alwaysBlockedNetworks.Any(network => network.Contains(normalizedAddress))) return false;
        return allowPrivateNetworks || !s_privateNetworks.Any(network => network.Contains(normalizedAddress));
    }

    /// <summary>Returns the IPv4 address an IPv6 address carries, or the address itself.</summary>
    private static IPAddress Normalize(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address;
        if (address.IsIPv4MappedToIPv6) return address.MapToIPv4();

        var bytes = address.GetAddressBytes();
        if (s_nat64Network.Contains(address)) return new IPAddress(bytes.AsSpan(12, 4));
        if (s_sixToFourNetwork.Contains(address)) return new IPAddress(bytes.AsSpan(2, 4));
        return address;
    }
}
