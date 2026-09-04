using System.Drawing;
using System.Windows.Forms;
using Proxion.Core;

namespace Proxion.Personal.UI;

/// <summary>
/// A modal dialog (opened from the tray icon's "Advanced Settings...") for overriding
/// the proxy this build was compiled with, without needing to rebuild it. Pre-filled
/// with whatever proxy is currently in effect (the hardcoded default, or a previously
/// saved override).
/// </summary>
public sealed class AdvancedProxySettingsForm : Form
{
    private readonly ComboBox _typeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _hostBox = new();
    private readonly TextBox _portBox = new();
    private readonly TextBox _usernameBox = new();
    private readonly TextBox _passwordBox = new() { UseSystemPasswordChar = true };
    private readonly ComboBox _protocolBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    public ProxySettings? Result { get; private set; }

    public AdvancedProxySettingsForm(ProxySettings current)
    {
        Text = "Proxion - Advanced Proxy Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(420, 380);
        Font = new Font("Segoe UI", 9f);
        Icon = IconLoader.Load();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(16, 16, 16, 0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _typeBox.Items.AddRange(new object[] { "SOCKS5", "HTTP" });
        AddRow(layout, "Type:", _typeBox);
        AddRow(layout, "Host:", _hostBox);
        AddRow(layout, "Port:", _portBox);
        AddRow(layout, "Username:", _usernameBox);
        AddRow(layout, "Password:", _passwordBox);
        _protocolBox.Items.AddRange(new object[] { "Both (default)", "TCP only", "UDP only" });
        AddRow(layout, "Route:", _protocolBox);

        _typeBox.SelectedIndex = current.Type == ProxyType.Http ? 1 : 0;
        _hostBox.Text = current.Host;
        _portBox.Text = current.Port > 0 ? current.Port.ToString() : string.Empty;
        _usernameBox.Text = current.Username;
        _passwordBox.Text = current.Password;
        _protocolBox.SelectedIndex = current.Protocol switch
        {
            RuleProtocol.TcpOnly => 1,
            RuleProtocol.UdpOnly => 2,
            _ => 0,
        };

        var note = new Label
        {
            Text = "This overrides the proxy this build was compiled with. Takes effect "
                 + "immediately if PURPLE is already running, and is remembered for next time."
                 + Environment.NewLine + Environment.NewLine
                 + "If a game connects but then times out, its traffic likely relies on UDP and "
                 + "your proxy probably doesn't support relaying it (most SOCKS5 proxies don't) - "
                 + "try \"TCP only\" so UDP goes direct instead of through a tunnel that can't carry it.",
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 130,
            Padding = new Padding(16, 12, 16, 0),
            ForeColor = SystemColors.GrayText,
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 52,
            Padding = new Padding(16),
        };
        var saveButton = new Button { Text = "Save", Width = 90 };
        var cancelButton = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
        saveButton.Click += OnSaveClicked;
        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(cancelButton);

        Controls.Add(note);
        Controls.Add(layout);
        Controls.Add(buttonPanel);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private static void AddRow(TableLayoutPanel layout, string label, Control control)
    {
        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var row = layout.RowCount - 1;
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 0) }, 0, row);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 4, 0, 4);
        layout.Controls.Add(control, 1, row);
    }

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        var proxy = new ProxySettings
        {
            Type = _typeBox.SelectedIndex == 1 ? ProxyType.Http : ProxyType.Socks5,
            Host = _hostBox.Text.Trim(),
            Username = _usernameBox.Text,
            Password = _passwordBox.Text,
            Protocol = _protocolBox.SelectedIndex switch
            {
                1 => RuleProtocol.TcpOnly,
                2 => RuleProtocol.UdpOnly,
                _ => RuleProtocol.Both,
            },
        };

        var errors = new List<string>();
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

        Result = proxy;
        DialogResult = DialogResult.OK;
        Close();
    }
}
