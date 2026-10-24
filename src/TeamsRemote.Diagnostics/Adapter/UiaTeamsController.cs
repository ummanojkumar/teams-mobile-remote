using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.EventHandlers;
using FlaUI.UIA3;
using TeamsRemote.Core.Abstractions;
using TeamsRemote.Core.Classification;
using TeamsRemote.Core.Models;
using TeamsRemote.Core.Redaction;

namespace TeamsRemote.Diagnostics.Adapter;

public sealed class UiaTeamsController : ITeamsController
{
    private static readonly string[] CandidateProcessNames = { "ms-teams", "Teams" };
    private readonly UIA3Automation _automation;
    private bool _disposed;

    public UiaTeamsController()
    {
        _automation = new UIA3Automation();
    }

    public Task<TeamsDiagnosticReport> InspectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var processInfo = DetectTeamsProcess();
        if (!processInfo.IsRunning)
        {
            var emptyReport = new TeamsDiagnosticReport
            {
                Process = processInfo,
                MeetingSurface = new MeetingSurfaceInfo
                {
                    IsDetected = false,
                    WindowHandle = 0,
                    RedactedWindowTitle = null,
                    AutomationClassName = null,
                    ControlSurfaceType = null
                },
                Controls = new[]
                {
                    CreateMissingControl(TargetControl.Microphone, "Microsoft Teams is not running."),
                    CreateMissingControl(TargetControl.Camera, "Microsoft Teams is not running."),
                    CreateMissingControl(TargetControl.Hand, "Microsoft Teams is not running.")
                },
                DiagnosticSummary = "Microsoft Teams process is not running. No meeting surface or controls could be inspected."
            };

            return Task.FromResult(RedactionHelper.SanitizeReport(emptyReport));
        }

        // Find candidate meeting window
        var (meetingSurface, meetingWindow) = FindMeetingSurface(processInfo.AllProcessIds);

        if (!meetingSurface.IsDetected || meetingWindow == null)
        {
            var reportNoMeeting = new TeamsDiagnosticReport
            {
                Process = processInfo,
                MeetingSurface = meetingSurface,
                Controls = new[]
                {
                    CreateMissingControl(TargetControl.Microphone, "No active meeting window or control surface detected."),
                    CreateMissingControl(TargetControl.Camera, "No active meeting window or control surface detected."),
                    CreateMissingControl(TargetControl.Hand, "No active meeting window or control surface detected.")
                },
                DiagnosticSummary = "Microsoft Teams is running, but no active meeting window or call control surface was detected."
            };

            return Task.FromResult(RedactionHelper.SanitizeReport(reportNoMeeting));
        }

        // Search for meeting controls within the meeting window
        var controls = InspectControls(meetingWindow);

        var report = new TeamsDiagnosticReport
        {
            Process = processInfo,
            MeetingSurface = meetingSurface,
            Controls = controls,
            DiagnosticSummary = $"Active meeting control surface detected. Inspected {controls.Count} target controls."
        };

