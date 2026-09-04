using System.Drawing;
using System.Windows.Forms;
using Proxion.Core;
using Proxion.Windows;

namespace Proxion.App.UI;

/// <summary>What the setup window collected, once the user clicks Start.</summary>
public sealed record SessionSetupResult(string PurplePath, ProxySettings Proxy, bool LocalhostViaProxy);

/// <summary>
/// The window shown on launch: where PURPLE is installed, and the proxy to route its
/// traffic through. PURPLE's path is pre-filled by auto-detection (or the last value
/// used), but always editable via Browse. ProxyBridge itself is bundled with Proxion,
/// so there's nothing to locate for it.
/// </summary>
public sealed class SetupForm : Form
{
    private readonly TextBox _purplePathBox = new() { ReadOnly = true };
    private readonly ComboBox _proxyTypeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _hostBox = new();
    private readonly TextBox _portBox = new();
    private readonly TextBox _usernameBox = new();
    private readonly TextBox _passwordBox = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _localhostViaProxyBox = new() { Text = "Also route localhost (127.x.x.x / ::1) traffic through the proxy" };

    public SessionSetupResult? Result { get; private set; }

    public SetupForm()
    {
        Text = "Proxion Setup";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(540, 380);
        Font = new Font("Segoe UI", 9f);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            AutoSize = true,
            Padding = new Padding(16, 16, 16, 0),
            RowCount = 0,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));

        AddPathRow(layout, "PURPLE launcher:", _purplePathBox, OnBrowsePurple);
        AddSectionLabel(layout, "Proxy");

        _proxyTypeBox.Items.AddRange(new object[] { "SOCKS5", "HTTP" });
        _proxyTypeBox.SelectedIndex = 0;
        AddRow(layout, "Type:", _proxyTypeBox);
        AddRow(layout, "Host:", _hostBox);
        AddRow(layout, "Port:", _portBox);
        AddRow(layout, "Username (optional):", _usernameBox);
        AddRow(layout, "Password (optional):", _passwordBox);

        layout.RowCount++;
        layout.Controls.Add(_localhostViaProxyBox, 0, layout.RowCount - 1);
        layout.SetColumnSpan(_localhostViaProxyBox, 3);

        var noteLabel = new Label
        {
            Text = "Only NCSOFT PURPLE and the games it actually launches will be routed through this proxy - "
                 + "nothing else on your system.",
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(16, 8, 16, 0),
            ForeColor = SystemColors.GrayText,
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 52,
            Padding = new Padding(16),
        };
        var startButton = new Button { Text = "Start", Width = 90, DialogResult = DialogResult.None };
        var cancelButton = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
        startButton.Click += OnStartClicked;
        buttonPanel.Controls.Add(startButton);
        buttonPanel.Controls.Add(cancelButton);

        Controls.Add(layout);
        Controls.Add(noteLabel);
        Controls.Add(buttonPanel);
        AcceptButton = startButton;
        CancelButton = cancelButton;

        Load += (_, _) => PreFill();
    }

    private void AddRow(TableLayoutPanel layout, string label, Control control)
    {
        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var row = layout.RowCount - 1;
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 0) }, 0, row);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 4, 0, 4);
        layout.Controls.Add(control, 1, row);
        layout.SetColumnSpan(control, 2);
    }

    private void AddPathRow(TableLayoutPanel layout, string label, TextBox pathBox, EventHandler onBrowse)
    {
        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var row = layout.RowCount - 1;
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 0) }, 0, row);
        pathBox.Dock = DockStyle.Fill;
        pathBox.Margin = new Padding(0, 4, 4, 4);
        layout.Controls.Add(pathBox, 1, row);
        var browse = new Button { Text = "Browse...", Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
        browse.Click += onBrowse;
        layout.Controls.Add(browse, 2, row);
    }

    private void AddSectionLabel(TableLayoutPanel layout, string text)
    {
        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 16, 0, 4),
        };
        layout.Controls.Add(label, 0, layout.RowCount - 1);
        layout.SetColumnSpan(label, 3);
    }

    private void PreFill()
    {
        var stored = SettingsStore.Load();

        _purplePathBox.Text = PathIfExists(stored.PurpleLauncherPath) ?? PurpleLocator.TryAutoDetect() ?? string.Empty;

        _proxyTypeBox.SelectedIndex = stored.ProxyType.Equals("http", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _hostBox.Text = stored.ProxyHost;
        _portBox.Text = stored.ProxyPort > 0 ? stored.ProxyPort.ToString() : string.Empty;
        _usernameBox.Text = stored.ProxyUsername;
        _passwordBox.Text = SettingsStore.UnprotectPassword(stored.ProtectedPassword);
    }

    private static string? PathIfExists(string path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;

    private void OnBrowsePurple(object? sender, EventArgs e) => BrowseForExe(_purplePathBox, "Locate PurpleLauncher.exe");

    private void BrowseForExe(TextBox target, string title)
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (!string.IsNullOrWhiteSpace(target.Text) && File.Exists(target.Text))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(target.Text);
        }
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dialog.FileName;
        }
    }

    private void OnStartClicked(object? sender, EventArgs e)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(_purplePathBox.Text) || !File.Exists(_purplePathBox.Text))
        {
            errors.Add("Select the PURPLE launcher executable (PurpleLauncher.exe).");
        }

        var proxy = new ProxySettings
        {
            Type = _proxyTypeBox.SelectedIndex == 1 ? ProxyType.Http : ProxyType.Socks5,
            Host = _hostBox.Text.Trim(),
            Username = _usernameBox.Text,
            Password = _passwordBox.Text,
        };
        if (!int.TryParse(_portBox.Text.Trim(), out var port))
        {
            errors.Add("Proxy port must be a number.");
        }
        else
        {
            proxy.Port = port;
        }
        errors.AddRange(proxy.Validate());

        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Check your settings",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var stored = SettingsStore.FromProxySettings(_purplePathBox.Text, proxy);
        SettingsStore.Save(stored);

        Result = new SessionSetupResult(_purplePathBox.Text, proxy, _localhostViaProxyBox.Checked);
        DialogResult = DialogResult.OK;
        Close();
    }
}
