using System.Drawing;
using System.Windows.Forms;

namespace Proxion.Personal.UI;

/// <summary>
/// The only window this fork shows: a disclaimer naming exactly which third-party
/// proxy PURPLE's traffic will be routed through, requiring an explicit acknowledgement
/// before anything starts. After clicking Continue, open PURPLE yourself - Proxion
/// moves to the tray immediately and detects it running in the background.
/// </summary>
public sealed class DisclaimerForm : Form
{
    private readonly CheckBox _ackBox = new() { Text = "I understand and want to continue" };
    private readonly Button _continueButton = new() { Text = "Continue", Enabled = false };

    public DisclaimerForm()
    {
        Text = "Proxion";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 300);
        Font = new Font("Segoe UI", 9f);
        Icon = IconLoader.Load();

        var proxy = PersonalProxyConfig.Proxy;
        var disclaimer = new Label
        {
            Text = "This app will route NCSOFT PURPLE's game traffic through a "
                 + "third-party proxy server:" + Environment.NewLine + Environment.NewLine
                 + $"        {proxy.TypeLabel.ToUpperInvariant()}  {proxy.Host}:{proxy.Port}" + Environment.NewLine + Environment.NewLine
                 + "All traffic from PURPLE, and from any game it launches, will pass "
                 + "through this server for as long as this app is running." + Environment.NewLine + Environment.NewLine
                 + "After you continue, open PURPLE yourself - Proxion will detect it "
                 + "automatically and move to the system tray.",
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 220,
            Padding = new Padding(16, 16, 16, 0),
        };

        _ackBox.Dock = DockStyle.Top;
        _ackBox.Padding = new Padding(16, 0, 16, 0);
        _ackBox.CheckedChanged += (_, _) => _continueButton.Enabled = _ackBox.Checked;

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 52,
            Padding = new Padding(16),
        };
        var cancelButton = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
        _continueButton.Width = 90;
        _continueButton.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        buttonPanel.Controls.Add(_continueButton);
        buttonPanel.Controls.Add(cancelButton);

        Controls.Add(disclaimer);
        Controls.Add(_ackBox);
        Controls.Add(buttonPanel);
        AcceptButton = _continueButton;
        CancelButton = cancelButton;
    }
}
