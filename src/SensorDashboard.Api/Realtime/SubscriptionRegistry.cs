using System.Collections.Concurrent;

namespace SensorDashboard.Api.Realtime;

public enum SubscribeOutcome
{
    Added,
    AlreadySubscribed,
    LimitReached,
}

/// <summary>
/// Which hub groups each connection has joined. Makes subscribing idempotent (a repeated
/// subscribe doesn't re-join the group) and caps how many groups one connection can hold.
/// </summary>
public sealed class SubscriptionRegistry
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _byConnection = new();

    public SubscribeOutcome TryAdd(string connectionId, string group, int maxPerConnection)
    {
        var groups = _byConnection.GetOrAdd(connectionId, _ => []);
        lock (groups)
        {
            if (groups.Contains(group)) return SubscribeOutcome.AlreadySubscribed;
            if (groups.Count >= maxPerConnection) return SubscribeOutcome.LimitReached;
            groups.Add(group);
            return SubscribeOutcome.Added;
        }
    }

    /// <returns>True if the connection was subscribed to the group.</returns>
    public bool Remove(string connectionId, string group)
    {
        if (!_byConnection.TryGetValue(connectionId, out var groups)) return false;
        lock (groups) return groups.Remove(group);
    }

    public void RemoveConnection(string connectionId) => _byConnection.TryRemove(connectionId, out _);

    public int Count(string connectionId) =>
        _byConnection.TryGetValue(connectionId, out var groups) ? groups.Count : 0;
}
