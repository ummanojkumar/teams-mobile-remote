using System.Drawing;
using System.Windows.Forms;
using TeamsRemote.Core.Abstractions;
using TeamsRemote.Core.Adapters;
using TeamsRemote.Core.Models;
using TeamsRemote.Core.Redaction;
using TeamsRemote.Core.State;

namespace TeamsRemote.Companion.UI;

public sealed class CompanionMainForm : Form
{
    private readonly ITeamsController _controller;
    private readonly MeetingStateStore _stateStore;
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _trayMenu;

    private Label _lblTeamsStatus = null!;
    private Label _lblMeetingStatus = null!;
    private Panel _pnlMicCard = null!;
    private Label _lblMicState = null!;
    private Label _lblConfidence = null!;
    private Button _btnScan = null!;
    private Button _btnMute = null!;
    private Button _btnUnmute = null!;
    private TextBox _txtLog = null!;

    private bool _isExiting;
    private bool _isActionInProgress;

    public CompanionMainForm()
    {
        _controller = new UiaTeamsController();
        _stateStore = new MeetingStateStore();

        // Tray menu and icon
        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add("Show Companion", null, (s, e) => ShowAndRestore());
        _trayMenu.Items.Add("Scan Teams", null, async (s, e) => await ScanTeamsAsync());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("Exit", null, (s, e) => ExitApplication());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Teams Mobile Remote Companion",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (s, e) => ShowAndRestore();

        InitializeFormComponents();

        _stateStore.StateChanged += () =>
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(UpdateUiFromState));
            }
            else
            {
                UpdateUiFromState();
            }
        };

        // Initial scan on load
        Shown += async (s, e) => await ScanTeamsAsync();
    }

    private void InitializeFormComponents()
    {
        Text = "Teams Mobile Remote — Companion (Phase 1)";
        Size = new Size(620, 560);
        MinimumSize = new Size(580, 500);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        BackColor = Color.FromArgb(248, 249, 250);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            RowCount = 4,
            ColumnCount = 1
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 85f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // Group 1: Teams and Meeting Status
        var grpStatus = new GroupBox
        {
            Text = "Teams Environment",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(33, 37, 41)
        };
        _lblTeamsStatus = new Label
        {
            Text = "Teams Process: Checking...",
            Location = new Point(16, 26),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        _lblMeetingStatus = new Label
        {
            Text = "Meeting Surface: Checking...",
            Location = new Point(16, 52),
            AutoSize = true,
            ForeColor = Color.FromArgb(108, 117, 125)
        };
        grpStatus.Controls.Add(_lblTeamsStatus);
        grpStatus.Controls.Add(_lblMeetingStatus);
        mainLayout.Controls.Add(grpStatus, 0, 0);

        // Group 2: Microphone State Card
        var grpMic = new GroupBox
        {
            Text = "Microphone Control Surface",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(33, 37, 41)
        };
        _pnlMicCard = new Panel
        {
            Location = new Point(16, 24),
            Size = new Size(grpMic.Width - 32, 95),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.FromArgb(233, 236, 239),
            BorderStyle = BorderStyle.FixedSingle
        };
        _lblMicState = new Label
        {
            Text = "UNKNOWN",
            Dock = DockStyle.Top,
            Height = 50,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            ForeColor = Color.FromArgb(73, 80, 87)
        };
        _lblConfidence = new Label
        {
            Text = "Confidence: UNKNOWN · Revision: 0",
            Dock = DockStyle.Bottom,
            Height = 35,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(108, 117, 125)
        };
        _pnlMicCard.Controls.Add(_lblMicState);
        _pnlMicCard.Controls.Add(_lblConfidence);
        grpMic.Controls.Add(_pnlMicCard);
        mainLayout.Controls.Add(grpMic, 0, 1);

        // Panel 3: Action Buttons
        var pnlButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        _btnScan = new Button
        {
            Text = "🔍 Scan Teams",
            Height = 38,
            Width = 140,
            BackColor = Color.FromArgb(13, 110, 253),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnScan.Click += async (s, e) => await ScanTeamsAsync();

        _btnMute = new Button
        {
            Text = "🔇 Set Muted",
            Height = 38,
            Width = 140,
            BackColor = Color.FromArgb(220, 53, 69),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Enabled = false
        };
        _btnMute.Click += async (s, e) => await SetMicrophoneAsync(DesiredMicrophoneState.Muted);

        _btnUnmute = new Button
        {
            Text = "🎙️ Set Unmuted",
            Height = 38,
            Width = 140,
            BackColor = Color.FromArgb(25, 135, 84),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Enabled = false
        };
        _btnUnmute.Click += async (s, e) => await SetMicrophoneAsync(DesiredMicrophoneState.Unmuted);

        pnlButtons.Controls.Add(_btnScan);
        pnlButtons.Controls.Add(_btnMute);
        pnlButtons.Controls.Add(_btnUnmute);
        mainLayout.Controls.Add(pnlButtons, 0, 2);

        // Group 4: Activity Log
        var grpLog = new GroupBox
        {
            Text = "Activity Log (Strictly Redacted)",
            Dock = DockStyle.Fill
        };
        _txtLog = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(33, 37, 41),
            ForeColor = Color.FromArgb(248, 249, 250),
            Font = new Font("Consolas", 9f)
        };
        grpLog.Controls.Add(_txtLog);
        mainLayout.Controls.Add(grpLog, 0, 3);

        Controls.Add(mainLayout);
    }

    private async Task ScanTeamsAsync()
    {
        if (_isActionInProgress) return;

        LogMessage("Scanning Teams desktop environment...");
        try
        {
            var report = await _controller.InspectAsync();

            if (!report.Process.IsRunning)
            {
                _lblTeamsStatus.Text = "Teams Process: NOT RUNNING";
                _lblTeamsStatus.ForeColor = Color.FromArgb(220, 53, 69);
                _lblMeetingStatus.Text = "Meeting Surface: Unavailable";
                _stateStore.Reset();
                LogMessage("Scan complete: Microsoft Teams is not running.");
                return;
            }

            _lblTeamsStatus.Text = $"Teams Process: RUNNING ({report.Process.ProcessName}, PID: {report.Process.ProcessId})";
            _lblTeamsStatus.ForeColor = Color.FromArgb(25, 135, 84);

            if (!report.MeetingSurface.IsDetected)
            {
                _lblMeetingStatus.Text = "Meeting Surface: NOT DETECTED (No active meeting)";
                _lblMeetingStatus.ForeColor = Color.FromArgb(108, 117, 125);
                _stateStore.UpdateObserved(TeamsControlState.Unknown, isConfirmed: false, meetingStatus: "none");
                LogMessage("Scan complete: Teams running, but no active meeting surface found.");
                return;
            }

            _lblMeetingStatus.Text = $"Meeting Surface: DETECTED ({report.MeetingSurface.RedactedWindowTitle})";
            _lblMeetingStatus.ForeColor = Color.FromArgb(13, 110, 253);

            var mic = report.Controls.FirstOrDefault(c => c.TargetControl == TargetControl.Microphone);
            if (mic != null && mic.Found && mic.ReadableState != TeamsControlState.Unknown)
            {
                _stateStore.UpdateObserved(mic.ReadableState, isConfirmed: true, meetingStatus: "active");
                LogMessage($"Scan complete: Microphone is {mic.ReadableState.ToUpperInvariant()} (Tier: {mic.CapabilityTier.ToWireString()}).");
            }
            else
            {
                _stateStore.UpdateObserved(TeamsControlState.Unknown, isConfirmed: false, meetingStatus: "active");
                LogMessage("Scan complete: Meeting detected, but microphone control could not be verified.");
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Scan failed: {RedactionHelper.RedactSecrets(ex.Message)}");
        }
    }

    private async Task SetMicrophoneAsync(DesiredMicrophoneState desired)
    {
        if (_isActionInProgress) return;

        _isActionInProgress = true;
        UpdateButtonStates();

        string desiredWire = desired.ToWireString();
        LogMessage($"Executing desired-state action: set_microphone({desiredWire})...");
        _stateStore.MarkPending();

        try
        {
            var result = await _controller.SetMicrophoneStateAsync(desired);

            switch (result.Status)
            {
                case CommandExecutionStatus.Confirmed:
                    _stateStore.UpdateObserved(result.FinalState, isConfirmed: true);
                    LogMessage($"Result: CONFIRMED in {result.Duration.TotalMilliseconds:F0}ms. Mic is {result.FinalState.ToUpperInvariant()}.");
                    break;

                case CommandExecutionStatus.AlreadyInDesiredState:
                    _stateStore.UpdateObserved(result.FinalState, isConfirmed: true);
                    LogMessage($"Result: ALREADY_IN_STATE. Mic is already {result.FinalState.ToUpperInvariant()}. No action needed.");
                    break;

                case CommandExecutionStatus.TimedOut:
                    _stateStore.MarkStaleOrUncertain();
                    LogMessage($"Result: TIMED_OUT after {result.Duration.TotalMilliseconds:F0}ms. Error: {result.ErrorCode}. Reason: {result.Reason}");
                    break;

                default:
                    _stateStore.MarkStaleOrUncertain();
                    LogMessage($"Result: {result.Status}. Error: {result.ErrorCode}. Reason: {result.Reason}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _stateStore.MarkStaleOrUncertain();
            LogMessage($"Execution error: {RedactionHelper.RedactSecrets(ex.Message)}");
        }
        finally
        {
            _isActionInProgress = false;
            UpdateButtonStates();
        }
    }

    private void UpdateUiFromState()
    {
        var (rev, state, confidence, meeting) = _stateStore.GetSnapshot();

        string stateUpper = state.ToUpperInvariant();
        _lblMicState.Text = stateUpper;

        switch (state)
        {
            case TeamsControlState.Unmuted:
                _pnlMicCard.BackColor = Color.FromArgb(209, 231, 221); // light green
                _lblMicState.ForeColor = Color.FromArgb(15, 81, 50);
                break;
            case TeamsControlState.Muted:
                _pnlMicCard.BackColor = Color.FromArgb(248, 215, 218); // light red
                _lblMicState.ForeColor = Color.FromArgb(132, 32, 41);
                break;
            default:
                _pnlMicCard.BackColor = Color.FromArgb(233, 236, 239); // gray
                _lblMicState.ForeColor = Color.FromArgb(73, 80, 87);
                break;
        }

        string confUpper = confidence.ToWireString().ToUpperInvariant();
        _lblConfidence.Text = $"Confidence: {confUpper} · Revision: {rev} · Meeting: {meeting.ToUpperInvariant()}";

        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        var (_, state, confidence, meeting) = _stateStore.GetSnapshot();
        bool meetingActive = meeting == "active";
        bool notBusy = !_isActionInProgress;

        _btnScan.Enabled = notBusy;

        // "Set Muted" is enabled only if state is not already muted, meeting is active, not busy, and confidence is not pending
        _btnMute.Enabled = notBusy && meetingActive && confidence != StateConfidence.Pending && state != TeamsControlState.Muted;

        // "Set Unmuted" is enabled only if state is not already unmuted, meeting is active, not busy, and confidence is not pending
        _btnUnmute.Enabled = notBusy && meetingActive && confidence != StateConfidence.Pending && state != TeamsControlState.Unmuted;
    }

    private void LogMessage(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        string sanitized = RedactionHelper.RedactSecrets(RedactionHelper.RedactFilePath(message));

        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLogLine($"[{timestamp}] {sanitized}"));
        }
        else
        {
            AppendLogLine($"[{timestamp}] {sanitized}");
        }
    }

    private void AppendLogLine(string line)
    {
        _txtLog.AppendText(line + Environment.NewLine);
        _txtLog.SelectionStart = _txtLog.TextLength;
        _txtLog.ScrollToCaret();
    }

    private void ShowAndRestore()
    {
        Show();
        WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _controller.Dispose();
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_isExiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _trayIcon.ShowBalloonTip(
                2000,
                "Teams Mobile Remote",
                "Companion minimized to tray. Double-click tray icon to open.",
                ToolTipIcon.Info);
        }
        else
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _controller.Dispose();
            base.OnFormClosing(e);
        }
    }
}
