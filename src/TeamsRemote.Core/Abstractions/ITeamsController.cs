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
}
