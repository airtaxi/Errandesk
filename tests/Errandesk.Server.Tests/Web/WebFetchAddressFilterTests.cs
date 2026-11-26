using System.Net;
using System.Net.Sockets;
using Errandesk.Server.Web;

namespace Errandesk.Server.Tests.Web;

public sealed class WebFetchAddressFilterTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.20.30")]
    [InlineData("169.254.169.254")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("2002:7f00:1::")]
    public void IsAllowed_BlocksLocalAddressesEvenWhenPrivateNetworksAreAllowed(string address)
    {
        Assert.False(WebFetchAddressFilter.IsAllowed(IPAddress.Parse(address), false));
        Assert.False(WebFetchAddressFilter.IsAllowed(IPAddress.Parse(address), true));
    }

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.10")]
    [InlineData("100.64.0.1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:192.168.1.10")]
    [InlineData("64:ff9b::a00:5")]
    public void IsAllowed_BlocksPrivateNetworksUnlessAllowed(string address)
    {
        Assert.False(WebFetchAddressFilter.IsAllowed(IPAddress.Parse(address), false));
        Assert.True(WebFetchAddressFilter.IsAllowed(IPAddress.Parse(address), true));
    }

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("172.32.0.1")]
    [InlineData("8.8.8.8")]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946")]
    [InlineData("::ffff:8.8.8.8")]
    public void IsAllowed_AllowsPublicAddresses(string address) => Assert.True(WebFetchAddressFilter.IsAllowed(IPAddress.Parse(address), false));

    [Fact]
    public async Task ConnectAsync_RefusesHostsThatResolveToBlockedAddresses()
    {
        var exception = await Assert.ThrowsAsync<WebFetchBlockedAddressException>(async () => await WebFetchConnector.ConnectAsync(new DnsEndPoint("127.0.0.1", 80), true, TestContext.Current.CancellationToken));

        Assert.Equal("127.0.0.1", exception.Host);
    }

    [Fact]
    public async Task Handler_RefusesRequestsToBlockedAddressesBeforeConnecting()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var httpClient = new HttpClient(WebFetchConnector.CreateHandler(true));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => httpClient.GetAsync($"http://127.0.0.1:{port}/", TestContext.Current.CancellationToken));

        Assert.IsType<WebFetchBlockedAddressException>(exception.InnerException);
        Assert.False(listener.Pending());
    }
}
