using System.Text.Json;
using TeamsRemote.Core.Adapters;
using TeamsRemote.Core.Models;
using TeamsRemote.Core.Protocol;
using TeamsRemote.Core.Security;
using Xunit;

namespace TeamsRemote.Tests;

public class ReactionFeasibilityTests
{
    [Fact]
    public void ReactionTypes_ValidatesFiveSupportedReactions()
    {
        Assert.Equal(5, ReactionTypes.ValidReactions.Count);
        Assert.Contains(ReactionTypes.Like, ReactionTypes.ValidReactions);
        Assert.Contains(ReactionTypes.Love, ReactionTypes.ValidReactions);
        Assert.Contains(ReactionTypes.Applause, ReactionTypes.ValidReactions);
        Assert.Contains(ReactionTypes.Laugh, ReactionTypes.ValidReactions);
        Assert.Contains(ReactionTypes.Surprise, ReactionTypes.ValidReactions);

        // Case-insensitive validation
        Assert.True(ReactionTypes.IsValid("like"));
        Assert.True(ReactionTypes.IsValid("LIKE"));
        Assert.True(ReactionTypes.IsValid("Love"));
        Assert.True(ReactionTypes.IsValid("applause"));
        Assert.True(ReactionTypes.IsValid("LAUGH"));
        Assert.True(ReactionTypes.IsValid("Surprise"));

        // Invalid reactions rejected
        Assert.False(ReactionTypes.IsValid("dislike"));
        Assert.False(ReactionTypes.IsValid("fire"));
        Assert.False(ReactionTypes.IsValid("rocket"));
        Assert.False(ReactionTypes.IsValid(null));
        Assert.False(ReactionTypes.IsValid(""));
        Assert.False(ReactionTypes.IsValid("   "));
    }

    [Theory]
    [InlineData("Like", "like-button", ReactionTypes.Like)]
    [InlineData("Like (thumbs up)", "", ReactionTypes.Like)]
    [InlineData("", "reaction-like", ReactionTypes.Like)]
    [InlineData("Love", "heart-button", ReactionTypes.Love)]
    [InlineData("Heart", "", ReactionTypes.Love)]
    [InlineData("Love (heart)", "reaction-love", ReactionTypes.Love)]
    [InlineData("Applause", "clap-button", ReactionTypes.Applause)]
    [InlineData("Clap", "", ReactionTypes.Applause)]
    [InlineData("Applause (clapping hands)", "reaction-applause", ReactionTypes.Applause)]
    [InlineData("Laugh", "laugh-button", ReactionTypes.Laugh)]
    [InlineData("Laughing", "", ReactionTypes.Laugh)]
    [InlineData("Surprise", "surprise-button", ReactionTypes.Surprise)]
    [InlineData("Surprise (wow)", "", ReactionTypes.Surprise)]
    [InlineData("Wow", "reaction-wow", ReactionTypes.Surprise)]
    public void ClassifyReactionType_IdentifiesAllFiveReactionsAccurately(string name, string autoId, string expectedReaction)
    {
        var resolved = UiaTeamsController.ClassifyReactionType(name, autoId);
        Assert.Equal(expectedReaction, resolved);
    }

    [Theory]
    [InlineData("Mute microphone", "microphone-button")]
    [InlineData("Turn camera on", "camera-button")]
    [InlineData("Raise hand", "raise-hand-button")]
    [InlineData("Lower hand", "lower-hand-button")]
    [InlineData("Share screen", "share-button")]
    [InlineData("Leave", "hangup-button")]
    public void ClassifyReactionType_IgnoresUnrelatedControls(string name, string autoId)
    {
        var resolved = UiaTeamsController.ClassifyReactionType(name, autoId);
        Assert.Null(resolved);
    }

    [Theory]
    [InlineData("React and raise hand", "reactions-button", true)]
    [InlineData("Reactions", "calling-reaction-bar", true)]
    [InlineData("React", "reaction-button", true)]
    [InlineData("Show reactions", "", true)]
    [InlineData("Mute microphone", "microphone-button", false)]
    [InlineData("Turn camera off", "camera-button", false)]
    public void IsReactionLauncher_DetectsToolbarReactionsButton(string name, string autoId, bool expected)
    {
        Assert.Equal(expected, UiaTeamsController.IsReactionLauncher(name, autoId));
    }

