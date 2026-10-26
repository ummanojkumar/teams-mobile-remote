using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using TeamsRemote.Core.Abstractions;
using TeamsRemote.Core.Classification;
using TeamsRemote.Core.Models;
using TeamsRemote.Core.Protocol;
using TeamsRemote.Core.Redaction;

namespace TeamsRemote.Core.Adapters;

public sealed class UiaTeamsController : ITeamsController
{
    private static readonly string[] CandidateProcessNames = { "ms-teams", "Teams" };
    private readonly UIA3Automation _automation;
    private readonly SemaphoreSlim _actionLock = new(1, 1);
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

    private readonly MicrophoneActionCoordinator _coordinator = new();
    private readonly CameraActionCoordinator _cameraCoordinator = new();
    private readonly HandActionCoordinator _handCoordinator = new();

    public async Task<MicrophoneCommandResult> SetMicrophoneStateAsync(
        DesiredMicrophoneState desiredState,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        return await _coordinator.ExecuteAsync(
            desiredState,
            controlProvider: () =>
            {
                var processInfo = DetectTeamsProcess();
                if (!processInfo.IsRunning)
                {
                    return Task.FromResult((
                        found: false,
                        currentState: TeamsControlState.Unknown,
                        hasPattern: false,
                        isEnabled: false,
                        invokeAction: (Action)(() => { })));
                }

                var (meetingSurface, meetingWindow) = FindMeetingSurface(processInfo.AllProcessIds);
                if (!meetingSurface.IsDetected || meetingWindow == null)
                {
                    return Task.FromResult((
                        found: false,
                        currentState: TeamsControlState.Unknown,
                        hasPattern: false,
                        isEnabled: false,
                        invokeAction: (Action)(() => { })));
                }

                var controls = FindControlElements(meetingWindow);
                var micElement = controls[TargetControl.Microphone];
                if (micElement == null)
                {
                    return Task.FromResult((
                        found: false,
                        currentState: TeamsControlState.Unknown,
                        hasPattern: false,
                        isEnabled: false,
                        invokeAction: (Action)(() => { })));
                }

                string currentName = micElement.Properties.Name.ValueOrDefault ?? string.Empty;
                string? currentToggle = micElement.Patterns.Toggle.IsSupported
                    ? micElement.Patterns.Toggle.PatternOrDefault?.ToggleState.ValueOrDefault.ToString()
                    : null;
                string? currentHelp = micElement.Properties.HelpText.ValueOrDefault;

                var (currentState, _) = ControlCapabilityClassifier.ParseState(
                    TargetControl.Microphone,
                    currentName,
                    currentToggle,
                    currentHelp);

                bool hasToggle = micElement.Patterns.Toggle.IsSupported;
                bool hasInvoke = micElement.Patterns.Invoke.IsSupported;
                bool isEnabled = micElement.Properties.IsEnabled.ValueOrDefault;

                Action invokeAction = () =>
                {
                    if (hasToggle)
                    {
                        micElement.Patterns.Toggle.PatternOrDefault?.Toggle();
                    }
                    else
                    {
                        micElement.Patterns.Invoke.PatternOrDefault?.Invoke();
                    }
                };

                return Task.FromResult((
                    found: true,
                    currentState,
                    hasPattern: hasToggle || hasInvoke,
                    isEnabled,
                    invokeAction));
            },
            reQueryStateFunc: () =>
            {
                var processInfo = DetectTeamsProcess();
                if (!processInfo.IsRunning)
                {
                    return Task.FromResult(TeamsControlState.Unknown);
                }

                var (_, meetingWindow) = FindMeetingSurface(processInfo.AllProcessIds);
                if (meetingWindow == null)
                {
                    return Task.FromResult(TeamsControlState.Unknown);
                }

                var controls = FindControlElements(meetingWindow);
                var micElement = controls[TargetControl.Microphone];
                if (micElement == null)
                {
                    return Task.FromResult(TeamsControlState.Unknown);
                }

                string name = micElement.Properties.Name.ValueOrDefault ?? string.Empty;
                string? toggle = micElement.Patterns.Toggle.IsSupported
                    ? micElement.Patterns.Toggle.PatternOrDefault?.ToggleState.ValueOrDefault.ToString()
                    : null;

                var (state, _) = ControlCapabilityClassifier.ParseState(TargetControl.Microphone, name, toggle);
                return Task.FromResult(state);
            },
            verificationTimeout: TimeSpan.FromSeconds(2),
            cancellationToken: cancellationToken);
    }

