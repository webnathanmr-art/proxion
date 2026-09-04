using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Windows.Forms;
using Proxion.Core;
using Proxion.Windows;

namespace Proxion.Personal.UI;

/// <summary>
/// Owns the whole personal-fork session: the settings window, the tray icon (which
/// doubles as a live ping indicator for the proxy), and (once PURPLE is launched) the
/// same process-tree-scoped proxy rule as the general app. PURPLE can be launched
/// either by clicking the window's button, or automatically on startup if the
/// "auto-run" setting was previously enabled - either way, the window hides to the
/// tray once launched, and the tray icon can always bring it back.
/// </summary>
public sealed class PersonalAppContext : ApplicationContext
{
    private const int PollIntervalMs = 3000;
    private const int PingIntervalMs = 10_000;
    private const int PingTimeoutMs = 2000;
    private const int ProxyBridgeVerbosity = 3; // both logs and connection events - see _proxyBridgeLogPath

    private readonly SessionLogger _logger;
    private readonly string _proxyBridgeLogPath;
    private readonly ProxyBridgeProcessRunner _bridge = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _pingTimer;
    private readonly Ping _ping = new();
    private readonly NotifyIcon _trayIcon;
    private readonly PersonalSettingsForm _settingsForm;
    private readonly ToolStripItem _statusMenuItem;
    private readonly string _cliPath;

    private ProxySettings _proxy;
    private PingIcon? _currentIcon;
    private bool _pingInProgress;
    private string _sessionStatusText = "Not started";
    private string _pingStatusText = "checking proxy...";
    private SessionRuleTracker? _tracker;
    private bool _shutdownDone;

