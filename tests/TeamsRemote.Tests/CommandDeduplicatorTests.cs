using TeamsRemote.Core.Protocol;
using TeamsRemote.Core.Transport;
using Xunit;

namespace TeamsRemote.Tests;

public class CommandDeduplicatorTests
{
    [Fact]
    public void TryGet_WhenMessageIdNotStored_ReturnsFalse()
    {
        var dedupe = new CommandDeduplicator();
        bool found = dedupe.TryGet(Guid.NewGuid(), out var cached);

        Assert.False(found);
        Assert.Null(cached);
    }

    [Fact]
    public void StoreAndTryGet_WhenWithinRetentionWindow_ReturnsCachedResult()
    {
        var dedupe = new CommandDeduplicator(TimeSpan.FromMinutes(10));
        var msgId = Guid.NewGuid();
        var result = new CommandResultPayload
        {
            Status = "confirmed",
            StateRevision = 42,
            State = new Dictionary<string, string> { ["echo"] = "pong" },
            Reason = "Initial execution"
        };

        dedupe.Store(msgId, result);

        bool found = dedupe.TryGet(msgId, out var cached);
        Assert.True(found);
        Assert.NotNull(cached);
        Assert.Equal("confirmed", cached.Status);
        Assert.Equal(42, cached.StateRevision);
        Assert.Equal("Initial execution", cached.Reason);
    }

    [Fact]
    public async Task TryGet_WhenEntryExpires_ReturnsFalseAndPrunes()
    {
        // 50ms retention window for test
        var dedupe = new CommandDeduplicator(TimeSpan.FromMilliseconds(50));
        var msgId = Guid.NewGuid();
        var result = new CommandResultPayload
        {
            Status = "confirmed",
            StateRevision = 1
        };

        dedupe.Store(msgId, result);
        Assert.True(dedupe.TryGet(msgId, out _));

        // Wait for retention window to elapse
        await Task.Delay(80);

        bool found = dedupe.TryGet(msgId, out var cached);
        Assert.False(found);
        Assert.Null(cached);
        Assert.Equal(0, dedupe.Count);
    }
}