    public async Task<CameraCommandResult> SetCameraStateAsync(
        DesiredCameraState desiredState,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        return await _cameraCoordinator.ExecuteAsync(
            desiredState,
            controlProvider: () =>
            {
                var processInfo = DetectTeamsProcess();
                if (!processInfo.IsRunning)
                {
                    return Task.FromResult((
                        found: false,
                        currentState: TeamsControlState.Unknown,
                        hasPattern: false,
                        isEnabled: false,
                        invokeAction: (Action)(() => { }),
                        fingerprint: (string?)null));
                }

                var (meetingSurface, meetingWindow) = FindMeetingSurface(processInfo.AllProcessIds);
                if (!meetingSurface.IsDetected || meetingWindow == null)
                {
                    return Task.FromResult((
                        found: false,
                        currentState: TeamsControlState.Unknown,
                        hasPattern: false,
                        isEnabled: false,
                        invokeAction: (Action)(() => { }),
                        fingerprint: (string?)null));
                }

                var controls = FindControlElements(meetingWindow);
                var cameraElement = controls[TargetControl.Camera];
                if (cameraElement == null)
                {
                    return Task.FromResult((
                        found: false,
                        currentState: TeamsControlState.Unknown,
                        hasPattern: false,
                        isEnabled: false,
                        invokeAction: (Action)(() => { }),
                        fingerprint: (string?)null));
                }

                string currentName = cameraElement.Properties.Name.ValueOrDefault ?? string.Empty;
                string? currentToggle = cameraElement.Patterns.Toggle.IsSupported
                    ? cameraElement.Patterns.Toggle.PatternOrDefault?.ToggleState.ValueOrDefault.ToString()
                    : null;
                string? currentHelp = cameraElement.Properties.HelpText.ValueOrDefault;

                var (currentState, _) = ControlCapabilityClassifier.ParseState(
                    TargetControl.Camera,
                    currentName,
                    currentToggle,
                    currentHelp);

                bool hasToggle = cameraElement.Patterns.Toggle.IsSupported;
                bool hasInvoke = cameraElement.Patterns.Invoke.IsSupported;
                bool isEnabled = cameraElement.Properties.IsEnabled.ValueOrDefault;

                string fingerprint = $"{meetingSurface.WindowHandle}_{cameraElement.Properties.AutomationId.ValueOrDefault}_{cameraElement.Properties.ControlType.ValueOrDefault}";

                Action invokeAction = () =>
                {
                    if (hasToggle)
                    {
                        cameraElement.Patterns.Toggle.PatternOrDefault?.Toggle();
                    }
                    else
                    {
                        cameraElement.Patterns.Invoke.PatternOrDefault?.Invoke();
                    }
                };

                return Task.FromResult((
                    found: true,
                    currentState,
                    hasPattern: hasToggle || hasInvoke,
                    isEnabled,
                    invokeAction,
                    fingerprint: (string?)fingerprint));
            },
            reQueryStateFunc: () =>
            {
                var processInfo = DetectTeamsProcess();
                if (!processInfo.IsRunning)
                {
                    return Task.FromResult(TeamsControlState.Unknown);
                }

                var (_, meetingWindow) = FindMeetingSurface(processInfo.AllProcessIds);
                if (meetingWindow == null)
                {
                    return Task.FromResult(TeamsControlState.Unknown);
                }

                var controls = FindControlElements(meetingWindow);
                var cameraElement = controls[TargetControl.Camera];
                if (cameraElement == null)
                {
                    return Task.FromResult(TeamsControlState.Unknown);
                }

                string name = cameraElement.Properties.Name.ValueOrDefault ?? string.Empty;
                string? toggle = cameraElement.Patterns.Toggle.IsSupported
                    ? cameraElement.Patterns.Toggle.PatternOrDefault?.ToggleState.ValueOrDefault.ToString()
                    : null;

                var (state, _) = ControlCapabilityClassifier.ParseState(TargetControl.Camera, name, toggle);
                return Task.FromResult(state);
            },
            verificationTimeout: TimeSpan.FromSeconds(2),
            cancellationToken: cancellationToken);
    }

