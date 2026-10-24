using TeamsRemote.Core.Adapters;
using TeamsRemote.Core.Models;
using Xunit;

namespace TeamsRemote.Tests;

public class VerificationTimeoutTests
{
    [Fact]
    public async Task ExecuteAsync_WhenVerificationTimesOut_ReturnsTimedOutAndNeverClaimsConfirmed()
    {
        var coordinator = new MicrophoneActionCoordinator();
        bool actionInvoked = false;

        var result = await coordinator.ExecuteAsync(
            DesiredMicrophoneState.Muted,
            controlProvider: () => Task.FromResult((
                found: true,
                currentState: TeamsControlState.Unmuted,
                hasPattern: true,
                isEnabled: true,
                invokeAction: (Action)(() => actionInvoked = true))),
            // Continues returning old state "unmuted" - state never changed to "muted"
            reQueryStateFunc: () => Task.FromResult(TeamsControlState.Unmuted),
            verificationTimeout: TimeSpan.FromMilliseconds(150));

        Assert.True(actionInvoked);
        Assert.Equal(CommandExecutionStatus.TimedOut, result.Status);
        Assert.Equal("VERIFICATION_TIMED_OUT", result.ErrorCode);
        Assert.Equal(TeamsControlState.Unmuted, result.FinalState);
        Assert.Contains("timed out", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("confirmed", result.Status.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WhenVerificationSucceedsWithinTimeout_ReturnsConfirmed()
    {
        var coordinator = new MicrophoneActionCoordinator();
        bool actionInvoked = false;
        int pollCount = 0;

        var result = await coordinator.ExecuteAsync(
            DesiredMicrophoneState.Muted,
            controlProvider: () => Task.FromResult((
                found: true,
                currentState: TeamsControlState.Unmuted,
                hasPattern: true,
                isEnabled: true,
                invokeAction: (Action)(() => actionInvoked = true))),
            reQueryStateFunc: () =>
            {
                pollCount++;
                // State transitions to "muted" on second poll
                return Task.FromResult(pollCount >= 2 ? TeamsControlState.Muted : TeamsControlState.Unmuted);
            },
            verificationTimeout: TimeSpan.FromSeconds(1));

        Assert.True(actionInvoked);
        Assert.Equal(CommandExecutionStatus.Confirmed, result.Status);
        Assert.Equal(TeamsControlState.Muted, result.FinalState);
        Assert.Contains("confirmed via UIA verification", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WhenInvokeActionThrows_ReturnsFailedWithInvokeFailed()
    {
        var coordinator = new MicrophoneActionCoordinator();

        var result = await coordinator.ExecuteAsync(
            DesiredMicrophoneState.Muted,
            controlProvider: () => Task.FromResult((
                found: true,
                currentState: TeamsControlState.Unmuted,
                hasPattern: true,
                isEnabled: true,
                invokeAction: (Action)(() => throw new InvalidOperationException("UIA COM element inaccessible")))),
            reQueryStateFunc: () => Task.FromResult(TeamsControlState.Muted),
            verificationTimeout: TimeSpan.FromSeconds(1));

        Assert.Equal(CommandExecutionStatus.Failed, result.Status);
        Assert.Equal("INVOKE_FAILED", result.ErrorCode);
        Assert.Contains("Failed to invoke pattern", result.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
