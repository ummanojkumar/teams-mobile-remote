using TeamsRemote.Core.Models;
using TeamsRemote.Core.State;
using Xunit;

namespace TeamsRemote.Tests;

public class MeetingStateStoreTests
{
    [Fact]
    public void InitialState_IsUnknownAndZeroRevision()
    {
        var store = new MeetingStateStore();
        var (rev, state, conf, meeting) = store.GetSnapshot();

        Assert.Equal(0, rev);
        Assert.Equal(TeamsControlState.Unknown, state);
        Assert.Equal(StateConfidence.Unknown, conf);
        Assert.Equal("none", meeting);
    }

    [Fact]
    public void UpdateObserved_Confirmed_IncrementsRevisionAndSetsConfirmed()
    {
        var store = new MeetingStateStore();
        bool eventFired = false;
        store.StateChanged += () => eventFired = true;

        store.UpdateObserved(TeamsControlState.Unmuted, isConfirmed: true, meetingStatus: "active");

        var (rev, state, conf, meeting) = store.GetSnapshot();
        Assert.Equal(1, rev);
        Assert.Equal(TeamsControlState.Unmuted, state);
        Assert.Equal(StateConfidence.Confirmed, conf);
        Assert.Equal("active", meeting);
        Assert.True(eventFired);
    }

    [Fact]
    public void MarkPending_PreservesCurrentStateAndSetsPending()
    {
        var store = new MeetingStateStore();
        store.UpdateObserved(TeamsControlState.Muted, isConfirmed: true);

        store.MarkPending();

        var (rev, state, conf, _) = store.GetSnapshot();
        Assert.Equal(1, rev);
        Assert.Equal(TeamsControlState.Muted, state);
        Assert.Equal(StateConfidence.Pending, conf);
    }

    [Fact]
    public void MarkStaleOrUncertain_PreservesCurrentStateAndSetsStale()
    {
        var store = new MeetingStateStore();
        store.UpdateObserved(TeamsControlState.Muted, isConfirmed: true);

        store.MarkStaleOrUncertain();

        var (rev, state, conf, _) = store.GetSnapshot();
        Assert.Equal(1, rev);
        Assert.Equal(TeamsControlState.Muted, state);
        Assert.Equal(StateConfidence.Stale, conf);
    }

    [Fact]
    public void Reset_ClearsStateRevisionAndResetsToUnknown()
    {
        var store = new MeetingStateStore();
        store.UpdateObserved(TeamsControlState.Unmuted, isConfirmed: true);

        store.Reset();

        var (rev, state, conf, meeting) = store.GetSnapshot();
        Assert.Equal(0, rev);
        Assert.Equal(TeamsControlState.Unknown, state);
        Assert.Equal(StateConfidence.Unknown, conf);
        Assert.Equal("none", meeting);
    }
}