    public async Task<HandCommandResult> SetHandStateAsync(
        DesiredHandState desiredState,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        return await _handCoordinator.ExecuteAsync(
            desiredState,
            controlProvider: () =>
            {
                var processInfo = DetectTeamsProcess();
                if (!processInfo.IsRunning)
                {
                    return Task.FromResult((
                        found: false,
                        currentState: TeamsControlState.Unknown,
                        hasPattern: false,
                        isEnabled: false,
                        invokeAction: (Action)(() => { }),
                        fingerprint: (string?)null));
                }

                var (meetingSurface, meetingWindow) = FindMeetingSurface(processInfo.AllProcessIds);
                if (!meetingSurface.IsDetected || meetingWindow == null)
                {
                    return Task.FromResult((
                        found: false,
                        currentState: TeamsControlState.Unknown,
                        hasPattern: false,
                        isEnabled: false,
                        invokeAction: (Action)(() => { }),
                        fingerprint: (string?)null));
                }

                var controls = FindControlElements(meetingWindow);
                var handElement = controls[TargetControl.Hand];
                if (handElement == null)
                {
                    return Task.FromResult((
                        found: false,
                        currentState: TeamsControlState.Unknown,
                        hasPattern: false,
                        isEnabled: false,
                        invokeAction: (Action)(() => { }),
                        fingerprint: (string?)null));
                }

                string currentName = handElement.Properties.Name.ValueOrDefault ?? string.Empty;
                string? currentToggle = handElement.Patterns.Toggle.IsSupported
                    ? handElement.Patterns.Toggle.PatternOrDefault?.ToggleState.ValueOrDefault.ToString()
                    : null;
                string? currentHelp = handElement.Properties.HelpText.ValueOrDefault;

                var (currentState, _) = ControlCapabilityClassifier.ParseState(
                    TargetControl.Hand,
                    currentName,
                    currentToggle,
                    currentHelp);

                bool hasToggle = handElement.Patterns.Toggle.IsSupported;
                bool hasInvoke = handElement.Patterns.Invoke.IsSupported;
                bool isEnabled = handElement.Properties.IsEnabled.ValueOrDefault;

                string fingerprint = $"{meetingSurface.WindowHandle}_{handElement.Properties.AutomationId.ValueOrDefault}_{handElement.Properties.ControlType.ValueOrDefault}";

                Action invokeAction = () =>
                {
                    if (hasToggle)
                    {
                        handElement.Patterns.Toggle.PatternOrDefault?.Toggle();
                    }
                    else
                    {
                        handElement.Patterns.Invoke.PatternOrDefault?.Invoke();
                    }
                };

                return Task.FromResult((
                    found: true,
                    currentState,
                    hasPattern: hasToggle || hasInvoke,
                    isEnabled,
                    invokeAction,
                    fingerprint: (string?)fingerprint));
            },
            reQueryStateFunc: () =>
            {
                var processInfo = DetectTeamsProcess();
                if (!processInfo.IsRunning)
                {
                    return Task.FromResult(TeamsControlState.Unknown);
                }

                var (_, meetingWindow) = FindMeetingSurface(processInfo.AllProcessIds);
                if (meetingWindow == null)
                {
                    return Task.FromResult(TeamsControlState.Unknown);
                }

                var controls = FindControlElements(meetingWindow);
                var handElement = controls[TargetControl.Hand];
                if (handElement == null)
                {
                    return Task.FromResult(TeamsControlState.Unknown);
                }

                string name = handElement.Properties.Name.ValueOrDefault ?? string.Empty;
                string? toggle = handElement.Patterns.Toggle.IsSupported
                    ? handElement.Patterns.Toggle.PatternOrDefault?.ToggleState.ValueOrDefault.ToString()
                    : null;

                var (state, _) = ControlCapabilityClassifier.ParseState(TargetControl.Hand, name, toggle);
                return Task.FromResult(state);
            },
            verificationTimeout: TimeSpan.FromSeconds(2),
            cancellationToken: cancellationToken);
    }

    public IDisposable? SubscribeMicrophoneStateChanged(Action onStateChangeDetected)
    {
        ThrowIfDisposed();

        try
        {
            var processInfo = DetectTeamsProcess();
            if (!processInfo.IsRunning) return null;

            var (surface, meetingWindow) = FindMeetingSurface(processInfo.AllProcessIds);
            if (!surface.IsDetected || meetingWindow == null) return null;

            var controls = FindControlElements(meetingWindow);
            var micElement = controls[TargetControl.Microphone];
            if (micElement == null) return null;

            var propHandler = micElement.RegisterPropertyChangedEvent(
                TreeScope.Element,
                (automationElement, propertyId, newValue) =>
                {
                    try
                    {
                        onStateChangeDetected();
                    }
                    catch
                    {
                        // Safe swallow on background MTA callback
                    }
                },
                _automation.PropertyLibrary.Element.Name,
                _automation.PropertyLibrary.Toggle.ToggleState);

            return propHandler;
        }
        catch
        {
            return null;
        }
    }

