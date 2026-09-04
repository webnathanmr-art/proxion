using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Proxion.Core;
using Proxion.Windows;

namespace Proxion.Personal.UI;

/// <summary>
/// Drives the personal-fork session: starts ProxyBridge against the hardcoded proxy,
/// then waits for the user to open PURPLE themselves (rather than launching it), moving
/// to the tray immediately. Once PURPLE is detected running, behaves exactly like the
/// general app's session - the proxy rule grows to cover exactly what PURPLE actually
/// launches, and the session ends (stopping ProxyBridge) when PURPLE and everything it
/// launched have closed, or the user stops it from the tray.
/// </summary>
public sealed class PersonalTrayContext : ApplicationContext
{
    private const string PurpleProcessName = "PurpleLauncher.exe";
    private const int PollIntervalMs = 3000;
    private const int WaitForPurpleTimeoutSeconds = 1800; // 30 minutes to manually open PURPLE
    private const int ProxyBridgeVerbosity = 1;

    private readonly ProxySettings _proxy = PersonalProxyConfig.Proxy;
    private readonly SessionLogger _logger;
    private readonly ProxyBridgeProcessRunner _bridge = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly NotifyIcon _trayIcon;
    private readonly string _cliPath;

    private SessionRuleTracker? _tracker;
    private DateTime _waitDeadlineUtc;
    private bool _shutdownDone;

    public PersonalTrayContext()
    {
        _logger = new SessionLogger(AppPaths.NewLogFilePath());
        _cliPath = PersonalProxyConfig.CliPathOverride ?? EmbeddedProxyBridge.ExtractIfNeeded();

        _trayIcon = new NotifyIcon
        {
            Icon = IconLoader.Load(),
            Text = TrimTrayText($"Proxion - waiting for PURPLE ({ProxyLabel()})"),
            ContextMenuStrip = BuildMenu(),
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => Shutdown("Stopped from the tray icon (double-click).");

        _timer = new System.Windows.Forms.Timer { Interval = PollIntervalMs };
        _timer.Tick += Timer_Tick;

        StartWaiting();
    }

    private string ProxyLabel() => $"{_proxy.TypeLabel.ToUpperInvariant()} {_proxy.Host}:{_proxy.Port}";

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var status = menu.Items.Add($"Routing via {ProxyLabel()}");
        status.Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        var openLog = menu.Items.Add("Open Log File");
        openLog.Click += (_, _) => OpenLogFile();
        var stop = menu.Items.Add("Stop Proxion");
        stop.Click += (_, _) => Shutdown("Stopped from the tray icon (Stop Proxion).");
        return menu;
    }

    private void StartWaiting()
    {
        WriteProfile(new[] { PurpleProcessName });
        _logger.Info("Starting ProxyBridge CLI...");
        try
        {
            _bridge.Start(_cliPath, AppPaths.ProfilePath, ProxyBridgeVerbosity);
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

        System.Threading.Thread.Sleep(1500);
        if (_bridge.HasExited)
        {
            _logger.Warn($"ProxyBridge CLI exited immediately (exit code {_bridge.ExitCode}).");
            MessageBox.Show(
                "ProxyBridge CLI exited immediately. Make sure Proxion is running as Administrator.",
                "Proxion", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _trayIcon.Visible = false;
            ExitThread();
            return;
        }

        _waitDeadlineUtc = DateTime.UtcNow.AddSeconds(WaitForPurpleTimeoutSeconds);
        _logger.Info($"Waiting for {PurpleProcessName} to start (open it manually now)...");
        _trayIcon.ShowBalloonTip(4000, "Proxion",
            $"Waiting for PURPLE. Open it now - traffic will route through {ProxyLabel()} once detected.",
            ToolTipIcon.Info);

        _timer.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
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

        if (_tracker is null)
        {
            var purple = snapshot.FirstOrDefault(p => string.Equals(p.Name, PurpleProcessName, StringComparison.OrdinalIgnoreCase));
            if (purple.Pid != 0)
            {
                _logger.Info($"Detected PURPLE running (pid {purple.Pid}). Now routing its traffic.");
                _trayIcon.Text = TrimTrayText($"Proxion - routing via {ProxyLabel()}");
                _trayIcon.ShowBalloonTip(3000, "Proxion", $"PURPLE detected - now routing via {ProxyLabel()}.", ToolTipIcon.Info);
                _tracker = new SessionRuleTracker(purple.Pid, PurpleProcessName);
                HandlePoll(_tracker.Poll(snapshot, out var initialNames), initialNames);
                return;
            }

            if (DateTime.UtcNow > _waitDeadlineUtc)
            {
                Shutdown("Timed out waiting for PURPLE to start.");
            }
            return;
        }

        HandlePoll(_tracker.Poll(snapshot, out var newNames), newNames);
    }

    private void HandlePoll(SessionPollResult result, IReadOnlyList<string> newNames)
    {
        switch (result)
        {
            case SessionPollResult.NewProcessesDetected:
                _logger.Info($"PURPLE launched a new process: {string.Join(", ", newNames)}. Updating the proxy rule.");
                WriteProfile(_tracker!.TrackedNames);
                _bridge.Restart(_cliPath, AppPaths.ProfilePath, ProxyBridgeVerbosity);
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

    private void WriteProfile(IEnumerable<string> processNames)
    {
        var json = PbProfileBuilder.Build(_proxy, processNames);
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

    private static string TrimTrayText(string text) =>
        text.Length <= 63 ? text : text[..60] + "...";
}
