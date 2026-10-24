using TeamsRemote.Core.Classification;
using TeamsRemote.Core.Models;
using Xunit;

namespace TeamsRemote.Tests;

public class ControlStateParsingTests
{
    [Theory]
    [InlineData("Mute microphone (Ctrl+Shift+M)", TeamsControlState.Unmuted)]
    [InlineData("Mute microphone", TeamsControlState.Unmuted)]
    [InlineData("Mute mic", TeamsControlState.Unmuted)]
    [InlineData("Unmute microphone (Ctrl+Shift+M)", TeamsControlState.Muted)]
    [InlineData("Unmute microphone", TeamsControlState.Muted)]
    public void ParseState_Microphone_AccessibleName_DeterminesState(string accessibleName, string expectedState)
    {
        var (state, source) = ControlCapabilityClassifier.ParseState(
            TargetControl.Microphone,
            accessibleName: accessibleName,
            toggleState: null);

        Assert.Equal(expectedState, state);
        Assert.Equal("AccessibleName", source);
    }

    [Theory]
    [InlineData("On", TeamsControlState.Unmuted)]
    [InlineData("Off", TeamsControlState.Muted)]
    public void ParseState_Microphone_ToggleState_DeterminesStateWhenNameIsGeneric(string toggleState, string expectedState)
    {
        var (state, source) = ControlCapabilityClassifier.ParseState(
            TargetControl.Microphone,
            accessibleName: "Microphone",
            toggleState: toggleState);

        Assert.Equal(expectedState, state);
        Assert.Equal("TogglePattern", source);
    }

    [Theory]
    [InlineData("Turn camera on (Ctrl+Shift+O)", TeamsControlState.Off)]
    [InlineData("Turn camera on", TeamsControlState.Off)]
    [InlineData("Turn camera off (Ctrl+Shift+O)", TeamsControlState.On)]
    [InlineData("Turn camera off", TeamsControlState.On)]
    public void ParseState_Camera_AccessibleName_DeterminesState(string accessibleName, string expectedState)
    {
        var (state, source) = ControlCapabilityClassifier.ParseState(
            TargetControl.Camera,
            accessibleName: accessibleName,
            toggleState: null);

        Assert.Equal(expectedState, state);
        Assert.Equal("AccessibleName", source);
    }

    [Theory]
    [InlineData("On", TeamsControlState.On)]
    [InlineData("Off", TeamsControlState.Off)]
    public void ParseState_Camera_ToggleState_DeterminesState(string toggleState, string expectedState)
    {
        var (state, source) = ControlCapabilityClassifier.ParseState(
            TargetControl.Camera,
            accessibleName: "Camera",
            toggleState: toggleState);

        Assert.Equal(expectedState, state);
        Assert.Equal("TogglePattern", source);
    }

    [Theory]
    [InlineData("Raise hand (Ctrl+Shift+K)", TeamsControlState.Lowered)]
    [InlineData("Raise hand", TeamsControlState.Lowered)]
    [InlineData("Lower hand (Ctrl+Shift+K)", TeamsControlState.Raised)]
    [InlineData("Lower hand", TeamsControlState.Raised)]
    public void ParseState_Hand_AccessibleName_DeterminesState(string accessibleName, string expectedState)
    {
        var (state, source) = ControlCapabilityClassifier.ParseState(
            TargetControl.Hand,
            accessibleName: accessibleName,
            toggleState: null);

        Assert.Equal(expectedState, state);
        Assert.Equal("AccessibleName", source);
    }

    [Theory]
    [InlineData("On", TeamsControlState.Raised)]
    [InlineData("Off", TeamsControlState.Lowered)]
    public void ParseState_Hand_ToggleState_DeterminesState(string toggleState, string expectedState)
    {
        var (state, source) = ControlCapabilityClassifier.ParseState(
            TargetControl.Hand,
            accessibleName: "Reactions",
            toggleState: toggleState);

        Assert.Equal(expectedState, state);
        Assert.Equal("TogglePattern", source);
    }

    [Fact]
    public void ParseState_WhenNoInfoMatches_ReturnsUnknown()
    {
        var (state, source) = ControlCapabilityClassifier.ParseState(
            TargetControl.Microphone,
            accessibleName: "Settings button",
            toggleState: null);

        Assert.Equal(TeamsControlState.Unknown, state);
        Assert.Equal("None", source);
    }
}
