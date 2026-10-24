namespace TeamsRemote.Core.Transport;

public sealed class CommandRateLimiter
{
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly Dictionary<string, List<DateTimeOffset>> _requestTimestamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public CommandRateLimiter(int maxRequests = 5, TimeSpan? window = null)
    {
        _maxRequests = maxRequests;
        _window = window ?? TimeSpan.FromSeconds(10);
    }

    public bool CheckAndRecord(string controlKey)
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            var cutoff = now - _window;

            if (!_requestTimestamps.TryGetValue(controlKey, out var timestamps))
            {
                timestamps = new List<DateTimeOffset>();
                _requestTimestamps[controlKey] = timestamps;
            }

            // Remove expired entries
            timestamps.RemoveAll(t => t < cutoff);

            if (timestamps.Count >= _maxRequests)
            {
                return false; // Rate limited
            }

            timestamps.Add(now);
            return true;
        }
    }
}