    public IDisposable? SubscribeCameraStateChanged(Action onStateChangeDetected)
    {
        ThrowIfDisposed();

        try
        {
            var processInfo = DetectTeamsProcess();
            if (!processInfo.IsRunning) return null;

            var (surface, meetingWindow) = FindMeetingSurface(processInfo.AllProcessIds);
            if (!surface.IsDetected || meetingWindow == null) return null;

            var controls = FindControlElements(meetingWindow);
            var cameraElement = controls[TargetControl.Camera];
            if (cameraElement == null) return null;

            var propHandler = cameraElement.RegisterPropertyChangedEvent(
                TreeScope.Element,
                (automationElement, propertyId, newValue) =>
                {
                    try
                    {
                        onStateChangeDetected();
                    }
                    catch
                    {
                        // Safe swallow on background MTA callback
                    }
                },
                _automation.PropertyLibrary.Element.Name,
                _automation.PropertyLibrary.Toggle.ToggleState);

            return propHandler;
        }
        catch
        {
            return null;
        }
    }

    public IDisposable? SubscribeHandStateChanged(Action onStateChangeDetected)
    {
        ThrowIfDisposed();

        try
        {
            var processInfo = DetectTeamsProcess();
            if (!processInfo.IsRunning) return null;

            var (surface, meetingWindow) = FindMeetingSurface(processInfo.AllProcessIds);
            if (!surface.IsDetected || meetingWindow == null) return null;

            var controls = FindControlElements(meetingWindow);
            var handElement = controls[TargetControl.Hand];
            if (handElement == null) return null;

            var propHandler = handElement.RegisterPropertyChangedEvent(
                TreeScope.Element,
                (automationElement, propertyId, newValue) =>
                {
                    try
                    {
                        onStateChangeDetected();
                    }
                    catch
                    {
                        // Safe swallow on background MTA callback
                    }
                },
                _automation.PropertyLibrary.Element.Name,
                _automation.PropertyLibrary.Toggle.ToggleState);

            return propHandler;
        }
        catch
        {
            return null;
        }
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

                    var elements = FindControlElements(window);
                    bool hasMeetingControls = elements.Values.Any(v => v != null);

                    if (hasMeetingControls)
                    {
                        bestMeetingWindow = window;
                        bestWindowTitle = title;
                        bestClassName = className;
                        break;
                    }

                    if (title.Contains("| Microsoft Teams", StringComparison.OrdinalIgnoreCase))
                    {
                        bestMeetingWindow = window;
                        bestWindowTitle = title;
                        bestClassName = className;
                    }
                }
                catch
                {
                    // Stale window
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
            var condition = cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button)
                .Or(cf.ByControlType(FlaUI.Core.Definitions.ControlType.SplitButton))
                .Or(cf.ByControlType(FlaUI.Core.Definitions.ControlType.CheckBox))
                .Or(cf.ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem));

            var candidates = rootWindow.FindAllDescendants(condition);

            foreach (var el in candidates)
            {
                try
                {
                    string name = el.Properties.Name.ValueOrDefault ?? string.Empty;
                    string autoId = el.Properties.AutomationId.ValueOrDefault ?? string.Empty;

                    if (result[TargetControl.Microphone] == null && IsMicrophoneControl(name, autoId))
                    {
                        result[TargetControl.Microphone] = el;
                    }

                    if (result[TargetControl.Camera] == null && IsCameraControl(name, autoId))
                    {
                        result[TargetControl.Camera] = el;
                    }

                    bool isExplicitHand = IsExplicitHandControl(name, autoId);
                    bool isGenericNavHand = IsGenericReactionsControl(name, autoId);

                    if (result[TargetControl.Hand] == null && (isExplicitHand || isGenericNavHand))
                    {
                        result[TargetControl.Hand] = el;
                    }
                    else if (result[TargetControl.Hand] is { } prevHand && isExplicitHand)
                    {
                        // Prefer explicit state-bearing control over generic reactions button
                        string prevName = prevHand.Properties.Name.ValueOrDefault ?? string.Empty;
                        string prevId = prevHand.Properties.AutomationId.ValueOrDefault ?? string.Empty;
                        if (!IsExplicitHandControl(prevName, prevId))
                        {
                            result[TargetControl.Hand] = el;
                        }
                    }

                    if (result[TargetControl.Microphone] != null &&
                        result[TargetControl.Camera] != null &&
                        result[TargetControl.Hand] is { } finalHand &&
                        IsExplicitHandControl(
                            finalHand.Properties.Name.ValueOrDefault ?? string.Empty,
                            finalHand.Properties.AutomationId.ValueOrDefault ?? string.Empty))
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
                reports.Add(CreateMissingControl(target, "Control element was not found in meeting window."));
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

                var (readableState, stateSource) = ControlCapabilityClassifier.ParseState(
                    target,
                    name,
                    toggleStateStr,
                    helpText);

                var (tier, reason) = ControlCapabilityClassifier.Classify(
                    targetControl: target,
                    isFound: true,
                    isEnabled: isEnabled,
                    readableState: readableState,
                    supportedPatterns: supportedPatterns);

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
               n.Contains("stop video") || n.Contains("start video") ||
               (n.Contains("camera") && (n.Contains("turn") || n.Contains("video") || n.Contains("ctrl+shift+o")));
    }

    private static bool IsExplicitHandControl(string name, string autoId)
    {
        string n = name.ToLowerInvariant();
        string id = autoId.ToLowerInvariant();

        if (n.Contains("react and raise hand") || n.Equals("reactions") || id.Contains("reactions-button"))
        {
            return false;
        }

        return id.Contains("raise-hand") || id.Contains("hand-button") ||
               n.Contains("raise hand") || n.Contains("lower hand") ||
               n.Contains("raise your hand") || n.Contains("lower your hand") ||
               n.Contains("hand is raised") || n.Contains("hand is lowered");
    }

    private static bool IsGenericReactionsControl(string name, string autoId)
    {
        string n = name.ToLowerInvariant();
        string id = autoId.ToLowerInvariant();

        return id.Contains("reactions") || n.Contains("react and raise hand") || n.Equals("reactions");
    }

    private static bool IsHandControl(string name, string autoId)
    {
        return IsExplicitHandControl(name, autoId) || IsGenericReactionsControl(name, autoId);
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

    /// <summary>
    /// Passively observes the Teams Live Reactions toolbar and flyout menu elements during a user-assisted observation session.
    /// Strictly read-only: does not invoke the React launcher or any emoji buttons.
    /// </summary>
    public async Task<ReactionDiagnosticReport> ObserveReactionsAsync(
        TimeSpan duration,
        Action<string>? onProgressMessage = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var processInfo = DetectTeamsProcess();
        if (!processInfo.IsRunning || processInfo.AllProcessIds.Count == 0)
        {
            return new ReactionDiagnosticReport
            {
                MeetingDetected = false,
                PolicyStatus = ReactionPolicyStatus.Unknown,
                PolicyReason = "Teams process is not running"
            };
        }

        var (surface, meetingWindow) = FindMeetingSurface(processInfo.AllProcessIds);
        if (!surface.IsDetected || meetingWindow == null)
        {
            return new ReactionDiagnosticReport
            {
                MeetingDetected = false,
                PolicyStatus = ReactionPolicyStatus.Unknown,
                PolicyReason = "No active Teams meeting surface detected"
            };
        }

        // 1. Discover React Toolbar Launcher (Read-Only)
        var launcher = FindReactionLauncher(meetingWindow);
        bool launcherFound = launcher != null;
        string? launcherName = launcher?.Properties.Name.ValueOrDefault;
        string? launcherAutoId = launcher?.Properties.AutomationId.ValueOrDefault;
        string? launcherControlType = launcher?.Properties.ControlType.ValueOrDefault.ToString();
        bool launcherSupportsExpandCollapse = launcher != null && launcher.Patterns.ExpandCollapse.IsSupported;
        bool launcherIsEnabled = launcher?.Properties.IsEnabled.ValueOrDefault ?? false;
        string? launcherHelpText = launcher?.Properties.HelpText.ValueOrDefault;

        onProgressMessage?.Invoke(launcherFound
            ? $"React launcher detected: '{launcherName}' ({launcherAutoId}). Waiting for user to manually open the menu..."
            : "React launcher not found on meeting toolbar. Scanning for reactions menu...");

        // Check if launcher HelpText indicates reactions are disabled by policy
        string policyStatus = ReactionPolicyStatus.Unknown;
        string? policyReason = null;
        if (launcherHelpText != null && launcherHelpText.Contains("turned off", StringComparison.OrdinalIgnoreCase))
        {
            policyStatus = ReactionPolicyStatus.PolicyDisabled;
            policyReason = launcherHelpText;
        }

        var discoveredReactions = new Dictionary<string, ReactionControlElementInfo>(StringComparer.OrdinalIgnoreCase);
        var observedEvents = new List<string>();
        bool flyoutObserved = false;
        string? flyoutHostingType = null;

        var deadline = DateTimeOffset.UtcNow + duration;
        var cf = _automation.ConditionFactory;

        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            // Search A: In-Tree Children of meetingWindow
            var inTreeReactionElements = ScanForReactionButtons(meetingWindow);
            if (inTreeReactionElements.Count > 0)
            {
                flyoutObserved = true;
                flyoutHostingType = "InTreeChild";
                foreach (var (rType, elInfo) in inTreeReactionElements)
                {
                    discoveredReactions[rType] = elInfo;
                }
            }

            // Search B: Separate Top-Level Popup Windows (e.g. Chrome_WidgetWin_1 panes owned by Teams)
            if (discoveredReactions.Count < 5 && processInfo.AllProcessIds.Count > 0)
            {
                try
                {
                    var desktop = _automation.GetDesktop();
                    var popups = desktop.FindAllChildren(cf.ByProcessId(processInfo.AllProcessIds[0]));
                    foreach (var popup in popups)
                    {
                        if (popup.Equals(meetingWindow)) continue;
                        var popupReactionElements = ScanForReactionButtons(popup);
                        if (popupReactionElements.Count > 0)
                        {
                            flyoutObserved = true;
                            flyoutHostingType = "TopLevelPopup";
                            foreach (var (rType, elInfo) in popupReactionElements)
                            {
                                discoveredReactions[rType] = elInfo;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore stale popup window exceptions
                }
            }

            if (discoveredReactions.Count >= 5)
            {
                onProgressMessage?.Invoke($"All 5 reactions discovered! (Like, Love, Applause, Laugh, Surprise).");
                break;
            }

            try
            {
                await Task.Delay(250, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (policyStatus == ReactionPolicyStatus.Unknown)
        {
            if (discoveredReactions.Count > 0)
            {
                policyStatus = ReactionPolicyStatus.Enabled;
            }
            else if (launcherFound && !launcherIsEnabled)
            {
                policyStatus = ReactionPolicyStatus.PolicyDisabled;
                policyReason = "Reactions button is disabled";
            }
        }

        return new ReactionDiagnosticReport
        {
            MeetingDetected = true,
            ReactLauncherFound = launcherFound,
            LauncherName = launcherName,
            LauncherAutomationId = launcherAutoId,
            LauncherControlType = launcherControlType,
            LauncherSupportsExpandCollapse = launcherSupportsExpandCollapse,
            LauncherIsEnabled = launcherIsEnabled,
            FlyoutObserved = flyoutObserved,
            FlyoutHostingType = flyoutHostingType,
            PolicyStatus = policyStatus,
            PolicyReason = policyReason,
            DiscoveredReactions = discoveredReactions.Values.ToList(),
            ObservedEvents = observedEvents,
            ObservedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Executes a remote Live Reaction invocation (like, love, applause, laugh, surprise).
    /// Opens the reactions flyout if needed, locates the target button, and triggers it via InvokePattern.
    /// </summary>
    public async Task<ReactionCommandResult> SendReactionAsync(
        string reaction,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var sw = Stopwatch.StartNew();

        if (!ReactionTypes.IsValid(reaction))
        {
            return new ReactionCommandResult
            {
                Status = CommandExecutionStatus.Rejected,
                Reaction = reaction,
                ErrorCode = ProtocolErrorCodes.InvalidDesiredState,
                Reason = $"Invalid reaction '{reaction}'. Expected one of: like, love, applause, laugh, surprise.",
                Duration = sw.Elapsed
            };
        }

        await _actionLock.WaitAsync(cancellationToken);
        try
        {
            var processInfo = DetectTeamsProcess();
            if (!processInfo.IsRunning || processInfo.AllProcessIds.Count == 0)
            {
                return new ReactionCommandResult
                {
                    Status = CommandExecutionStatus.Failed,
                    Reaction = reaction,
                    ErrorCode = ProtocolErrorCodes.NoActiveMeeting,
                    Reason = "Teams process is not running.",
                    Duration = sw.Elapsed
                };
            }

            var (surface, meetingWindow) = FindMeetingSurface(processInfo.AllProcessIds);
            if (!surface.IsDetected || meetingWindow == null)
            {
                return new ReactionCommandResult
                {
                    Status = CommandExecutionStatus.Failed,
                    Reaction = reaction,
                    ErrorCode = ProtocolErrorCodes.NoActiveMeeting,
                    Reason = "No active Teams meeting surface detected.",
                    Duration = sw.Elapsed
                };
            }

            // 1. Check if flyout is already open and contains target button
            AutomationElement? targetButton = FindReactionButtonElement(meetingWindow, reaction);

            if (targetButton == null && processInfo.AllProcessIds.Count > 0)
            {
                var desktop = _automation.GetDesktop();
                var cf = _automation.ConditionFactory;
                var popups = desktop.FindAllChildren(cf.ByProcessId(processInfo.AllProcessIds[0]));
                foreach (var popup in popups)
                {
                    if (popup.Equals(meetingWindow)) continue;
                    targetButton = FindReactionButtonElement(popup, reaction);
                    if (targetButton != null) break;
                }
            }

            // 2. If not already open, locate reaction launcher and open menu
            if (targetButton == null)
            {
                var launcher = FindReactionLauncher(meetingWindow);
                if (launcher == null)
                {
                    return new ReactionCommandResult
                    {
                        Status = CommandExecutionStatus.Failed,
                        Reaction = reaction,
                        ErrorCode = ProtocolErrorCodes.ControlNotFound,
                        Reason = "Reactions toolbar launcher not found.",
                        Duration = sw.Elapsed
                    };
                }

                if (!launcher.Properties.IsEnabled.ValueOrDefault)
                {
                    return new ReactionCommandResult
                    {
                        Status = CommandExecutionStatus.Rejected,
                        Reaction = reaction,
                        ErrorCode = ProtocolErrorCodes.PolicyRestricted,
                        Reason = "Reactions are disabled by meeting policy.",
                        Duration = sw.Elapsed
                    };
                }

                // Open the flyout using ExpandCollapsePattern or InvokePattern
                bool opened = false;
                if (launcher.Patterns.ExpandCollapse.IsSupported)
                {
                    launcher.Patterns.ExpandCollapse.Pattern.Expand();
                    opened = true;
                }
                else if (launcher.Patterns.Invoke.IsSupported)
                {
                    launcher.Patterns.Invoke.Pattern.Invoke();
                    opened = true;
                }

                if (!opened)
                {
                    return new ReactionCommandResult
                    {
                        Status = CommandExecutionStatus.Failed,
                        Reaction = reaction,
                        ErrorCode = ProtocolErrorCodes.InvokeFailed,
                        Reason = "Reactions launcher does not support ExpandCollapse or Invoke patterns.",
                        Duration = sw.Elapsed
                    };
                }

                // Wait briefly for flyout to mount
                await Task.Delay(200, cancellationToken);

                // Scan again for target button
                targetButton = FindReactionButtonElement(meetingWindow, reaction);
                if (targetButton == null && processInfo.AllProcessIds.Count > 0)
                {
                    var desktop = _automation.GetDesktop();
                    var cf = _automation.ConditionFactory;
                    var popups = desktop.FindAllChildren(cf.ByProcessId(processInfo.AllProcessIds[0]));
                    foreach (var popup in popups)
                    {
                        if (popup.Equals(meetingWindow)) continue;
                        targetButton = FindReactionButtonElement(popup, reaction);
                        if (targetButton != null) break;
                    }
                }
            }

            if (targetButton == null)
            {
                return new ReactionCommandResult
                {
                    Status = CommandExecutionStatus.Failed,
                    Reaction = reaction,
                    ErrorCode = ProtocolErrorCodes.ControlNotFound,
                    Reason = $"Reaction button '{reaction}' could not be located in reactions menu.",
                    Duration = sw.Elapsed
                };
            }

            if (!targetButton.Properties.IsEnabled.ValueOrDefault)
            {
                return new ReactionCommandResult
                {
                    Status = CommandExecutionStatus.Rejected,
                    Reaction = reaction,
                    ErrorCode = ProtocolErrorCodes.PolicyRestricted,
                    Reason = $"Reaction button '{reaction}' is disabled by meeting policy.",
                    Duration = sw.Elapsed
                };
            }

            if (!targetButton.Patterns.Invoke.IsSupported)
            {
                return new ReactionCommandResult
                {
                    Status = CommandExecutionStatus.Failed,
                    Reaction = reaction,
                    ErrorCode = ProtocolErrorCodes.InvokeFailed,
                    Reason = $"Reaction button '{reaction}' does not support InvokePattern.",
                    Duration = sw.Elapsed
                };
            }

            // 3. Execute UIA Invoke
            targetButton.Patterns.Invoke.Pattern.Invoke();

            return new ReactionCommandResult
            {
                Status = CommandExecutionStatus.Confirmed,
                Reaction = reaction,
                Reason = $"Reaction '{reaction}' sent successfully.",
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            return new ReactionCommandResult
            {
                Status = CommandExecutionStatus.Failed,
                Reaction = reaction,
                ErrorCode = ProtocolErrorCodes.InvokeFailed,
                Reason = $"Failed to invoke reaction '{reaction}': {RedactionHelper.RedactSecrets(ex.Message)}",
                Duration = sw.Elapsed
            };
        }
        finally
        {
            _actionLock.Release();
        }
    }

    private static AutomationElement? FindReactionButtonElement(AutomationElement container, string targetReaction)
    {
        try
        {
            var cf = container.Automation.ConditionFactory;
            var candidates = container.FindAllDescendants(
                cf.ByControlType(ControlType.Button).Or(cf.ByControlType(ControlType.MenuItem)));

            foreach (var item in candidates)
            {
                try
                {
                    string name = item.Properties.Name.ValueOrDefault ?? string.Empty;
                    string autoId = item.Properties.AutomationId.ValueOrDefault ?? string.Empty;
                    string? rType = ClassifyReactionType(name, autoId);

                    if (string.Equals(rType, targetReaction, StringComparison.OrdinalIgnoreCase))
                    {
                        return item;
                    }
                }
                catch
                {
                    // Stale element
                }
            }
        }
        catch
        {
            // Crawl error
        }

        return null;
    }

    private AutomationElement? FindReactionLauncher(AutomationElement rootWindow)
    {
        try
        {
            var cf = _automation.ConditionFactory;
            var buttons = rootWindow.FindAllDescendants(cf.ByControlType(ControlType.Button)
                .Or(cf.ByControlType(ControlType.SplitButton))
                .Or(cf.ByControlType(ControlType.MenuItem)));

            foreach (var btn in buttons)
            {
                try
                {
                    string name = btn.Properties.Name.ValueOrDefault ?? string.Empty;
                    string autoId = btn.Properties.AutomationId.ValueOrDefault ?? string.Empty;

                    if (IsReactionLauncher(name, autoId))
                    {
                        return btn;
                    }
                }
                catch
                {
                    // Ignore stale element
                }
            }
        }
        catch
        {
            // Ignore crawl errors
        }

        return null;
    }

    public static bool IsReactionLauncher(string name, string autoId)
    {
        string n = name.ToLowerInvariant();
        string id = autoId.ToLowerInvariant();

        return id.Contains("reactions-button") ||
               id.Contains("calling-reaction-bar") ||
               id.Contains("reaction-button") ||
               n.Contains("react and raise hand") ||
               n.Equals("reactions") ||
               n.Equals("react") ||
               n.Contains("show reactions");
    }

    private static Dictionary<string, ReactionControlElementInfo> ScanForReactionButtons(AutomationElement container)
    {
        var result = new Dictionary<string, ReactionControlElementInfo>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var cf = container.Automation.ConditionFactory;
            var candidates = container.FindAllDescendants(
                cf.ByControlType(ControlType.Button).Or(cf.ByControlType(ControlType.MenuItem)));

            foreach (var item in candidates)
            {
                try
                {
                    string name = item.Properties.Name.ValueOrDefault ?? string.Empty;
                    string autoId = item.Properties.AutomationId.ValueOrDefault ?? string.Empty;
                    string? rType = ClassifyReactionType(name, autoId);

                    if (rType != null && !result.ContainsKey(rType))
                    {
                        result[rType] = new ReactionControlElementInfo
                        {
                            ReactionType = rType,
                            AccessibleName = name,
                            AutomationId = autoId,
                            ControlType = item.Properties.ControlType.ValueOrDefault.ToString(),
                            SupportsInvokePattern = item.Patterns.Invoke.IsSupported,
                            IsEnabled = item.Properties.IsEnabled.ValueOrDefault,
                            BoundingRectangle = item.Properties.BoundingRectangle.ValueOrDefault.ToString(),
                            HelpText = item.Properties.HelpText.ValueOrDefault
                        };
                    }
                }
                catch
                {
                    // Ignore stale element
                }
            }
        }
        catch
        {
            // Ignore crawl errors
        }

        return result;
    }

    public static string? ClassifyReactionType(string name, string autoId)
    {
        string n = name.ToLowerInvariant();
        string id = autoId.ToLowerInvariant();

        // 1. Like
        if (id.Contains("like") || n.StartsWith("like") || n.Contains("like (") || n.Contains("thumbs up"))
        {
            return ReactionTypes.Like;
        }

        // 2. Love / Heart
        if (id.Contains("love") || id.Contains("heart") || n.StartsWith("love") || n.Contains("love (") || n.Contains("heart"))
        {
            return ReactionTypes.Love;
        }

        // 3. Applause / Clap
        if (id.Contains("applause") || id.Contains("clap") || n.StartsWith("applause") || n.Contains("applause (") || n.Contains("clap"))
        {
            return ReactionTypes.Applause;
        }

        // 4. Laugh / Laughing
        if (id.Contains("laugh") || n.StartsWith("laugh") || n.Contains("laugh (") || n.Contains("laughing"))
        {
            return ReactionTypes.Laugh;
        }

        // 5. Surprise / Wow
        if (id.Contains("surprise") || id.Contains("wow") || n.StartsWith("surprise") || n.Contains("surprise (") || n.Contains("wow"))
        {
            return ReactionTypes.Surprise;
        }

        return null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _actionLock.Dispose();
            _automation.Dispose();
            _disposed = true;
        }
    }
}
