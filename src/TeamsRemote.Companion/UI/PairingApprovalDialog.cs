using System.Drawing;
using System.Windows.Forms;

namespace TeamsRemote.Companion.UI;

public sealed class PairingApprovalDialog : Form
{
    public bool Approved { get; private set; }

    public PairingApprovalDialog(string deviceLabel, string confirmationCode)
    {
        Text = "Approve Phone Pairing";
        Size = new Size(420, 260);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        BackColor = Color.FromArgb(248, 249, 250);

        var lblPrompt = new Label
        {
            Text = $"Incoming pairing request from:\n{deviceLabel}",
            Location = new Point(24, 20),
            Size = new Size(360, 45),
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(33, 37, 41)
        };

        var lblCodeDesc = new Label
        {
            Text = "Verify this pairing code matches the code on your phone:",
            Location = new Point(24, 75),
            Size = new Size(360, 20),
            ForeColor = Color.FromArgb(108, 117, 125)
        };

        var lblCode = new Label
        {
            Text = confirmationCode,
            Location = new Point(24, 100),
            Size = new Size(360, 45),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Consolas", 22f, FontStyle.Bold),
            ForeColor = Color.FromArgb(13, 110, 253),
            BackColor = Color.FromArgb(233, 236, 239),
            BorderStyle = BorderStyle.FixedSingle
        };

        var btnApprove = new Button
        {
            Text = "✓ Approve",
            Location = new Point(190, 165),
            Size = new Size(100, 36),
            BackColor = Color.FromArgb(25, 135, 84),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnApprove.Click += (s, e) =>
        {
            Approved = true;
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnReject = new Button
        {
            Text = "✕ Reject",
            Location = new Point(300, 165),
            Size = new Size(84, 36),
            BackColor = Color.FromArgb(108, 117, 125),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnReject.Click += (s, e) =>
        {
            Approved = false;
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Controls.Add(lblPrompt);
        Controls.Add(lblCodeDesc);
        Controls.Add(lblCode);
        Controls.Add(btnApprove);
        Controls.Add(btnReject);

        AcceptButton = btnApprove;
        CancelButton = btnReject;
    }
}