        return Task.FromResult(RedactionHelper.SanitizeReport(report));
    }

    public async Task ObserveEventsAsync(
        TimeSpan duration,
        Action<ControlEventReport> onEventObserved,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var processInfo = DetectTeamsProcess();
        if (!processInfo.IsRunning)
        {
            return;
        }

        var (_, meetingWindow) = FindMeetingSurface(processInfo.AllProcessIds);
        if (meetingWindow == null)
        {
            return;
        }

        var controlElements = FindControlElements(meetingWindow);
        var registeredHandlers = new List<IDisposable>();

        try
        {
            foreach (var (targetControl, element) in controlElements)
            {
                if (element == null) continue;

                try
                {
                    // Register property changed event for Name and ToggleState if supported
                    var propHandler = element.RegisterPropertyChangedEvent(
                        TreeScope.Element,
                        (automationElement, propertyId, newValue) =>
                        {
                            string propName = propertyId.ToString();
                            string? valStr = newValue?.ToString();

                            var evtReport = new ControlEventReport
                            {
                                ObservedAt = DateTimeOffset.UtcNow,
                                TargetControl = targetControl,
                                PropertyName = propName,
                                NewValue = valStr,
                                RedactedSummary = $"State change observed on {targetControl}: {propName} = {valStr}"
                            };

                            onEventObserved(evtReport);
                        },
                        _automation.PropertyLibrary.Element.Name,
                        _automation.PropertyLibrary.Toggle.ToggleState);

                    if (propHandler != null)
                    {
                        registeredHandlers.Add(propHandler);
                    }
                }
                catch
                {
                    // Some elements or UIA providers might not allow event subscription
                }
            }

            // Wait for specified duration or cancellation
            try
            {
                await Task.Delay(duration, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Normal exit on cancellation
            }
        }
        finally
        {
            foreach (var handler in registeredHandlers)
            {
                try
                {
                    handler.Dispose();
                }
                catch
                {
                    // Ignore disposal errors on teardown
                }
            }
        }
    }

    private TeamsProcessInfo DetectTeamsProcess()
    {
        var matchingProcesses = new List<Process>();
        foreach (var name in CandidateProcessNames)
        {
            try
            {
                matchingProcesses.AddRange(Process.GetProcessesByName(name));
            }
            catch
            {
                // Process enumeration security restriction or exit
            }
        }

        if (matchingProcesses.Count == 0)
        {
            return new TeamsProcessInfo { IsRunning = false };
        }

        var primaryProcess = matchingProcesses[0];
        string? exePath = null;
        try
        {
            exePath = primaryProcess.MainModule?.FileName;
        }
        catch
        {
            // May fail for packaged / protected apps
        }

        return new TeamsProcessInfo
        {
            IsRunning = true,
            ProcessName = primaryProcess.ProcessName,
            ProcessId = primaryProcess.Id,
            ExecutablePath = exePath,
            AllProcessIds = matchingProcesses.Select(p => p.Id).Distinct().ToList()
        };
    }

    private (MeetingSurfaceInfo Surface, AutomationElement? WindowElement) FindMeetingSurface(IReadOnlyList<int> processIds)
    {
        try
        {
            var desktop = _automation.GetDesktop();
            var cf = _automation.ConditionFactory;

            // Search top-level windows belonging to any of the Teams processes
            var candidateWindows = desktop.FindAllChildren(cf.ByControlType(FlaUI.Core.Definitions.ControlType.Window));

            AutomationElement? bestMeetingWindow = null;
            string? bestWindowTitle = null;
            string? bestClassName = null;

            foreach (var window in candidateWindows)
            {
                try
                {
                    int winPid = window.Properties.ProcessId.ValueOrDefault;
                    if (!processIds.Contains(winPid))
                    {
                        continue;
                    }

                    string title = window.Properties.Name.ValueOrDefault ?? string.Empty;
                    string className = window.Properties.ClassName.ValueOrDefault ?? string.Empty;

                    // Check if this window hosts meeting controls
                    var elements = FindControlElements(window);
                    bool hasMeetingControls = elements.Values.Any(v => v != null);

                    if (hasMeetingControls)
                    {
                        bestMeetingWindow = window;
                        bestWindowTitle = title;
                        bestClassName = className;
                        break;
                    }

                    // Secondary check: window title with Teams meeting pattern
                    if (title.Contains("| Microsoft Teams", StringComparison.OrdinalIgnoreCase))
                    {
                        bestMeetingWindow = window;
                        bestWindowTitle = title;
                        bestClassName = className;
                    }
                }
                catch
                {
                    // Handle stale or closed window during enumeration
                }
            }

            if (bestMeetingWindow != null)
            {
                return (
                    new MeetingSurfaceInfo
                    {
                        IsDetected = true,
                        WindowHandle = (long)bestMeetingWindow.Properties.NativeWindowHandle.ValueOrDefault,
                        RedactedWindowTitle = RedactionHelper.RedactWindowTitle(bestWindowTitle),
                        AutomationClassName = bestClassName,
                        ControlSurfaceType = "MeetingWindow"
                    },
                    bestMeetingWindow);
            }
        }
        catch
        {
            // Fallback on UIA traversal failure
        }

        return (
            new MeetingSurfaceInfo
            {
                IsDetected = false,
                WindowHandle = 0,
                RedactedWindowTitle = null,
                AutomationClassName = null,
                ControlSurfaceType = null
            },
            null);
    }

    private Dictionary<TargetControl, AutomationElement?> FindControlElements(AutomationElement rootWindow)
    {
        var result = new Dictionary<TargetControl, AutomationElement?>
        {
            [TargetControl.Microphone] = null,
            [TargetControl.Camera] = null,
            [TargetControl.Hand] = null
        };

        try
        {
            var cf = _automation.ConditionFactory;
            // Narrow query to clickable control types
            var condition = cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button)
                .Or(cf.ByControlType(FlaUI.Core.Definitions.ControlType.SplitButton))
                .Or(cf.ByControlType(FlaUI.Core.Definitions.ControlType.CheckBox));

            var candidates = rootWindow.FindAllDescendants(condition);

            foreach (var el in candidates)
            {
                try
                {
                    string name = el.Properties.Name.ValueOrDefault ?? string.Empty;
                    string autoId = el.Properties.AutomationId.ValueOrDefault ?? string.Empty;

                    // Match Microphone
                    if (result[TargetControl.Microphone] == null && IsMicrophoneControl(name, autoId))
                    {
                        result[TargetControl.Microphone] = el;
                    }

                    // Match Camera
                    if (result[TargetControl.Camera] == null && IsCameraControl(name, autoId))
                    {
                        result[TargetControl.Camera] = el;
                    }

                    // Match Hand
                    if (result[TargetControl.Hand] == null && IsHandControl(name, autoId))
                    {
                        result[TargetControl.Hand] = el;
                    }

                    if (result.Values.All(v => v != null))
                    {
                        break;
                    }
                }
                catch
                {
                    // Stale element during crawl
                }
            }
        }
        catch
        {
            // Handle query error
        }

        return result;
    }

    private List<ControlDiagnosticInfo> InspectControls(AutomationElement meetingWindow)
    {
        var elements = FindControlElements(meetingWindow);
        var reports = new List<ControlDiagnosticInfo>();

        foreach (var target in new[] { TargetControl.Microphone, TargetControl.Camera, TargetControl.Hand })
        {
            var el = elements[target];
            if (el == null)
            {
                reports.Add(CreateMissingControl(target, $"Control element was not found in meeting window."));
                continue;
            }

            try
            {
                string name = el.Properties.Name.ValueOrDefault ?? string.Empty;
                string autoId = el.Properties.AutomationId.ValueOrDefault ?? string.Empty;
                string controlType = el.Properties.ControlType.ValueOrDefault.ToString();
                bool isEnabled = el.Properties.IsEnabled.ValueOrDefault;
                string? helpText = el.Properties.HelpText.ValueOrDefault;

                var supportedPatterns = new List<string>();
                string? toggleStateStr = null;

                if (el.Patterns.Toggle.IsSupported)
                {
                    supportedPatterns.Add("TogglePattern");
                    try
                    {
                        var togglePattern = el.Patterns.Toggle.PatternOrDefault;
                        if (togglePattern != null)
                        {
                            toggleStateStr = togglePattern.ToggleState.ValueOrDefault.ToString();
                        }
                    }
                    catch
                    {
                        // Pattern read error
                    }
                }

                if (el.Patterns.Invoke.IsSupported)
                {
                    supportedPatterns.Add("InvokePattern");
                }

                if (el.Patterns.SelectionItem.IsSupported)
                {
                    supportedPatterns.Add("SelectionItemPattern");
                }

                // Parse state and source
                var (readableState, stateSource) = ControlCapabilityClassifier.ParseState(
                    target,
                    name,
                    toggleStateStr,
                    helpText);

                // Classify capability tier
                var (tier, reason) = ControlCapabilityClassifier.Classify(
                    isFound: true,
                    isEnabled: isEnabled,
                    readableState: readableState,
                    supportedPatterns: supportedPatterns);

                // Check event observability capability
                bool isEventObservable = el.Patterns.Toggle.IsSupported || !string.IsNullOrEmpty(name);

                reports.Add(new ControlDiagnosticInfo
                {
                    TargetControl = target,
                    Found = true,
                    AccessibleName = RedactionHelper.SanitizeAccessibleName(name, target),
                    ControlType = controlType,
                    AutomationId = autoId,
                    SupportedPatterns = supportedPatterns,
                    ReadableState = readableState,
                    StateSource = stateSource,
                    IsEventObservable = isEventObservable,
                    CapabilityTier = tier,
                    Reason = reason
                });
            }
            catch (Exception ex)
            {
                reports.Add(CreateMissingControl(target, $"Error inspecting control: {ex.Message}"));
            }
        }

        return reports;
    }

    private static bool IsMicrophoneControl(string name, string autoId)
    {
        string n = name.ToLowerInvariant();
        string id = autoId.ToLowerInvariant();

        return id.Contains("microphone") || id.Contains("mic-button") ||
               n.Contains("mute microphone") || n.Contains("unmute microphone") ||
               (n.Contains("microphone") && (n.Contains("mute") || n.Contains("audio")));
    }

    private static bool IsCameraControl(string name, string autoId)
    {
        string n = name.ToLowerInvariant();
        string id = autoId.ToLowerInvariant();

        return id.Contains("video") || id.Contains("camera") ||
               n.Contains("turn camera on") || n.Contains("turn camera off") ||
               n.Contains("turn off camera") || n.Contains("turn on camera") ||
               n.Contains("stop video") || n.Contains("start video");
    }

    private static bool IsHandControl(string name, string autoId)
    {
        string n = name.ToLowerInvariant();
        string id = autoId.ToLowerInvariant();

        return id.Contains("raise-hand") || id.Contains("hand-button") ||
               n.Contains("raise hand") || n.Contains("lower hand") ||
               n.Contains("raise your hand") || n.Contains("lower your hand") ||
               n.Contains("react and raise hand");
    }

    private static ControlDiagnosticInfo CreateMissingControl(TargetControl target, string reason)
    {
        return new ControlDiagnosticInfo
        {
            TargetControl = target,
            Found = false,
            AccessibleName = null,
            ControlType = null,
            AutomationId = null,
            SupportedPatterns = Array.Empty<string>(),
            ReadableState = TeamsControlState.Unknown,
            StateSource = null,
            IsEventObservable = false,
            CapabilityTier = ControlCapabilityTier.Unavailable,
            Reason = reason
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _automation.Dispose();
            _disposed = true;
        }
    }
}
