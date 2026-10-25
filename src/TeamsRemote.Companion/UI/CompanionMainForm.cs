using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using QRCoder;
using TeamsRemote.Core.Abstractions;
using TeamsRemote.Core.Adapters;
using TeamsRemote.Core.Models;
using TeamsRemote.Core.Pairing;
using TeamsRemote.Core.Protocol;
using TeamsRemote.Core.Redaction;
using TeamsRemote.Core.Security;
using TeamsRemote.Core.State;
using TeamsRemote.Core.Transport;

namespace TeamsRemote.Companion.UI;

public sealed class CompanionMainForm : Form
{
    private readonly ITeamsController _controller;
    private readonly MeetingStateStore _stateStore;
    private readonly IDeviceCredentialStore _credentialStore;
    private readonly PairingService _pairingService;
    private readonly CommandProcessor _commandProcessor;
    private readonly ConnectionClient _connectionClient;
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _trayMenu;

    private readonly Uri _relayUri = new("ws://127.0.0.1:5100/ws/companion");

    private Label _lblTeamsStatus = null!;
    private Label _lblMeetingStatus = null!;
    private Panel _pnlMicCard = null!;
    private Label _lblMicState = null!;
    private Label _lblConfidence = null!;
    private Button _btnScan = null!;
    private Button _btnMute = null!;
    private Button _btnUnmute = null!;

    private Label _lblRelayStatus = null!;
    private Label _lblPairedDevice = null!;
    private Button _btnPairPhone = null!;
    private Button _btnRevokePhone = null!;

    private TextBox _txtLog = null!;

    private bool _isExiting;
    private bool _isActionInProgress;

    public CompanionMainForm()
    {
        _controller = new UiaTeamsController();
        _stateStore = new MeetingStateStore();
        _credentialStore = new DpapiDeviceStore();

        _pairingService = new PairingService(_credentialStore, ConfirmPairingDialogAsync);
        _commandProcessor = new CommandProcessor(_stateStore, () => _pairingService.ActiveDeviceId);
        _connectionClient = new ConnectionClient(_relayUri, _commandProcessor, _stateStore, _pairingService);

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

        // Wire event handlers
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

            _ = Task.Run(async () =>
            {
                try
                {
                    await _connectionClient.SendStateSnapshotAsync();
                }
                catch
                {
                    // Ignore transient transport failures
                }
            });
        };

