using System.Reflection;
using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using QRCoder;
using TeamsRemote.Companion.AppHost;
using TeamsRemote.Companion.Network;
using TeamsRemote.Companion.Security;
using TeamsRemote.Companion.Settings;
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
    private readonly EmbeddedRelayHost _relayHost;
    private readonly ITeamsController _controller;
    private readonly MeetingStateStore _stateStore;
    private readonly IDeviceCredentialStore _credentialStore;
    private readonly PairingService _pairingService;
    private readonly CommandProcessor _commandProcessor;
    private readonly ConnectionClient _connectionClient;
    private readonly TeamsPassiveStateObserver _passiveObserver;
    private readonly CompanionSettingsManager _settingsManager;
    private readonly WindowsFirewallService _firewallService;
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _trayMenu;

    private readonly Uri _relayUri = new("ws://127.0.0.1:5100/ws/companion");

    private CompanionSettings _settings = new();
    private IReadOnlyList<NetworkAdapterInfo> _availableAdapters = Array.Empty<NetworkAdapterInfo>();
    private bool _isUpdatingLanSettingsUi;

    private Label _lblTeamsStatus = null!;
    private Label _lblMeetingStatus = null!;
    private Panel _pnlMicCard = null!;
    private Label _lblMicState = null!;
    private Label _lblConfidence = null!;
    private Button _btnScan = null!;
    private Button _btnMute = null!;
    private Button _btnUnmute = null!;
    private Button _btnObserveReactions = null!;

    private Label _lblRelayStatus = null!;
    private Label _lblPairedDevice = null!;
    private Button _btnPairPhone = null!;
    private Button _btnRevokePhone = null!;
    private Button _btnOpenPwa = null!;

    private CheckBox _chkEnableLan = null!;
    private ComboBox _cmbAdapters = null!;
    private Button _btnRefreshAdapters = null!;
    private Label _lblLanStatus = null!;
    private Label _lblPhoneUrl = null!;
    private Button _btnCopyPhoneUrl = null!;
    private Label _lblLanSecurityNotice = null!;

    private TextBox _txtLog = null!;

    private bool _isExiting;
    private bool _isActionInProgress;

    public CompanionMainForm()
    {
        _relayHost = new EmbeddedRelayHost();
        _controller = new UiaTeamsController();
        _stateStore = new MeetingStateStore();
        _credentialStore = new DpapiDeviceStore();
        _settingsManager = new CompanionSettingsManager();
        _firewallService = new WindowsFirewallService();

        _pairingService = new PairingService(_credentialStore, ConfirmPairingDialogAsync);
        _commandProcessor = new CommandProcessor(_stateStore, () => _pairingService.ActiveDeviceId, _controller);
        _connectionClient = new ConnectionClient(_relayUri, _commandProcessor, _stateStore, _pairingService);
        _passiveObserver = new TeamsPassiveStateObserver(
            _controller,
            _stateStore,
            () => _pairingService.ActiveDeviceId != null,
            pollingInterval: TimeSpan.FromSeconds(2),
            debounceDuration: TimeSpan.FromMilliseconds(150));

        // Tray menu and icon
        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add("Show Companion", null, (s, e) => ShowAndRestore());
        _trayMenu.Items.Add("Open Web Remote (PWA)", null, (s, e) => OpenWebRemoteInBrowser());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("Scan Teams", null, async (s, e) => await ScanTeamsAsync());
        _trayMenu.Items.Add("Observe Live Reactions (User-Assisted)", null, async (s, e) => await ObserveLiveReactionsAsync());
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
            _passiveObserver.Start();

            // Start in-process relay and outbound transport
            _ = Task.Run(async () =>
            {
                try
                {
                    _settings = await _settingsManager.LoadAsync();
                    BeginInvoke(RefreshAdaptersList);

                    // Reconcile firewall rule on startup (MSIX upgrade detection & stale rule cleanup)
                    var reconciliation = await _firewallService.ReconcileFirewallRuleOnStartupAsync(
                        _settings.EnablePhoneAccess,
                        (title, message) =>
                        {
                            var tcs = new TaskCompletionSource<bool>();
                            BeginInvoke(() =>
                            {
                                var res = MessageBox.Show(this, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                                tcs.SetResult(res == DialogResult.Yes);
                            });
                            return tcs.Task;
                        },
                        msg => LogMessage(msg));

                    if (reconciliation == FirewallReconciliationResult.Declined)
                    {
                        _settings.EnablePhoneAccess = false;
                        await _settingsManager.SaveAsync(_settings);
                        BeginInvoke(() =>
                        {
                            _chkEnableLan.Checked = false;
                            UpdateLanStatusUi(false, null);
                        });
                    }

                    if (_settings.EnablePhoneAccess && !string.IsNullOrWhiteSpace(_settings.SelectedIpAddress))
                    {
                        var matching = NetworkInterfaceScanner.GetPrivateRfc1918Adapters()
                            .FirstOrDefault(a => a.IpAddressString == _settings.SelectedIpAddress);

                        if (matching != null)
                        {
                            LogMessage($"[EMBEDDED-RELAY] Starting embedded Relay on http://127.0.0.1:5100 and http://{_settings.SelectedIpAddress}:5100...");
                            await _relayHost.RebindAsync(_settings.SelectedIpAddress);
                            UpdateLanStatusUi(true, _settings.SelectedIpAddress);
                            LogMessage($"[EMBEDDED-RELAY] Active in Trusted Private-Network Mode (http://{_settings.SelectedIpAddress}:5100).");
                        }
                        else
                        {
                            LogMessage($"[EMBEDDED-RELAY] Saved adapter IP {_settings.SelectedIpAddress} not found on any active interface. Reverting to loopback.");
                            _settings.EnablePhoneAccess = false;
                            await _settingsManager.SaveAsync(_settings);
                            await _relayHost.StartAsync();
                            UpdateLanStatusUi(false, null);
                        }
                    }
                    else
                    {
                        LogMessage("Starting in-process Relay server on http://127.0.0.1:5100...");
                        await _relayHost.StartAsync();
                        UpdateLanStatusUi(false, null);
                    }

                    bool healthy = await _relayHost.WaitForHealthAsync(TimeSpan.FromSeconds(6));
                    if (healthy)
                    {
                        LogMessage("In-process Relay is healthy (PWA available).");
                    }
                    else
                    {
                        LogMessage("Warning: Relay health check timed out. Attempting transport connection...");
                    }

                    await _connectionClient.StartAsync();
                }
                catch (Exception ex)
                {
                    LogMessage($"Relay host / transport client error: {ex.Message}");
                }
            });
        };
    }

    private void InitializeFormComponents()
    {
        var infoVer = typeof(CompanionMainForm).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(infoVer) && infoVer.Contains('+'))
        {
            infoVer = infoVer.Split('+')[0];
        }
        string verDisplay = !string.IsNullOrWhiteSpace(infoVer) ? $"v{infoVer}" : "v0.9.0-beta.3";
        Text = $"Teams Mobile Remote — Companion ({verDisplay})";
        Size = new Size(680, 800);
        MinimumSize = new Size(640, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        BackColor = Color.FromArgb(248, 249, 250);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            RowCount = 6,
            ColumnCount = 1
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 75f));  // 0: Status
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120f)); // 1: Mic card
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f));  // 2: Mic buttons
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 105f)); // 3: Pairing & Transport
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 155f)); // 4: Mobile Phone Access (Trusted Private-Network Mode)
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // 5: Log

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

        _btnObserveReactions = new Button
        {
            Text = "✨ Observe Reactions",
            Height = 36,
            Width = 155,
            BackColor = Color.FromArgb(108, 117, 125),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnObserveReactions.Click += async (s, e) => await ObserveLiveReactionsAsync();

        pnlButtons.Controls.Add(_btnScan);
        pnlButtons.Controls.Add(_btnMute);
        pnlButtons.Controls.Add(_btnUnmute);
        pnlButtons.Controls.Add(_btnObserveReactions);
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

        _btnOpenPwa = new Button
        {
            Text = "🌐 Open Web Remote",
            Location = new Point(292, 68),
            Size = new Size(150, 32),
            BackColor = Color.FromArgb(13, 110, 253),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnOpenPwa.Click += (s, e) => OpenWebRemoteInBrowser();

        grpPairing.Controls.Add(_lblRelayStatus);
        grpPairing.Controls.Add(_lblPairedDevice);
        grpPairing.Controls.Add(_btnPairPhone);
        grpPairing.Controls.Add(_btnRevokePhone);
        grpPairing.Controls.Add(_btnOpenPwa);
        mainLayout.Controls.Add(grpPairing, 0, 3);

        // Group 5: Mobile Phone Access (Private Wi-Fi / Trusted Private-Network Mode)
        var grpLan = new GroupBox
        {
            Text = "Mobile Phone Access (Private Wi-Fi)",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(33, 37, 41)
        };

        _chkEnableLan = new CheckBox
        {
            Text = "Enable Phone Access on Private Wi-Fi",
            Location = new Point(16, 22),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(33, 37, 41)
        };
        _chkEnableLan.CheckedChanged += OnEnableLanCheckedChanged;

        var lblAdapter = new Label
        {
            Text = "Network Adapter:",
            Location = new Point(16, 52),
            AutoSize = true,
            ForeColor = Color.FromArgb(73, 80, 87)
        };

        _cmbAdapters = new ComboBox
        {
            Location = new Point(135, 48),
            Size = new Size(340, 28),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbAdapters.SelectedIndexChanged += OnAdapterSelectedIndexChanged;

        _btnRefreshAdapters = new Button
        {
            Text = "🔄 Refresh",
            Location = new Point(485, 47),
            Size = new Size(85, 29),
            BackColor = Color.FromArgb(240, 240, 240),
            FlatStyle = FlatStyle.Flat
        };
        _btnRefreshAdapters.Click += (s, e) => RefreshAdaptersList();

        _lblLanStatus = new Label
        {
            Text = "Status: Loopback only (http://127.0.0.1:5100)",
            Location = new Point(16, 82),
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(108, 117, 125)
        };

        _lblPhoneUrl = new Label
        {
            Text = "Phone URL: http://192.168.1.150:5100",
            Location = new Point(16, 104),
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(13, 110, 253),
            Visible = false
        };

        _btnCopyPhoneUrl = new Button
        {
            Text = "📋 Copy URL",
            Location = new Point(340, 100),
            Size = new Size(95, 26),
            BackColor = Color.FromArgb(13, 110, 253),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Visible = false
        };
        _btnCopyPhoneUrl.Click += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(_settings.SelectedIpAddress))
            {
                Clipboard.SetText($"http://{_settings.SelectedIpAddress}:5100");
                MessageBox.Show($"Copied http://{_settings.SelectedIpAddress}:5100 to clipboard!", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };

        _lblLanSecurityNotice = new Label
        {
            Text = "⚠️ Trusted Private-Network Mode: Unencrypted HTTP. Use only on private home/office Wi-Fi.",
            Location = new Point(16, 128),
            Size = new Size(620, 18),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
            ForeColor = Color.FromArgb(175, 75, 0),
            Visible = false
        };

        grpLan.Controls.Add(_chkEnableLan);
        grpLan.Controls.Add(lblAdapter);
        grpLan.Controls.Add(_cmbAdapters);
        grpLan.Controls.Add(_btnRefreshAdapters);
        grpLan.Controls.Add(_lblLanStatus);
        grpLan.Controls.Add(_lblPhoneUrl);
        grpLan.Controls.Add(_btnCopyPhoneUrl);
        grpLan.Controls.Add(_lblLanSecurityNotice);
        mainLayout.Controls.Add(grpLan, 0, 4);

        // Group 6: Activity Log
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
        mainLayout.Controls.Add(grpLog, 0, 5);

        Controls.Add(mainLayout);
    }

    private async Task ShowPairingQrAsync()
    {
        try
        {
            bool isLanActive = _settings.EnablePhoneAccess && !string.IsNullOrWhiteSpace(_settings.SelectedIpAddress);
            string phoneHttpUrl = isLanActive
                ? $"http://{_settings.SelectedIpAddress}:5100"
                : "http://127.0.0.1:5100";

            string clientRelayUrl = Environment.GetEnvironmentVariable("TEAMS_REMOTE_CLIENT_RELAY_URL")?.Trim() is { Length: > 0 } explicitUrl
                ? explicitUrl
                : (isLanActive ? $"ws://{_settings.SelectedIpAddress}:5100/ws/client" : "ws://127.0.0.1:5100/ws/client");

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
                version = ProtocolEnvelope.CurrentVersion,
                pairingCode = challenge.PairingCode,
                relayUrl = challenge.RelayUrl,
                shortCode = challenge.PairingCode,
                expiresAt = challenge.ExpiresAt
            });

            using var qrData = qrGen.CreateQrCode(qrPayload, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new QRCode(qrData);
            using var qrBitmap = qrCode.GetGraphic(5);

            LogMessage($"Generated single-use pairing code ({challenge.PairingCode}). Expires in 60s.");
            LogMessage($"[PAIRING] Client Relay URL: {clientRelayUrl}");

            using var qrDialog = new PairingQrDialog(
                qrBitmap,
                challenge.PairingCode,
                qrPayload,
                phoneHttpUrl,
                isLanActive);
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

            await _passiveObserver.PollOnceAsync();

            var mic = report.Controls.FirstOrDefault(c => c.TargetControl == TargetControl.Microphone);
            var camera = report.Controls.FirstOrDefault(c => c.TargetControl == TargetControl.Camera);

            if (mic != null && mic.Found && mic.ReadableState != TeamsControlState.Unknown)
            {
                LogMessage($"Scan complete: Microphone is {mic.ReadableState.ToUpperInvariant()} (Tier: {mic.CapabilityTier.ToWireString()}).");
            }
            if (camera != null && camera.Found && camera.ReadableState != TeamsControlState.Unknown)
            {
                LogMessage($"Scan complete: Camera is {camera.ReadableState.ToUpperInvariant()} (Tier: {camera.CapabilityTier.ToWireString()}).");
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Scan failed: {RedactionHelper.RedactSecrets(ex.Message)}");
        }
    }

    private async Task ObserveLiveReactionsAsync()
    {
        if (_isActionInProgress) return;

        LogMessage("=== Phase 6A: User-Assisted Live Reactions Observation (Read-Only) ===");
        LogMessage("INSTRUCTIONS: Please manually hover over or click 'React and raise hand' in your Teams meeting window to open the reactions menu...");

        try
        {
            var report = await _controller.ObserveReactionsAsync(
                TimeSpan.FromSeconds(10),
                msg => LogMessage($"[REACTION OBSERVER] {msg}"));

            if (!report.MeetingDetected)
            {
                LogMessage("Observation finished: No active Teams meeting window detected.");
                return;
            }

            LogMessage($"Launcher Found: {report.ReactLauncherFound} (Name: '{report.LauncherName}', ID: '{report.LauncherAutomationId}', Type: {report.LauncherControlType}, Enabled: {report.LauncherIsEnabled})");
            LogMessage($"Flyout Observed: {report.FlyoutObserved} (Hosting: {report.FlyoutHostingType ?? "None"})");
            LogMessage($"Policy Status: {report.PolicyStatus} {(report.PolicyReason != null ? $"({report.PolicyReason})" : "")}");

            if (report.DiscoveredReactions.Count > 0)
            {
                LogMessage($"Discovered {report.DiscoveredReactions.Count} Reaction Controls:");
                foreach (var r in report.DiscoveredReactions)
                {
                    LogMessage($"  - [{r.ReactionType.ToUpperInvariant()}] Name: '{r.AccessibleName}', AutoId: '{r.AutomationId}', Type: {r.ControlType}, InvokePattern: {r.SupportsInvokePattern}, Enabled: {r.IsEnabled}, Bounds: {r.BoundingRectangle}");
                }
            }
            else
            {
                LogMessage("No reaction buttons discovered in tree during observation window. Ensure the reaction flyout menu was opened in Teams.");
            }

            LogMessage("=== Phase 6A Observation Complete (Genuinely Read-Only) ===");
        }
        catch (Exception ex)
        {
            LogMessage($"Reaction observation failed: {RedactionHelper.RedactSecrets(ex.Message)}");
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

        Logging.CompanionLog.Info(sanitized, "CompanionUI");

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

    private void OpenWebRemoteInBrowser()
    {
        try
        {
            string targetUrl = _settings.EnablePhoneAccess && !string.IsNullOrWhiteSpace(_settings.SelectedIpAddress)
                ? $"http://{_settings.SelectedIpAddress}:5100"
                : "http://127.0.0.1:5100";

            Process.Start(new ProcessStartInfo(targetUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogMessage($"Failed to launch browser: {ex.Message}");
        }
    }

    private sealed record NetworkAdapterComboItem(NetworkAdapterInfo? Adapter, string Text)
    {
        public override string ToString() => Text;
    }

    private void RefreshAdaptersList()
    {
        _availableAdapters = NetworkInterfaceScanner.GetPrivateRfc1918Adapters();
        _cmbAdapters.Items.Clear();
        _cmbAdapters.Items.Add(new NetworkAdapterComboItem(null, "-- Select an adapter --"));

        int selectIndex = 0;
        for (int i = 0; i < _availableAdapters.Count; i++)
        {
            var adapter = _availableAdapters[i];
            _cmbAdapters.Items.Add(new NetworkAdapterComboItem(adapter, adapter.DisplayName));

            if (_settings.EnablePhoneAccess && !string.IsNullOrWhiteSpace(_settings.SelectedAdapterId) &&
                adapter.Id == _settings.SelectedAdapterId)
            {
                selectIndex = i + 1;
            }
        }

        _cmbAdapters.SelectedIndex = selectIndex;
    }

    private void OnAdapterSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_cmbAdapters.SelectedItem is NetworkAdapterComboItem item && item.Adapter != null)
        {
            _settings.SelectedAdapterId = item.Adapter.Id;
            _settings.SelectedIpAddress = item.Adapter.IpAddressString;
        }
    }

    private async void OnEnableLanCheckedChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingLanSettingsUi) return;

        if (_chkEnableLan.Checked)
        {
            // Require explicit adapter selection: never auto-select
            if (_cmbAdapters.SelectedIndex <= 0 || _cmbAdapters.SelectedItem is not NetworkAdapterComboItem selectedItem || selectedItem.Adapter == null)
            {
                MessageBox.Show(
                    "Please select an active private Wi-Fi or Ethernet adapter from the list before enabling phone access.",
                    "Adapter Required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                _isUpdatingLanSettingsUi = true;
                _chkEnableLan.Checked = false;
                _isUpdatingLanSettingsUi = false;
                return;
            }

            var adapter = selectedItem.Adapter;

            if (!NetworkInterfaceScanner.IsPrivateRfc1918(adapter.IpAddress))
            {
                MessageBox.Show(
                    $"The selected network adapter IP ({adapter.IpAddressString}) is invalid or not an active private RFC 1918 interface.",
                    "Invalid Network Adapter",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                _isUpdatingLanSettingsUi = true;
                _chkEnableLan.Checked = false;
                _isUpdatingLanSettingsUi = false;
                return;
            }

            // Step 1: User consent explanation
            string consentMessage =
                $"Enabling phone access allows your mobile device to open Teams Remote at http://{adapter.IpAddressString}:5100 on your local Wi-Fi.\n\n" +
                "Windows Defender Firewall requires administrator elevation (UAC) to configure the inbound rule:\n" +
                $"• Name: {WindowsFirewallService.RuleName}\n" +
                $"• Scope: {_firewallService.GetExecutablePath()}\n" +
                "• Profile: Private network only\n" +
                "• Remote Address: LocalSubnet only\n" +
                "• Port: TCP 5100\n\n" +
                "⚠️ Security Notice (Trusted Private-Network Mode):\n" +
                "Local Wi-Fi traffic in this phase is unencrypted HTTP. Use only on trusted, password-protected private networks.\n\n" +
                "Do you want to proceed and open the Windows elevation prompt?";

            var consentResult = MessageBox.Show(
                consentMessage,
                "Enable Phone Access — Trusted Private-Network Mode",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (consentResult != DialogResult.Yes)
            {
                _isUpdatingLanSettingsUi = true;
                _chkEnableLan.Checked = false;
                _isUpdatingLanSettingsUi = false;
                LogMessage("LAN phone access activation was cancelled by the user.");
                UpdateLanStatusUi(false, null);
                return;
            }

            // Step 2: Elevated firewall rule creation
            LogMessage("Requesting administrator approval (UAC) for Windows Firewall rule...");
            var (firewallSuccess, firewallError) = await _firewallService.CreateRuleElevatedAsync();

            if (!firewallSuccess)
            {
                _isUpdatingLanSettingsUi = true;
                _chkEnableLan.Checked = false;
                _isUpdatingLanSettingsUi = false;

                MessageBox.Show(
                    $"Firewall configuration failed.\n\nDetails: {firewallError}\n\nPhone access remains disabled. Teams Remote is running in loopback-only mode.",
                    "Firewall Setup Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                LogMessage($"[FIREWALL] Activation aborted: {firewallError}");
                UpdateLanStatusUi(false, null);
                return;
            }

            LogMessage($"[FIREWALL] Rule '{WindowsFirewallService.RuleName}' created successfully for {adapter.IpAddressString} (LocalSubnet).");

            // Rebind Kestrel in-process host
            try
            {
                LogMessage($"Rebinding Embedded Relay to 127.0.0.1 and {adapter.IpAddressString}:5100...");
                await _relayHost.RebindAsync(adapter.IpAddressString);
                LogMessage($"[EMBEDDED-RELAY] Active on http://127.0.0.1:5100 and http://{adapter.IpAddressString}:5100");
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to rebind Relay to {adapter.IpAddressString}: {ex.Message}");
            }

            _settings.EnablePhoneAccess = true;
            _settings.SelectedAdapterId = adapter.Id;
            _settings.SelectedIpAddress = adapter.IpAddressString;
            await _settingsManager.SaveAsync(_settings);

            UpdateLanStatusUi(true, adapter.IpAddressString);
        }
        else
        {
            // User unchecks setting -> clean teardown
            LogMessage("Disabling phone access on local Wi-Fi. Rebinding to loopback-only...");

            try
            {
                await _relayHost.RebindAsync(null);
                LogMessage("[EMBEDDED-RELAY] Bound strictly to loopback: http://127.0.0.1:5100");
            }
            catch (Exception ex)
            {
                LogMessage($"Error rebinding Relay to loopback: {ex.Message}");
            }

            // Remove firewall rule
            var (removeSuccess, removeError) = await _firewallService.RemoveRuleElevatedAsync();
            if (removeSuccess)
            {
                LogMessage($"[FIREWALL] Removed rule '{WindowsFirewallService.RuleName}'.");
            }
            else
            {
                LogMessage($"[FIREWALL] Rule removal note: {removeError}");
            }

            _settings.EnablePhoneAccess = false;
            await _settingsManager.SaveAsync(_settings);

            UpdateLanStatusUi(false, null);
        }
    }

    private void UpdateLanStatusUi(bool isLanActive, string? lanIp)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateLanStatusUi(isLanActive, lanIp));
            return;
        }

        _isUpdatingLanSettingsUi = true;
        _chkEnableLan.Checked = isLanActive;
        _isUpdatingLanSettingsUi = false;

        if (isLanActive && !string.IsNullOrWhiteSpace(lanIp))
        {
            _lblLanStatus.Text = "Status: Active — Trusted Private-Network Mode (Local Wi-Fi Only)";
            _lblLanStatus.ForeColor = Color.FromArgb(25, 135, 84); // Green
            _lblPhoneUrl.Text = $"Phone URL: http://{lanIp}:5100";
            _lblPhoneUrl.Visible = true;
            _btnCopyPhoneUrl.Visible = true;
            _lblLanSecurityNotice.Visible = true;
        }
        else
        {
            _lblLanStatus.Text = "Status: Loopback only (http://127.0.0.1:5100)";
            _lblLanStatus.ForeColor = Color.FromArgb(108, 117, 125); // Gray
            _lblPhoneUrl.Visible = false;
            _btnCopyPhoneUrl.Visible = false;
            _lblLanSecurityNotice.Visible = false;
        }
    }

    private void ExitApplication()
    {
        _passiveObserver.Dispose();
        _isExiting = true;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _controller.Dispose();
        _ = _connectionClient.DisposeAsync();
        _relayHost.Dispose();
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
            _passiveObserver.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _controller.Dispose();
            _ = _connectionClient.DisposeAsync();
            _relayHost.Dispose();
            base.OnFormClosing(e);
        }
    }
}
