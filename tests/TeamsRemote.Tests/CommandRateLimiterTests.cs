using TeamsRemote.Core.Transport;
using Xunit;

namespace TeamsRemote.Tests;

public class CommandRateLimiterTests
{
    [Fact]
    public void CheckAndRecord_AllowsFirstFiveRequests_RejectsSixth()
    {
        var limiter = new CommandRateLimiter(maxRequests: 5, window: TimeSpan.FromSeconds(10));
        string control = "set_microphone";

        // First 5 requests must succeed
        for (int i = 0; i < 5; i++)
        {
            Assert.True(limiter.CheckAndRecord(control), $"Request {i + 1} should be permitted.");
        }

        // 6th request must be rate-limited
        Assert.False(limiter.CheckAndRecord(control), "6th request within window must be rate-limited.");
    }

    [Fact]
    public void CheckAndRecord_TracksControlsIndependently()
    {
        var limiter = new CommandRateLimiter(maxRequests: 2, window: TimeSpan.FromSeconds(10));

        Assert.True(limiter.CheckAndRecord("mic"));
        Assert.True(limiter.CheckAndRecord("mic"));
        Assert.False(limiter.CheckAndRecord("mic"));

        // Different control still has full quota
        Assert.True(limiter.CheckAndRecord("cam"));
        Assert.True(limiter.CheckAndRecord("cam"));
        Assert.False(limiter.CheckAndRecord("cam"));
    }

    [Fact]
    public async Task CheckAndRecord_AfterWindowExpires_AllowsSubsequentRequests()
    {
        // 50ms window for test
        var limiter = new CommandRateLimiter(maxRequests: 2, window: TimeSpan.FromMilliseconds(50));

        Assert.True(limiter.CheckAndRecord("mic"));
        Assert.True(limiter.CheckAndRecord("mic"));
        Assert.False(limiter.CheckAndRecord("mic"));

        await Task.Delay(80);

        Assert.True(limiter.CheckAndRecord("mic"), "After window expiry, requests must be permitted again.");
    }
}