        _pairingService.DevicePairingChanged += () =>
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(UpdatePairingUi));
            }
            else
            {
                UpdatePairingUi();
            }
        };

        _connectionClient.StatusChanged += status =>
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => UpdateConnectionStatusUi(status));
            }
            else
            {
                UpdateConnectionStatusUi(status);
            }
        };

        _connectionClient.LogMessage += msg => LogMessage(msg);

        // Initial scan and load on form show
        Shown += async (s, e) =>
        {
            await _pairingService.InitializeAsync();
            UpdatePairingUi();
            await ScanTeamsAsync();

            // Start outbound transport in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await _connectionClient.StartAsync();
                }
                catch (Exception ex)
                {
                    LogMessage($"Transport client ended: {ex.Message}");
                }
            });
        };
    }

    private void InitializeFormComponents()
    {
        Text = "Teams Mobile Remote — Companion (Phase 2)";
        Size = new Size(650, 680);
        MinimumSize = new Size(600, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        BackColor = Color.FromArgb(248, 249, 250);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            RowCount = 5,
            ColumnCount = 1
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80f));  // Status
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130f)); // Mic card
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f));  // Mic buttons
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110f)); // Pairing & Transport
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Log

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
            Location = new Point(16, 24),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        _lblMeetingStatus = new Label
        {
            Text = "Meeting Surface: Checking...",
            Location = new Point(16, 48),
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
            Location = new Point(16, 20),
            Size = new Size(grpMic.Width - 32, 90),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.FromArgb(233, 236, 239),
            BorderStyle = BorderStyle.FixedSingle
        };
        _lblMicState = new Label
        {
            Text = "UNKNOWN",
            Dock = DockStyle.Top,
            Height = 48,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 20f, FontStyle.Bold),
            ForeColor = Color.FromArgb(73, 80, 87)
        };
        _lblConfidence = new Label
        {
            Text = "Confidence: UNKNOWN · Revision: 0",
            Dock = DockStyle.Bottom,
            Height = 32,
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
            Height = 36,
            Width = 130,
            BackColor = Color.FromArgb(13, 110, 253),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnScan.Click += async (s, e) => await ScanTeamsAsync();

        _btnMute = new Button
        {
            Text = "🔇 Set Muted",
            Height = 36,
            Width = 130,
            BackColor = Color.FromArgb(220, 53, 69),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Enabled = false
        };
        _btnMute.Click += async (s, e) => await SetMicrophoneAsync(DesiredMicrophoneState.Muted);

        _btnUnmute = new Button
        {
            Text = "🎙️ Set Unmuted",
            Height = 36,
            Width = 130,
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

        // Group 4: Transport & Pairing
        var grpPairing = new GroupBox
        {
            Text = "Transport & Phone Pairing",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(33, 37, 41)
        };
        _lblRelayStatus = new Label
        {
            Text = "Relay Transport: Disconnected",
            Location = new Point(16, 22),
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(108, 117, 125)
        };
        _lblPairedDevice = new Label
        {
            Text = "Paired Device: No phone paired",
            Location = new Point(16, 44),
            AutoSize = true,
            ForeColor = Color.FromArgb(73, 80, 87)
        };
        _btnPairPhone = new Button
        {
            Text = "📱 Pair Phone",
            Location = new Point(16, 68),
            Size = new Size(130, 32),
            BackColor = Color.FromArgb(102, 16, 242),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnPairPhone.Click += async (s, e) => await ShowPairingQrAsync();

        _btnRevokePhone = new Button
        {
            Text = "✕ Revoke Device",
            Location = new Point(154, 68),
            Size = new Size(130, 32),
            BackColor = Color.FromArgb(108, 117, 125),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Enabled = false
        };
        _btnRevokePhone.Click += async (s, e) => await RevokeActiveDeviceAsync();

        grpPairing.Controls.Add(_lblRelayStatus);
        grpPairing.Controls.Add(_lblPairedDevice);
        grpPairing.Controls.Add(_btnPairPhone);
        grpPairing.Controls.Add(_btnRevokePhone);
        mainLayout.Controls.Add(grpPairing, 0, 3);

        // Group 5: Activity Log
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
        mainLayout.Controls.Add(grpLog, 0, 4);

        Controls.Add(mainLayout);
    }

    private async Task ShowPairingQrAsync()
    {
        try
        {
            string clientRelayUrl = _relayUri.ToString().Replace("/ws/companion", "/ws/client");
            var challenge = _pairingService.CreateSingleUseChallenge(clientRelayUrl);

            // Register ephemeral challenge with relay
            var challengeEnvelope = ProtocolEnvelope.Create(
                ProtocolMessageType.PairChallenge,
                challenge,
                Guid.Empty);

            await _connectionClient.SendEnvelopeAsync(challengeEnvelope);

            // Render QR Code via QRCoder
            using var qrGen = new QRCodeGenerator();
            string qrPayload = JsonSerializer.Serialize(new
            {
                challengeId = challenge.ChallengeId,
                relayUrl = challenge.RelayUrl,
                shortCode = challenge.ShortCode,
                expiresAt = challenge.ExpiresAt
            });

            using var qrData = qrGen.CreateQrCode(qrPayload, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new QRCode(qrData);
            using var qrBitmap = qrCode.GetGraphic(5);

            LogMessage($"Generated single-use pairing challenge ({challenge.ShortCode}). Expires in 60s.");
            LogMessage($"[PAIRING] Challenge ID: {challenge.ChallengeId}");

            using var qrDialog = new PairingQrDialog(qrBitmap, challenge.ShortCode, qrPayload);
            qrDialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            LogMessage($"Failed to generate pairing QR: {RedactionHelper.RedactSecrets(ex.Message)}");
        }
    }

    private Task<bool> ConfirmPairingDialogAsync(string deviceLabel, string confirmationCode)
    {
        if (InvokeRequired)
        {
            return (Task<bool>)Invoke(new Func<Task<bool>>(() => ConfirmPairingDialogAsync(deviceLabel, confirmationCode)));
        }

        using var dialog = new PairingApprovalDialog(deviceLabel, confirmationCode);
        var result = dialog.ShowDialog(this);
        bool approved = result == DialogResult.OK && dialog.Approved;

        LogMessage(approved
            ? $"Companion user approved pairing for '{deviceLabel}' ({confirmationCode})."
            : $"Companion user declined pairing for '{deviceLabel}' ({confirmationCode}).");

        return Task.FromResult(approved);
    }

    private async Task RevokeActiveDeviceAsync()
    {
        var device = _pairingService.ActiveDevice;
        if (device == null) return;

        var confirm = MessageBox.Show(
            $"Revoke pairing for '{device.DeviceLabel}'?\nThis device will immediately lose access and be unable to reconnect.",
            "Confirm Revocation",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm == DialogResult.Yes)
        {
            var revokeEnvelope = ProtocolEnvelope.Create(
                ProtocolMessageType.RevokeDevice,
                new RevokePayload { DeviceId = device.DeviceId, Reason = "Revoked by companion user" },
                device.DeviceId);

            await _connectionClient.SendEnvelopeAsync(revokeEnvelope);
            await _pairingService.RevokeDeviceAsync();

            LogMessage($"Device '{device.DeviceLabel}' revoked. Route dropped and DPAPI credentials cleared.");
        }
    }

    private void UpdatePairingUi()
    {
        var device = _pairingService.ActiveDevice;
        if (device != null)
        {
            _lblPairedDevice.Text = $"Paired Device: '{device.DeviceLabel}' (Paired at {device.PairedAt:HH:mm:ss} UTC)";
            _lblPairedDevice.ForeColor = Color.FromArgb(25, 135, 84);
            _btnRevokePhone.Enabled = true;
        }
        else
        {
            _lblPairedDevice.Text = "Paired Device: No phone paired";
            _lblPairedDevice.ForeColor = Color.FromArgb(108, 117, 125);
            _btnRevokePhone.Enabled = false;
        }
    }

    private void UpdateConnectionStatusUi(ConnectionStatus status)
    {
        _lblRelayStatus.Text = $"Relay Transport: {status} ({_relayUri})";
        _lblRelayStatus.ForeColor = status switch
        {
            ConnectionStatus.Connected => Color.FromArgb(25, 135, 84),
            ConnectionStatus.Connecting => Color.FromArgb(13, 110, 253),
            ConnectionStatus.Reconnecting => Color.FromArgb(255, 193, 7),
            _ => Color.FromArgb(220, 53, 69)
        };
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
        _btnMute.Enabled = notBusy && meetingActive && confidence != StateConfidence.Pending && state != TeamsControlState.Muted;
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
        _ = _connectionClient.DisposeAsync();
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
            _ = _connectionClient.DisposeAsync();
            base.OnFormClosing(e);
        }
    }
}