    public PersonalAppContext()
    {
        _logger = new SessionLogger(AppPaths.NewLogFilePath());
        _proxyBridgeLogPath = AppPaths.NewProxyBridgeLogFilePath();
        _cliPath = PersonalProxyConfig.CliPathOverride ?? EmbeddedProxyBridge.ExtractIfNeeded();
        _proxy = LoadEffectiveProxy();

        _settingsForm = new PersonalSettingsForm();
        _settingsForm.LaunchRequested += OnLaunchRequested;
        _settingsForm.FormClosed += OnSettingsFormClosed;

        var menu = new ContextMenuStrip();
        _statusMenuItem = menu.Items.Add(_sessionStatusText);
        _statusMenuItem.Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        var showSettings = menu.Items.Add("Show Settings");
        showSettings.Click += (_, _) => ShowSettings();
        var advanced = menu.Items.Add("Advanced Settings...");
        advanced.Click += (_, _) => ShowAdvancedSettings();
        var openLog = menu.Items.Add("Open Log File");
        openLog.Click += (_, _) => OpenLogFile();
        var openBridgeLog = menu.Items.Add("Open ProxyBridge Log");
        openBridgeLog.Click += (_, _) => OpenProxyBridgeLogFile();
        var stop = menu.Items.Add("Stop Proxion");
        stop.Click += (_, _) => Shutdown("Stopped from the tray icon (Stop Proxion).");

        _currentIcon = PingIcon.Cat();
        _trayIcon = new NotifyIcon
        {
            Icon = _currentIcon.Icon,
            Text = TrimTrayText($"Proxion - {_sessionStatusText}"),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => ShowSettings();

        _timer = new System.Windows.Forms.Timer { Interval = PollIntervalMs };
        _timer.Tick += Timer_Tick;

        _pingTimer = new System.Windows.Forms.Timer { Interval = PingIntervalMs };
        _pingTimer.Tick += async (_, _) => await RefreshPingIconAsync();
        _pingTimer.Start();
        _ = RefreshPingIconAsync();

        var stored = SettingsStore.Load();
        if (stored.AutoRunPurple && !string.IsNullOrWhiteSpace(stored.PurpleLauncherPath) && File.Exists(stored.PurpleLauncherPath))
        {
            _logger.Info("Auto-run is enabled; launching PURPLE without showing the settings window.");
            OnLaunchRequested(this, stored.PurpleLauncherPath);
        }
        else
        {
            _settingsForm.Show();
        }
    }

    private static ProxySettings LoadEffectiveProxy()
    {
        var stored = SettingsStore.Load();
        return !string.IsNullOrWhiteSpace(stored.ProxyHost) ? stored.ToProxySettings() : PersonalProxyConfig.Proxy;
    }

    private string ProxyLabel() => $"{_proxy.TypeLabel.ToUpperInvariant()} {_proxy.Host}:{_proxy.Port}";

    private void ShowSettings()
    {
        _settingsForm.Show();
        _settingsForm.WindowState = FormWindowState.Normal;
        _settingsForm.Activate();
    }

    private void ShowAdvancedSettings()
    {
        using var dialog = new AdvancedProxySettingsForm(_proxy);
        if (dialog.ShowDialog(_settingsForm) != DialogResult.OK || dialog.Result is not { } newProxy)
        {
            return;
        }

        _proxy = newProxy;
        SaveProxyOverride(newProxy);
        _logger.Info($"Advanced proxy settings updated: {ProxyLabel()}");

        if (_tracker is not null)
        {
            WriteProfile(_tracker.TrackedNames);
            _bridge.Restart(_cliPath, AppPaths.ProfilePath, ProxyBridgeVerbosity, _proxyBridgeLogPath);
            UpdateSessionStatus($"Routing via {ProxyLabel()}");
        }

        _ = RefreshPingIconAsync();
    }

    private static void SaveProxyOverride(ProxySettings proxy)
    {
        var stored = SettingsStore.Load();
        stored.ProxyType = proxy.TypeLabel;
        stored.ProxyHost = proxy.Host;
        stored.ProxyPort = proxy.Port;
        stored.ProxyUsername = proxy.Username;
        stored.RuleProtocol = proxy.ProtocolLabel;
        stored.ProtectedPassword = SettingsStore.ProtectPassword(proxy.Password);
        SettingsStore.Save(stored);
    }

    private void OnSettingsFormClosed(object? sender, EventArgs e)
    {
        // Only reached if the window was allowed to actually close (SessionActive was
        // false) - i.e. the user closed it before ever launching PURPLE. There's
        // nothing left running in that case, so the whole app should exit too.
        Shutdown("Settings window closed before PURPLE was launched.");
    }

    private void OnLaunchRequested(object? sender, string purplePath)
    {
        if (_tracker is not null)
        {
            // Already routing this session - nothing more to do, just get out of the way.
            _settingsForm.Hide();
            return;
        }

        if (string.IsNullOrWhiteSpace(purplePath) || !File.Exists(purplePath))
        {
            MessageBox.Show(_settingsForm, "Select a valid PurpleLauncher.exe first.",
                "Proxion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settingsForm.SaveCurrentSettings();

        var purpleName = Path.GetFileName(purplePath);
        WriteProfile(new[] { purpleName });

        _logger.Info("Starting ProxyBridge CLI (traffic routing begins now)...");
        try
        {
            _bridge.Start(_cliPath, AppPaths.ProfilePath, ProxyBridgeVerbosity, _proxyBridgeLogPath);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to start ProxyBridge CLI: {ex.Message}");
            MessageBox.Show(_settingsForm, $"Failed to start ProxyBridge CLI:{Environment.NewLine}{ex.Message}",
                "Proxion", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        System.Threading.Thread.Sleep(1500);
        if (_bridge.HasExited)
        {
            _logger.Warn($"ProxyBridge CLI exited immediately (exit code {_bridge.ExitCode}).");
            MessageBox.Show(_settingsForm,
                "ProxyBridge CLI exited immediately. Make sure Proxion is running as Administrator.",
                "Proxion", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _logger.Info($"Launching NCSOFT PURPLE: {purplePath}");
        Process? purpleProcess;
        try
        {
            purpleProcess = Process.Start(new ProcessStartInfo(purplePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to launch PURPLE: {ex.Message}");
            MessageBox.Show(_settingsForm, $"Failed to launch PURPLE:{Environment.NewLine}{ex.Message}",
                "Proxion", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _bridge.Stop();
            return;
        }

        var purplePid = purpleProcess?.Id ?? -1;
        _tracker = new SessionRuleTracker(purplePid, purpleName);
        _settingsForm.SessionActive = true;

        UpdateSessionStatus($"Routing via {ProxyLabel()}");
        _logger.Info("Proxion is now running in the background. Use the tray icon to stop it or adjust settings.");
        _trayIcon.ShowBalloonTip(4000, "Proxion",
            $"Routing PURPLE traffic through {ProxyLabel()}. Right-click the tray icon to stop or adjust settings.",
            ToolTipIcon.Info);

        _settingsForm.Hide();
        _timer.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_tracker is null)
        {
            return;
        }

        IReadOnlyList<ProcessSnapshot> snapshot;
        try
        {
            snapshot = WmiProcessSnapshotProvider.GetSnapshot();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to read the process list: {ex.Message}");
            return;
        }

        var result = _tracker.Poll(snapshot, out var newNames);
        switch (result)
        {
            case SessionPollResult.NewProcessesDetected:
                _logger.Info($"PURPLE launched a new process: {string.Join(", ", newNames)}. Updating the proxy rule.");
                WriteProfile(_tracker.TrackedNames);
                _bridge.Restart(_cliPath, AppPaths.ProfilePath, ProxyBridgeVerbosity, _proxyBridgeLogPath);
                _trayIcon.ShowBalloonTip(3000, "Proxion", $"Now also routing: {string.Join(", ", newNames)}", ToolTipIcon.Info);
                break;

            case SessionPollResult.SessionEnded:
                Shutdown("PURPLE and all its launched processes have closed.");
                break;

            case SessionPollResult.RootNotYetSeen:
            case SessionPollResult.Unchanged:
            default:
                break;
        }
    }

    private async Task RefreshPingIconAsync()
    {
        if (_pingInProgress || _shutdownDone)
        {
            return;
        }
        _pingInProgress = true;
        try
        {
            PingIcon nextIcon;
            try
            {
                var reply = await _ping.SendPingAsync(_proxy.Host, PingTimeoutMs);
                if (reply.Status == IPStatus.Success)
                {
                    nextIcon = PingIcon.FromPingMilliseconds(reply.RoundtripTime);
                    _pingStatusText = $"{reply.RoundtripTime} ms to {_proxy.Host}";
                }
                else
                {
                    nextIcon = PingIcon.Cat();
                    _pingStatusText = $"proxy unreachable ({reply.Status})";
                }
            }
            catch (Exception)
            {
                nextIcon = PingIcon.Cat();
                _pingStatusText = "ping unavailable";
            }

            var previousIcon = _currentIcon;
            _trayIcon.Icon = nextIcon.Icon;
            _currentIcon = nextIcon;
            previousIcon?.Dispose();

            RefreshTrayTooltip();
        }
        finally
        {
            _pingInProgress = false;
        }
    }

    private void UpdateSessionStatus(string text)
    {
        _sessionStatusText = text;
        _statusMenuItem.Text = text;
        _settingsForm.SetStatus(text);
        RefreshTrayTooltip();
    }

    private void RefreshTrayTooltip() =>
        _trayIcon.Text = TrimTrayText($"Proxion - {_sessionStatusText} ({_pingStatusText})");

    private void WriteProfile(IEnumerable<string> processNames)
    {
        var names = processNames.Concat(PurpleEcosystem.AlwaysRoutedProcessNames);
        var json = PbProfileBuilder.Build(_proxy, names);
        File.WriteAllText(AppPaths.ProfilePath, json);
    }

    private void OpenLogFile()
    {
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_logger.LogFilePath}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Not critical if this fails.
        }
    }

    private void OpenProxyBridgeLogFile()
    {
        try
        {
            if (!File.Exists(_proxyBridgeLogPath))
            {
                MessageBox.Show(_settingsForm, "ProxyBridge hasn't logged anything yet - it's only created once it actually starts.",
                    "Proxion", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_proxyBridgeLogPath}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Not critical if this fails.
        }
    }

    private void Shutdown(string reason)
    {
        if (_shutdownDone)
        {
            return;
        }
        _shutdownDone = true;

        _timer.Stop();
        _pingTimer.Stop();
        _ping.Dispose();
        _logger.Info(reason);

        if (_tracker is not null)
        {
            _logger.Info("Stopping ProxyBridge CLI and restoring direct traffic...");
            _bridge.Stop();
            try
            {
                File.Delete(AppPaths.ProfilePath);
            }
            catch (IOException)
            {
            }
        }

        _settingsForm.SessionActive = false;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _currentIcon?.Dispose();
        _settingsForm.Dispose();
        _logger.Info("Proxion session ended.");
        ExitThread();
    }

    private static string TrimTrayText(string text) =>
        text.Length <= 63 ? text : text[..60] + "...";
}
