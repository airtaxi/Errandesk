using System.Net;
using System.Net.Sockets;

namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// Provides a connection string to a local port where nothing listens, with short timeouts.
/// </summary>
public static class UnreachableMongoDb
{
    public static string CreateConnectionString()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return $"mongodb://127.0.0.1:{port}/?serverSelectionTimeoutMS=500&connectTimeoutMS=500";
    }
}