    [Fact]
    public void EstablishedCommandEnvelope_AlignsWithProjectProtocol()
    {
        var devId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        var corrId = Guid.NewGuid();

        var payload = new CommandPayload
        {
            Command = "send_reaction",
            Reaction = ReactionTypes.Love
        };

        var envelope = ProtocolEnvelope.Create(
            type: ProtocolMessageType.Command,
            payload: payload,
            deviceId: devId,
            correlationId: corrId,
            messageId: msgId);

        Assert.Equal(ProtocolEnvelope.CurrentVersion, envelope.Version);
        Assert.Equal(ProtocolMessageType.Command, envelope.Type);
        Assert.Equal(devId, envelope.DeviceId);
        Assert.Equal(msgId, envelope.MessageId);
        Assert.Equal(corrId, envelope.CorrelationId);

        var deserialized = envelope.DeserializePayload<CommandPayload>();
        Assert.NotNull(deserialized);
        Assert.Equal("send_reaction", deserialized.Command);
        Assert.Equal(ReactionTypes.Love, deserialized.Reaction);
    }

    [Fact]
    public void ReactionRateLimiter_EnforcesPerDeviceCooldown()
    {
        var limiter = new ReactionRateLimiter(
            perDeviceCooldown: TimeSpan.FromMilliseconds(100),
            maxBurstCount: 5,
            burstWindow: TimeSpan.FromSeconds(5),
            globalCooldown: TimeSpan.FromMilliseconds(10));

        var deviceId = Guid.NewGuid();

        // First attempt allowed
        var res1 = limiter.CheckAndRecord(deviceId);
        Assert.True(res1.Allowed);

        // Immediate second attempt rejected by per-device cooldown
        var res2 = limiter.CheckAndRecord(deviceId);
        Assert.False(res2.Allowed);
        Assert.Contains("cooldown active", res2.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(res2.RetryAfterMs > 0);
    }

    [Fact]
    public async Task ReactionRateLimiter_AllowsSubsequentRequestAfterCooldownExpires()
    {
        var limiter = new ReactionRateLimiter(
            perDeviceCooldown: TimeSpan.FromMilliseconds(40),
            maxBurstCount: 5,
            burstWindow: TimeSpan.FromSeconds(5),
            globalCooldown: TimeSpan.FromMilliseconds(10));

        var deviceId = Guid.NewGuid();

        Assert.True(limiter.CheckAndRecord(deviceId).Allowed);
        Assert.False(limiter.CheckAndRecord(deviceId).Allowed);

        await Task.Delay(55);

        var res = limiter.CheckAndRecord(deviceId);
        Assert.True(res.Allowed, "Request after cooldown must be allowed");
    }

    [Fact]
    public void ReactionRateLimiter_EnforcesGlobalCooldownAcrossDifferentDevices()
    {
        var limiter = new ReactionRateLimiter(
            perDeviceCooldown: TimeSpan.FromMilliseconds(10),
            maxBurstCount: 5,
            burstWindow: TimeSpan.FromSeconds(5),
            globalCooldown: TimeSpan.FromMilliseconds(100));

        var dev1 = Guid.NewGuid();
        var dev2 = Guid.NewGuid();

        Assert.True(limiter.CheckAndRecord(dev1).Allowed);

        // dev2 attempts immediately: should be blocked by global companion cooldown
        var res = limiter.CheckAndRecord(dev2);
        Assert.False(res.Allowed);
        Assert.Contains("Global reaction cooldown", res.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReactionRateLimiter_EnforcesBurstLimitInRollingWindow()
    {
        var limiter = new ReactionRateLimiter(
            perDeviceCooldown: TimeSpan.FromMilliseconds(15),
            maxBurstCount: 3,
            burstWindow: TimeSpan.FromMilliseconds(200),
            globalCooldown: TimeSpan.FromMilliseconds(5));

        var dev = Guid.NewGuid();

        // 1st request
        Assert.True(limiter.CheckAndRecord(dev).Allowed);
        await Task.Delay(20);

        // 2nd request
        Assert.True(limiter.CheckAndRecord(dev).Allowed);
        await Task.Delay(20);

        // 3rd request (max burst reached)
        Assert.True(limiter.CheckAndRecord(dev).Allowed);
        await Task.Delay(20);

        // 4th request inside window must be rejected
        var res4 = limiter.CheckAndRecord(dev);
        Assert.False(res4.Allowed);
        Assert.Contains("burst limit exceeded", res4.Reason, StringComparison.OrdinalIgnoreCase);

        // Wait for burst window to expire
        await Task.Delay(210);

        // Next request after window reset must be allowed
        Assert.True(limiter.CheckAndRecord(dev).Allowed);
    }
}
