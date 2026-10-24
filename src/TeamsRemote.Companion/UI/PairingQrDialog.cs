using System.Drawing;
using System.Windows.Forms;

namespace TeamsRemote.Companion.UI;

public sealed class PairingQrDialog : Form
{
    private readonly System.Windows.Forms.Timer _countdownTimer;
    private int _secondsRemaining = 60;
    private readonly Label _lblTimer;

    public PairingQrDialog(Bitmap qrImage, string confirmationCode)
    {
        Text = "Pair Mobile Remote (Single-Use QR)";
        Size = new Size(380, 480);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        BackColor = Color.FromArgb(248, 249, 250);

        var lblInstruction = new Label
        {
            Text = "Scan this single-use QR code in your phone's browser:",
            Location = new Point(20, 16),
            Size = new Size(340, 36),
            TextAlign = ContentAlignment.TopCenter,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(33, 37, 41)
        };

        var picQr = new PictureBox
        {
            Image = new Bitmap(qrImage),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(90, 56),
            Size = new Size(200, 200),
            BorderStyle = BorderStyle.FixedSingle
        };

        var lblCodeDesc = new Label
        {
            Text = "Matching Confirmation Code:",
            Location = new Point(20, 268),
            Size = new Size(340, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(108, 117, 125)
        };

        var lblCode = new Label
        {
            Text = confirmationCode,
            Location = new Point(70, 292),
            Size = new Size(240, 42),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(13, 110, 253),
            BackColor = Color.FromArgb(233, 236, 239),
            BorderStyle = BorderStyle.FixedSingle
        };

        _lblTimer = new Label
        {
            Text = "Expires in: 60s (Single-use)",
            Location = new Point(20, 345),
            Size = new Size(340, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9f, FontStyle.Italic),
            ForeColor = Color.FromArgb(220, 53, 69)
        };

        var btnClose = new Button
        {
            Text = "Close",
            Location = new Point(140, 380),
            Size = new Size(100, 36),
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
        Controls.Add(btnClose);

        _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _countdownTimer.Tick += (s, e) =>
        {
            _secondsRemaining--;
            if (_secondsRemaining <= 0)
            {
                _countdownTimer.Stop();
                _lblTimer.Text = "EXPIRED (Close and generate new QR)";
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
