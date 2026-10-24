using TeamsRemote.Core.Adapters;
using TeamsRemote.Core.Models;
using Xunit;

namespace TeamsRemote.Tests;

public class DuplicateActionPreventionTests
{
    [Fact]
    public async Task ExecuteAsync_WhenCommandIsInFlight_RejectsConcurrentRequestImmediately()
    {
        var coordinator = new MicrophoneActionCoordinator();
        var holdTcs = new TaskCompletionSource();

        // Launch first command that holds the execution lock
        var firstTask = coordinator.ExecuteAsync(
            DesiredMicrophoneState.Muted,
            controlProvider: async () =>
            {
                await holdTcs.Task;
                return (
                    found: true,
                    currentState: TeamsControlState.Unmuted,
                    hasPattern: true,
                    isEnabled: true,
                    invokeAction: () => { });
            },
            reQueryStateFunc: () => Task.FromResult(TeamsControlState.Muted),
            verificationTimeout: TimeSpan.FromSeconds(1));

        // Yield execution to ensure firstTask enters and acquires lock
        await Task.Delay(20);

        // Second command should be rejected immediately with COMMAND_IN_PROGRESS
        var secondResult = await coordinator.ExecuteAsync(
            DesiredMicrophoneState.Muted,
            controlProvider: () => Task.FromResult((
                found: true,
                currentState: TeamsControlState.Unmuted,
                hasPattern: true,
                isEnabled: true,
                invokeAction: (Action)(() => { }))),
            reQueryStateFunc: () => Task.FromResult(TeamsControlState.Muted),
            verificationTimeout: TimeSpan.FromSeconds(1));

        Assert.Equal(CommandExecutionStatus.Failed, secondResult.Status);
        Assert.Equal("COMMAND_IN_PROGRESS", secondResult.ErrorCode);
        Assert.Contains("currently executing", secondResult.Reason, StringComparison.OrdinalIgnoreCase);

        // Release first command and await its completion
        holdTcs.SetResult();
        var firstResult = await firstTask;

        Assert.Equal(CommandExecutionStatus.Confirmed, firstResult.Status);

        // Third command after release should now succeed
        var thirdResult = await coordinator.ExecuteAsync(
            DesiredMicrophoneState.Unmuted,
            controlProvider: () => Task.FromResult((
                found: true,
                currentState: TeamsControlState.Muted,
                hasPattern: true,
                isEnabled: true,
                invokeAction: (Action)(() => { }))),
            reQueryStateFunc: () => Task.FromResult(TeamsControlState.Unmuted),
            verificationTimeout: TimeSpan.FromSeconds(1));

        Assert.Equal(CommandExecutionStatus.Confirmed, thirdResult.Status);
    }
}
