using TeamsRemote.Core.Classification;
using TeamsRemote.Core.Models;
using Xunit;

namespace TeamsRemote.Tests;

public class ControlCapabilityClassifierTests
{
    [Fact]
    public void Classify_WhenNotDetected_ReturnsUnavailable()
    {
        var (tier, reason) = ControlCapabilityClassifier.Classify(
            isFound: false,
            isEnabled: true,
            readableState: TeamsControlState.Muted,
            supportedPatterns: new[] { "TogglePattern" });

        Assert.Equal(ControlCapabilityTier.Unavailable, tier);
        Assert.Contains("not detected", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_WhenStateIsUnknown_ReturnsUnavailable()
    {
        var (tier, reason) = ControlCapabilityClassifier.Classify(
            isFound: true,
            isEnabled: true,
            readableState: TeamsControlState.Unknown,
            supportedPatterns: new[] { "TogglePattern" });

        Assert.Equal(ControlCapabilityTier.Unavailable, tier);
        Assert.Contains("state could not be determined", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("TogglePattern")]
    [InlineData("Toggle")]
    [InlineData("InvokePattern")]
    [InlineData("Invoke")]
    [InlineData("SelectionItemPattern")]
    public void Classify_WhenStateIsReadableAndHasActionablePattern_ReturnsVerified(string pattern)
    {
        var (tier, reason) = ControlCapabilityClassifier.Classify(
            isFound: true,
            isEnabled: true,
            readableState: TeamsControlState.Unmuted,
            supportedPatterns: new[] { pattern });

        Assert.Equal(ControlCapabilityTier.Verified, tier);
        Assert.Contains("is supported", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_WhenStateIsReadableButNoActionablePattern_ReturnsObserveOnly()
    {
        var (tier, reason) = ControlCapabilityClassifier.Classify(
            isFound: true,
            isEnabled: true,
            readableState: TeamsControlState.Muted,
            supportedPatterns: new[] { "ValuePattern", "ScrollPattern" });

        Assert.Equal(ControlCapabilityTier.ObserveOnly, tier);
        Assert.Contains("no actionable execution pattern", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_WhenControlIsDisabled_ReturnsObserveOnly()
    {
        var (tier, reason) = ControlCapabilityClassifier.Classify(
            isFound: true,
            isEnabled: false,
            readableState: TeamsControlState.Muted,
            supportedPatterns: new[] { "TogglePattern" });

        Assert.Equal(ControlCapabilityTier.ObserveOnly, tier);
        Assert.Contains("disabled/read-only", reason, StringComparison.OrdinalIgnoreCase);
    }
}
