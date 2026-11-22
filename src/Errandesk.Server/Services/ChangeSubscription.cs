using System.Threading.Channels;

namespace Errandesk.Server.Services;

/// <summary>
/// Change versions published for one user while the subscription is open.
/// </summary>
public sealed class ChangeSubscription(ChannelReader<long> reader, Action unsubscribe) : IDisposable
{
    public ChannelReader<long> Reader => reader;

    public void Dispose() => unsubscribe();
}
