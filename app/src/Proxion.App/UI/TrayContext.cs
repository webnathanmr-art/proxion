using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Proxion.Core;
using Proxion.Windows;

namespace Proxion.App.UI;

/// <summary>
/// Drives one Proxion session end to end: starts ProxyBridge, launches PURPLE, shows
/// the tray icon, and polls the process tree so the proxy rule grows to cover exactly
/// the processes PURPLE actually launches - nothing more. Ends the session (stopping
/// ProxyBridge and restoring direct traffic) either when the user asks via the tray
/// icon, or once PURPLE and everything it launched have closed.
/// </summary>
public sealed class TrayContext : ApplicationContext
{
    private const int StartupTimeoutSeconds = 30;
    private const int PollIntervalMs = 3000;
    private const int ProxyBridgeVerbosity = 3; // both logs and connection events - see _proxyBridgeLogPath

    private readonly SessionSetupResult _setup;
    private readonly SessionLogger _logger;
    private readonly string _proxyBridgeLogPath;
    private readonly ProxyBridgeProcessRunner _bridge = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly NotifyIcon _trayIcon;
    private readonly string _cliPath;

    private SessionRuleTracker? _tracker;
    private DateTime _startupDeadlineUtc;
    private bool _shutdownDone;

    public TrayContext(SessionSetupResult setup)
    {
        _setup = setup;
        _logger = new SessionLogger(AppPaths.NewLogFilePath());
        _proxyBridgeLogPath = AppPaths.NewProxyBridgeLogFilePath();
        _cliPath = EmbeddedProxyBridge.ExtractIfNeeded();

        _trayIcon = new NotifyIcon
        {
            Icon = LoadIcon(setup.PurplePath),
            Text = TrimTrayText($"Proxion - routing via {ProxyLabel()}"),
            ContextMenuStrip = BuildMenu(),
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => Shutdown("Stopped from the tray icon (double-click).");

        _timer = new System.Windows.Forms.Timer { Interval = PollIntervalMs };
        _timer.Tick += Timer_Tick;

        StartSession();
    }

    private string ProxyLabel() => $"{_setup.Proxy.TypeLabel.ToUpperInvariant()} {_setup.Proxy.Host}:{_setup.Proxy.Port}";

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var status = menu.Items.Add($"Routing via {ProxyLabel()}");
        status.Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        var openLog = menu.Items.Add("Open Log File");
        openLog.Click += (_, _) => OpenLogFile();
        var openBridgeLog = menu.Items.Add("Open ProxyBridge Log");
        openBridgeLog.Click += (_, _) => OpenProxyBridgeLogFile();
        var stop = menu.Items.Add("Stop Proxion");
        stop.Click += (_, _) => Shutdown("Stopped from the tray icon (Stop Proxion).");
        return menu;
    }

    private void StartSession()
    {
        var purpleName = Path.GetFileName(_setup.PurplePath);

        WriteProfile(new[] { purpleName });
        _logger.Info("Starting ProxyBridge CLI (traffic routing begins now)...");
        try
        {
            _bridge.Start(_cliPath, AppPaths.ProfilePath, ProxyBridgeVerbosity, _proxyBridgeLogPath);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to start ProxyBridge CLI: {ex.Message}");
            MessageBox.Show($"Failed to start ProxyBridge CLI:{Environment.NewLine}{ex.Message}",
                "Proxion", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _trayIcon.Visible = false;
            ExitThread();
            return;
        }

        // Give ProxyBridge a moment to come up (or fail) before launching PURPLE.
        Thread.Sleep(1500);
        if (_bridge.HasExited)
        {
            _logger.Warn($"ProxyBridge CLI exited immediately (exit code {_bridge.ExitCode}).");
            MessageBox.Show(
                "ProxyBridge CLI exited immediately. Check that ProxyBridge is installed correctly "
                + "and that Proxion is running as Administrator.",
                "Proxion", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _trayIcon.Visible = false;
            ExitThread();
            return;
        }

        _logger.Info($"Launching NCSOFT PURPLE: {_setup.PurplePath}");
        Process? purpleProcess;
        try
        {
            purpleProcess = Process.Start(new ProcessStartInfo(_setup.PurplePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to launch PURPLE: {ex.Message}");
            MessageBox.Show($"Failed to launch PURPLE:{Environment.NewLine}{ex.Message}",
                "Proxion", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _bridge.Stop();
            _trayIcon.Visible = false;
            ExitThread();
            return;
        }

        var purplePid = purpleProcess?.Id ?? -1;
        _tracker = new SessionRuleTracker(purplePid, purpleName);
        _startupDeadlineUtc = DateTime.UtcNow.AddSeconds(StartupTimeoutSeconds);

        _logger.Info("Proxion is now running in the background. Use the tray icon to stop it.");
        _trayIcon.ShowBalloonTip(4000, "Proxion",
            $"Routing PURPLE traffic through {ProxyLabel()}. Right-click the tray icon to stop.",
            ToolTipIcon.Info);

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

            case SessionPollResult.RootNotYetSeen:
                if (DateTime.UtcNow > _startupDeadlineUtc)
                {
                    Shutdown("Timed out waiting for PURPLE to start.");
                }
                break;

            case SessionPollResult.SessionEnded:
                Shutdown("PURPLE and all its launched processes have closed.");
                break;

            case SessionPollResult.Unchanged:
            default:
                break;
        }
    }

    private void WriteProfile(IEnumerable<string> processNames)
    {
        var json = PbProfileBuilder.Build(_setup.Proxy, processNames, localhostViaProxy: _setup.LocalhostViaProxy);
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
                MessageBox.Show("ProxyBridge hasn't logged anything yet - it's only created once it actually starts.",
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
        _logger.Info(reason);
        _logger.Info("Stopping ProxyBridge CLI and restoring direct traffic...");
        _bridge.Stop();

        try
        {
            File.Delete(AppPaths.ProfilePath);
        }
        catch (IOException)
        {
        }

        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _logger.Info("Proxion session ended.");
        ExitThread();
    }

    private static Icon LoadIcon(string exePath)
    {
        try
        {
            var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon is not null)
            {
                return icon;
            }
        }
        catch (Exception)
        {
        }
        return SystemIcons.Application;
    }

    private static string TrimTrayText(string text) =>
        text.Length <= 63 ? text : text[..60] + "...";
}
