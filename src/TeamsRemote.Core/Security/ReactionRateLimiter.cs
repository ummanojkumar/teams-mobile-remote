namespace TeamsRemote.Core.Security;

public record ReactionRateLimitResult
{
    public bool Allowed { get; init; }
    public string? Reason { get; init; }
    public int RetryAfterMs { get; init; }

    public static ReactionRateLimitResult Permit() => new() { Allowed = true };

    public static ReactionRateLimitResult Deny(string reason, int retryAfterMs) => new()
    {
        Allowed = false,
        Reason = reason,
        RetryAfterMs = retryAfterMs
    };
}

public sealed class ReactionRateLimiter
{
    private readonly TimeSpan _perDeviceCooldown;
    private readonly int _maxBurstCount;
    private readonly TimeSpan _burstWindow;
    private readonly TimeSpan _globalCooldown;

    private readonly Dictionary<Guid, (DateTimeOffset LastSent, List<DateTimeOffset> Timestamps)> _deviceHistory = new();
    private DateTimeOffset _lastGlobalExecution = DateTimeOffset.MinValue;
    private readonly object _lock = new();

    public ReactionRateLimiter(
        TimeSpan? perDeviceCooldown = null,
        int maxBurstCount = 10,
        TimeSpan? burstWindow = null,
        TimeSpan? globalCooldown = null)
    {
        _perDeviceCooldown = perDeviceCooldown ?? TimeSpan.FromMilliseconds(500);
        _maxBurstCount = maxBurstCount;
        _burstWindow = burstWindow ?? TimeSpan.FromSeconds(10);
        _globalCooldown = globalCooldown ?? TimeSpan.FromMilliseconds(400);
    }

    public ReactionRateLimitResult CheckAndRecord(Guid deviceId)
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;

            // 1. Check Global Companion Cooldown (prevents rapid UIA thrashing across all devices)
            var elapsedSinceGlobal = now - _lastGlobalExecution;
            if (elapsedSinceGlobal < _globalCooldown)
            {
                int retryAfter = (int)Math.Ceiling((_globalCooldown - elapsedSinceGlobal).TotalMilliseconds);
                return ReactionRateLimitResult.Deny(
                    $"Global reaction cooldown active. Please wait {retryAfter}ms.",
                    retryAfter);
            }

            // 2. Check Per-Device Cooldown
            if (_deviceHistory.TryGetValue(deviceId, out var history))
            {
                var elapsedSinceDevice = now - history.LastSent;
                if (elapsedSinceDevice < _perDeviceCooldown)
                {
                    int retryAfter = (int)Math.Ceiling((_perDeviceCooldown - elapsedSinceDevice).TotalMilliseconds);
                    return ReactionRateLimitResult.Deny(
                        $"Reaction cooldown active. Please wait {retryAfter}ms.",
                        retryAfter);
                }

                // 3. Check Per-Device Burst Window (e.g. max 3 per 10s)
                var cutoff = now - _burstWindow;
                history.Timestamps.RemoveAll(t => t < cutoff);

                if (history.Timestamps.Count >= _maxBurstCount)
                {
                    var oldestInWindow = history.Timestamps[0];
                    var windowRemaining = oldestInWindow + _burstWindow - now;
                    int retryAfter = Math.Max(1, (int)Math.Ceiling(windowRemaining.TotalMilliseconds));
                    return ReactionRateLimitResult.Deny(
                        $"Reaction burst limit exceeded (max {_maxBurstCount} per {_burstWindow.TotalSeconds:0}s). Please wait {retryAfter}ms.",
                        retryAfter);
                }

                history.Timestamps.Add(now);
                _deviceHistory[deviceId] = (now, history.Timestamps);
            }
            else
            {
                _deviceHistory[deviceId] = (now, new List<DateTimeOffset> { now });
            }

            _lastGlobalExecution = now;
            return ReactionRateLimitResult.Permit();
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _deviceHistory.Clear();
            _lastGlobalExecution = DateTimeOffset.MinValue;
        }
    }
}
