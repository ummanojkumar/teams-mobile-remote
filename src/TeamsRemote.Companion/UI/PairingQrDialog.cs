using System.Drawing;
using System.Windows.Forms;

namespace TeamsRemote.Companion.UI;

public sealed class PairingQrDialog : Form
{
    private readonly System.Windows.Forms.Timer _countdownTimer;
    private int _secondsRemaining = 60;
    private readonly Label _lblTimer;

    public PairingQrDialog(Bitmap qrImage, string confirmationCode, string? rawPayload = null, string? phoneUrl = null, bool isLanMode = false)
    {
        Text = isLanMode
            ? "Pair Mobile Remote (Trusted Private-Network Mode)"
            : "Pair Mobile Remote (Single-Use QR)";
        Size = new Size(400, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        BackColor = Color.FromArgb(248, 249, 250);

        string instructionText = isLanMode && !string.IsNullOrWhiteSpace(phoneUrl)
            ? $"On your phone (connected to same Wi-Fi),\nopen {phoneUrl} and enter this code:"
            : "Scan this single-use QR code in your phone's browser:";

        var lblInstruction = new Label
        {
            Text = instructionText,
            Location = new Point(20, 12),
            Size = new Size(360, 40),
            TextAlign = ContentAlignment.TopCenter,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(33, 37, 41)
        };

        var picQr = new PictureBox
        {
            Image = new Bitmap(qrImage),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(105, 58),
            Size = new Size(190, 190),
            BorderStyle = BorderStyle.FixedSingle
        };

        var lblCodeDesc = new Label
        {
            Text = "Pairing Code (Enter on Phone):",
            Location = new Point(20, 256),
            Size = new Size(360, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(108, 117, 125)
        };

        var lblCode = new Label
        {
            Text = confirmationCode,
            Location = new Point(80, 280),
            Size = new Size(240, 42),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Consolas", 22f, FontStyle.Bold),
            ForeColor = Color.FromArgb(13, 110, 253),
            BackColor = Color.FromArgb(233, 236, 239),
            BorderStyle = BorderStyle.FixedSingle
        };

        _lblTimer = new Label
        {
            Text = "Expires in: 60s (Single-use)",
            Location = new Point(20, 330),
            Size = new Size(360, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9f, FontStyle.Italic),
            ForeColor = Color.FromArgb(220, 53, 69)
        };

        var lblModeNotice = new Label
        {
            Text = isLanMode
                ? "🔒 Trusted Private-Network Mode (Local Wi-Fi Only)"
                : "🔒 Loopback Security Mode (127.0.0.1)",
            Location = new Point(20, 355),
            Size = new Size(360, 36),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = isLanMode ? Color.FromArgb(114, 56, 0) : Color.FromArgb(108, 117, 125)
        };

        var btnCopy = new Button
        {
            Text = "Copy Code",
            Location = new Point(80, 400),
            Size = new Size(115, 36),
            BackColor = Color.FromArgb(13, 110, 253),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnCopy.Click += (s, e) =>
        {
            Clipboard.SetText(confirmationCode);
            MessageBox.Show($"Pairing code '{confirmationCode}' copied to clipboard!", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        var btnClose = new Button
        {
            Text = "Close",
            Location = new Point(205, 400),
            Size = new Size(115, 36),
            BackColor = Color.FromArgb(108, 117, 125),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnClose.Click += (s, e) => Close();

        Controls.Add(lblInstruction);
        Controls.Add(picQr);
        Controls.Add(lblCodeDesc);
        Controls.Add(lblCode);
        Controls.Add(_lblTimer);
        Controls.Add(lblModeNotice);
        Controls.Add(btnCopy);
        Controls.Add(btnClose);

        _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _countdownTimer.Tick += (s, e) =>
        {
            _secondsRemaining--;
            if (_secondsRemaining <= 0)
            {
                _countdownTimer.Stop();
                _lblTimer.Text = "EXPIRED (Close and generate new code)";
                _lblTimer.ForeColor = Color.Red;
                picQr.Enabled = false;
            }
            else
            {
                _lblTimer.Text = $"Expires in: {_secondsRemaining}s (Single-use)";
            }
        };
        _countdownTimer.Start();

        FormClosing += (s, e) =>
        {
            _countdownTimer.Stop();
            _countdownTimer.Dispose();
        };
    }
}
