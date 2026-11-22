using System.Threading.Channels;
using Errandesk.Server.Data;

namespace Errandesk.Server.Services;

/// <summary>
/// Tells the open event streams of a user that lists, tasks or notes the user can see changed. Notifications reach streams of this server process only.
/// </summary>
public sealed class ChangeNotifier
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, List<Channel<long>>> _subscriptionsByUserId = [];

    /// <summary>Whether changes of documents of type <typeparamref name="TDocument"/> appear in the change feed.</summary>
    public static bool IsFeedDocument<TDocument>() => typeof(TDocument) == typeof(TaskListDocument) || typeof(TDocument) == typeof(TaskDocument) || typeof(TDocument) == typeof(NoteDocument);

    /// <summary>Starts receiving change versions for <paramref name="userId"/>. Unread notifications collapse into the most recent one.</summary>
    public ChangeSubscription Subscribe(Guid userId)
    {
        var channel = Channel.CreateBounded<long>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        lock (_gate)
        {
            if (!_subscriptionsByUserId.TryGetValue(userId, out var subscriptions)) _subscriptionsByUserId[userId] = subscriptions = [];
            subscriptions.Add(channel);
        }
        return new ChangeSubscription(channel.Reader, () => Unsubscribe(userId, channel));
    }

    /// <summary>Notifies every subscription of <paramref name="userId"/> that data changed with <paramref name="changeVersion"/>.</summary>
    public void NotifyChanged(Guid userId, long changeVersion)
    {
        lock (_gate)
        {
            if (!_subscriptionsByUserId.TryGetValue(userId, out var subscriptions)) return;
            foreach (var subscription in subscriptions) subscription.Writer.TryWrite(changeVersion);
        }
    }

    /// <summary>Notifies the owner when <typeparamref name="TDocument"/> appears in the change feed.</summary>
    public void NotifyChanged<TDocument>(Guid userId, long changeVersion)
    {
        if (IsFeedDocument<TDocument>())
        {
            NotifyChanged(userId, changeVersion);
        }
    }

    private void Unsubscribe(Guid userId, Channel<long> channel)
    {
        lock (_gate)
        {
            if (!_subscriptionsByUserId.TryGetValue(userId, out var subscriptions)) return;
            subscriptions.Remove(channel);
            if (subscriptions.Count == 0) _subscriptionsByUserId.Remove(userId);
        }
        channel.Writer.TryComplete();
    }
}
