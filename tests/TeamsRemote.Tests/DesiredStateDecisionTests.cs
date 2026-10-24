using TeamsRemote.Core.Classification;
using TeamsRemote.Core.Models;
using Xunit;

namespace TeamsRemote.Tests;

public class DesiredStateDecisionTests
{
    [Fact]
    public void Evaluate_WhenAlreadyMutedAndRequestMuted_ReturnsAlreadyInDesiredStateAndNoInvoke()
    {
        var (shouldInvoke, status, reason) = DesiredStateDecisionEngine.Evaluate(
            currentState: TeamsControlState.Muted,
            desiredState: DesiredMicrophoneState.Muted,
            hasActionablePattern: true,
            isEnabled: true);

        Assert.False(shouldInvoke);
        Assert.Equal(CommandExecutionStatus.AlreadyInDesiredState, status);
        Assert.Contains("already muted", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WhenAlreadyUnmutedAndRequestUnmuted_ReturnsAlreadyInDesiredStateAndNoInvoke()
    {
        var (shouldInvoke, status, reason) = DesiredStateDecisionEngine.Evaluate(
            currentState: TeamsControlState.Unmuted,
            desiredState: DesiredMicrophoneState.Unmuted,
            hasActionablePattern: true,
            isEnabled: true);

        Assert.False(shouldInvoke);
        Assert.Equal(CommandExecutionStatus.AlreadyInDesiredState, status);
        Assert.Contains("already unmuted", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WhenMutedAndRequestUnmuted_ReturnsShouldInvoke()
    {
        var (shouldInvoke, status, reason) = DesiredStateDecisionEngine.Evaluate(
            currentState: TeamsControlState.Muted,
            desiredState: DesiredMicrophoneState.Unmuted,
            hasActionablePattern: true,
            isEnabled: true);

        Assert.True(shouldInvoke);
        Assert.Equal(CommandExecutionStatus.Confirmed, status);
        Assert.Contains("differs from requested state", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WhenUnmutedAndRequestMuted_ReturnsShouldInvoke()
    {
        var (shouldInvoke, status, reason) = DesiredStateDecisionEngine.Evaluate(
            currentState: TeamsControlState.Unmuted,
            desiredState: DesiredMicrophoneState.Muted,
            hasActionablePattern: true,
            isEnabled: true);

        Assert.True(shouldInvoke);
        Assert.Equal(CommandExecutionStatus.Confirmed, status);
        Assert.Contains("differs from requested state", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WhenStateIsUnknown_ReturnsStateUncertainAndNoInvoke()
    {
        var (shouldInvoke, status, reason) = DesiredStateDecisionEngine.Evaluate(
            currentState: TeamsControlState.Unknown,
            desiredState: DesiredMicrophoneState.Muted,
            hasActionablePattern: true,
            isEnabled: true);

        Assert.False(shouldInvoke);
        Assert.Equal(CommandExecutionStatus.StateUncertain, status);
        Assert.Contains("unknown or unreadable", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WhenActionablePatternIsMissing_ReturnsUnsupportedAndNoInvoke()
    {
        var (shouldInvoke, status, reason) = DesiredStateDecisionEngine.Evaluate(
            currentState: TeamsControlState.Muted,
            desiredState: DesiredMicrophoneState.Unmuted,
            hasActionablePattern: false,
            isEnabled: true);

        Assert.False(shouldInvoke);
        Assert.Equal(CommandExecutionStatus.Unsupported, status);
        Assert.Contains("does not support an actionable invocation pattern", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WhenControlIsDisabled_ReturnsUnsupportedAndNoInvoke()
    {
        var (shouldInvoke, status, reason) = DesiredStateDecisionEngine.Evaluate(
            currentState: TeamsControlState.Muted,
            desiredState: DesiredMicrophoneState.Unmuted,
            hasActionablePattern: true,
            isEnabled: false);

        Assert.False(shouldInvoke);
        Assert.Equal(CommandExecutionStatus.Unsupported, status);
        Assert.Contains("disabled", reason, StringComparison.OrdinalIgnoreCase);
    }
}
