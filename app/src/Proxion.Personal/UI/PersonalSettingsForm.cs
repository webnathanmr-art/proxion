using System.Drawing;
using System.Windows.Forms;
using Proxion.Windows;

namespace Proxion.Personal.UI;

/// <summary>
/// The one window this fork shows: where PURPLE is installed (auto-detected, or browse
/// for it yourself), a disclaimer naming exactly which third-party proxy its traffic
/// will be routed through, and a button to launch PURPLE - after which this window
/// hides to the tray. It isn't destroyed when hidden, so the tray icon can bring it
/// back later to change the PURPLE path or the auto-run setting.
/// </summary>
public sealed class PersonalSettingsForm : Form
{
    private readonly TextBox _purplePathBox = new() { ReadOnly = true };
    private readonly CheckBox _ackBox = new() { Text = "I understand and want to continue" };
    private readonly CheckBox _autoRunBox = new() { Text = "Automatically launch PURPLE next time Proxion starts" };
    private readonly Button _launchButton = new() { Text = "Launch PURPLE", Enabled = false, Width = 110 };
    private readonly Label _statusLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Text = "Not started yet." };

    /// <summary>Raised when the user clicks Launch PURPLE, with the path they picked.</summary>
    public event EventHandler<string>? LaunchRequested;

    /// <summary>
    /// Set by the owner once a session is running. While true, closing this window
    /// (the X button, or Cancel) just hides it instead of exiting the whole app.
    /// </summary>
    public bool SessionActive { get; set; }

    public string PurplePath => _purplePathBox.Text;

    public PersonalSettingsForm()
    {
        Text = "Proxion";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(480, 400);
        Font = new Font("Segoe UI", 9f);
        Icon = IconLoader.Load();

        var purpleRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            AutoSize = true,
            Padding = new Padding(16, 16, 16, 0),
        };
        purpleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        purpleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        purpleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        purpleRow.Controls.Add(new Label { Text = "PURPLE launcher:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 0) }, 0, 0);
        _purplePathBox.Dock = DockStyle.Fill;
        _purplePathBox.Margin = new Padding(0, 4, 4, 4);
        purpleRow.Controls.Add(_purplePathBox, 1, 0);
        var browseButton = new Button { Text = "Browse...", Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
        browseButton.Click += OnBrowseClicked;
        purpleRow.Controls.Add(browseButton, 2, 0);

        var proxy = PersonalProxyConfig.Proxy;
        var disclaimer = new Label
        {
            Text = "This app will route NCSOFT PURPLE's game traffic through a "
                 + "third-party proxy server:" + Environment.NewLine + Environment.NewLine
                 + $"        {proxy.TypeLabel.ToUpperInvariant()}  {proxy.Host}:{proxy.Port}" + Environment.NewLine + Environment.NewLine
                 + "All traffic from PURPLE, and from any game it launches, will pass "
                 + "through this server for as long as this app is running.",
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 150,
            Padding = new Padding(16, 16, 16, 0),
        };

        _ackBox.Dock = DockStyle.Top;
        _ackBox.Padding = new Padding(16, 0, 16, 0);
        _ackBox.CheckedChanged += (_, _) => UpdateLaunchButtonEnabled();

        _autoRunBox.Dock = DockStyle.Top;
        _autoRunBox.Padding = new Padding(16, 4, 16, 0);

        _statusLabel.Dock = DockStyle.Top;
        _statusLabel.Padding = new Padding(16, 12, 16, 0);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 52,
            Padding = new Padding(16),
        };
        var cancelButton = new Button { Text = "Cancel", Width = 90 };
        cancelButton.Click += (_, _) => Close();
        _launchButton.Click += (_, _) => LaunchRequested?.Invoke(this, _purplePathBox.Text);
        buttonPanel.Controls.Add(_launchButton);
        buttonPanel.Controls.Add(cancelButton);

        Controls.Add(_statusLabel);
        Controls.Add(_autoRunBox);
        Controls.Add(_ackBox);
        Controls.Add(disclaimer);
        Controls.Add(purpleRow);
        Controls.Add(buttonPanel);
        AcceptButton = _launchButton;

        FormClosing += OnFormClosing;
        Load += (_, _) => RefreshFromStorage();

        // Populate the fields immediately, not just on Load: the auto-run startup path
        // calls SaveCurrentSettings() (which reads these same fields) without ever
        // calling Show() first, and Load only fires the first time a form is actually
        // shown. Without this, auto-run would read back an empty PurplePath/AutoRun
        // from a never-populated textbox and silently overwrite the real stored values
        // with blanks on every single auto-run launch.
        RefreshFromStorage();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && SessionActive)
        {
            e.Cancel = true;
            Hide();
        }
    }

    public void RefreshFromStorage()
    {
        var stored = SettingsStore.Load();
        _purplePathBox.Text = PathIfExists(stored.PurpleLauncherPath) ?? PurpleLocator.TryAutoDetect() ?? string.Empty;
        _autoRunBox.Checked = stored.AutoRunPurple;
        UpdateLaunchButtonEnabled();
    }

    private static string? PathIfExists(string path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;

    private void OnBrowseClicked(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Locate PurpleLauncher.exe",
            Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (!string.IsNullOrWhiteSpace(_purplePathBox.Text) && File.Exists(_purplePathBox.Text))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_purplePathBox.Text);
        }
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _purplePathBox.Text = dialog.FileName;
            UpdateLaunchButtonEnabled();
        }
    }

    private void UpdateLaunchButtonEnabled()
    {
        _launchButton.Enabled = _ackBox.Checked
            && !string.IsNullOrWhiteSpace(_purplePathBox.Text)
            && File.Exists(_purplePathBox.Text);
    }

    public void SetStatus(string text) => _statusLabel.Text = text;

    /// <summary>Persists the current PURPLE path and auto-run choice, without touching any other stored settings.</summary>
    public void SaveCurrentSettings()
    {
        var stored = SettingsStore.Load();
        stored.PurpleLauncherPath = _purplePathBox.Text;
        stored.AutoRunPurple = _autoRunBox.Checked;
        SettingsStore.Save(stored);
    }
}
