using TeamsRemote.Core.Protocol;

namespace TeamsRemote.Core.Transport;

public sealed class CommandDeduplicator
{
    private record CachedEntry(CommandResultPayload Result, DateTimeOffset CachedAt);

    private readonly TimeSpan _retentionWindow;
    private readonly Dictionary<Guid, CachedEntry> _cache = new();
    private readonly object _lock = new();

    public CommandDeduplicator(TimeSpan? retentionWindow = null)
    {
        // Architecture requires at least 10 minutes
        _retentionWindow = retentionWindow ?? TimeSpan.FromMinutes(10);
    }

    public bool TryGet(Guid messageId, out CommandResultPayload? cachedResult)
    {
        lock (_lock)
        {
            PruneExpired();

            if (_cache.TryGetValue(messageId, out var entry))
            {
                cachedResult = entry.Result;
                return true;
            }

            cachedResult = null;
            return false;
        }
    }

    public void Store(Guid messageId, CommandResultPayload result)
    {
        lock (_lock)
        {
            PruneExpired();
            _cache[messageId] = new CachedEntry(result, DateTimeOffset.UtcNow);
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                PruneExpired();
                return _cache.Count;
            }
        }
    }

    private void PruneExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - _retentionWindow;
        var expiredKeys = _cache
            .Where(kvp => kvp.Value.CachedAt < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _cache.Remove(key);
        }
    }
}
