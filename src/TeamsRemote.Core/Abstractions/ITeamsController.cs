using TeamsRemote.Core.Models;

namespace TeamsRemote.Core.Abstractions;

public interface ITeamsController : IDisposable
{
    /// <summary>
    /// Inspects the current Teams desktop application, active meeting control surface, and meeting controls
    /// without taking any synthetic actions, keystrokes, or clicks.
    /// </summary>
    Task<TeamsDiagnosticReport> InspectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Passively observes state-change events on discovered meeting controls for a specified duration
    /// without taking any actions.
    /// </summary>
    Task ObserveEventsAsync(
        TimeSpan duration,
        Action<ControlEventReport> onEventObserved,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an explicit desired-state transition for the microphone (muted/unmuted).
    /// Pre-action state is checked, action is invoked only if needed, and the outcome
    /// is verified via UIA event or bounded re-query before returning confirmed.
    /// </summary>
    Task<MicrophoneCommandResult> SetMicrophoneStateAsync(
        DesiredMicrophoneState desiredState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an explicit desired-state transition for the camera (on/off).
    /// Pre-action state is checked, action is invoked only if needed, and the outcome
    /// is verified via UIA event or bounded re-query before returning confirmed.
    /// </summary>
    Task<CameraCommandResult> SetCameraStateAsync(
        DesiredCameraState desiredState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an explicit desired-state transition for Raise Hand (raised/lowered).
    /// Pre-action state is checked, action is invoked only if needed, and the outcome
    /// is verified via UIA event or bounded re-query before returning confirmed.
    /// </summary>
    Task<HandCommandResult> SetHandStateAsync(
        DesiredHandState desiredState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to microphone state-change events on the active meeting control.
    /// Invokes onStateChangeDetected when a UIA property change (e.g. Name or ToggleState) is raised.
    /// Returns an IDisposable that unregisters the UIA event handlers, or null if subscription failed.
    /// </summary>
    IDisposable? SubscribeMicrophoneStateChanged(Action onStateChangeDetected);

    /// <summary>
    /// Subscribes to camera state-change events on the active meeting control.
    /// Invokes onStateChangeDetected when a UIA property change (e.g. Name or ToggleState) is raised.
    /// Returns an IDisposable that unregisters the UIA event handlers, or null if subscription failed.
    /// </summary>
    IDisposable? SubscribeCameraStateChanged(Action onStateChangeDetected);

    /// <summary>
    /// Subscribes to Raise Hand state-change events on the active meeting control.
    /// Invokes onStateChangeDetected when a UIA property change (e.g. Name or ToggleState) is raised.
    /// Returns an IDisposable that unregisters the UIA event handlers, or null if subscription failed.
    /// </summary>
    IDisposable? SubscribeHandStateChanged(Action onStateChangeDetected);

    /// <summary>
    /// Passively observes the Teams Live Reactions toolbar and flyout menu elements during a user-assisted observation session.
    /// Strictly read-only: does not invoke the React launcher or any emoji buttons.
    /// </summary>
    Task<ReactionDiagnosticReport> ObserveReactionsAsync(
        TimeSpan duration,
        Action<string>? onProgressMessage = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a remote Live Reaction invocation (like, love, applause, laugh, surprise).
    /// Opens the reactions flyout if needed, locates the target button, and triggers it via InvokePattern.
    /// </summary>
    Task<ReactionCommandResult> SendReactionAsync(
        string reaction,
        CancellationToken cancellationToken = default);
}
