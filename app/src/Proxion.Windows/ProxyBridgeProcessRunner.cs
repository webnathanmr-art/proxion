using System.Diagnostics;

namespace Proxion.Windows;

/// <summary>Starts, stops, and restarts ProxyBridge_CLI.exe against a given .pbprofile.</summary>
public sealed class ProxyBridgeProcessRunner
{
    private readonly object _logLock = new();
    private Process? _process;
    private StreamWriter? _logWriter;

    public bool HasExited
    {
        get
        {
            if (_process is null)
            {
                return true;
            }
            try
            {
                _process.Refresh();
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public int? ExitCode => _process is { HasExited: true } ? _process.ExitCode : null;

    /// <summary>
    /// Starts ProxyBridge CLI. If <paramref name="logFilePath"/> is given, ProxyBridge's own
    /// console output (its connection-level logs at --verbose 3, not just Proxion's own
    /// higher-level log) is captured into that file instead of a visible console window -
    /// the only way to see what ProxyBridge itself actually did with a connection (attempted,
    /// succeeded, reset, etc.) without alt-tabbing to a minimized console for a background app.
    /// </summary>
    public void Start(string cliPath, string profilePath, int verbosity, string? logFilePath = null)
    {
        var psi = new ProcessStartInfo { FileName = cliPath };
        psi.ArgumentList.Add("--profile");
        psi.ArgumentList.Add(profilePath);
        psi.ArgumentList.Add("--verbose");
        psi.ArgumentList.Add(verbosity.ToString());

        StreamWriter? logWriter = null;
        if (logFilePath is not null)
        {
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            var stream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            logWriter = new StreamWriter(stream) { AutoFlush = true };
        }
        else
        {
            psi.UseShellExecute = true;
            psi.WindowStyle = ProcessWindowStyle.Minimized;
        }

        _logWriter?.Dispose();
        _logWriter = logWriter;

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ProxyBridge CLI.");

        if (logFilePath is not null)
        {
            _process.OutputDataReceived += (_, e) => WriteLogLine(e.Data);
            _process.ErrorDataReceived += (_, e) => WriteLogLine(e.Data is null ? null : $"[stderr] {e.Data}");
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
    }

    private void WriteLogLine(string? line)
    {
        if (line is null)
        {
            return;
        }
        lock (_logLock)
        {
            try
            {
                _logWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}");
            }
            catch (ObjectDisposedException)
            {
                // The writer was disposed (e.g. Stop() ran) between the null-check and here; ignore.
            }
        }
    }

    /// <summary>Stops the running ProxyBridge CLI process, gracefully if possible.</summary>
    public void Stop()
    {
        if (_process is null || HasExited)
        {
            DisposeLogWriter();
            return;
        }

        try
        {
            // taskkill without /F asks the process to close (CTRL_CLOSE-style), which
            // ProxyBridge's CLI handles the same way it handles Ctrl+C - cleanly tearing
            // down its WinDivert handles. Process.Kill() alone would just terminate it,
            // which risks leaving the driver in a half-configured state.
            using var taskkill = Process.Start(new ProcessStartInfo("taskkill.exe")
            {
                ArgumentList = { "/PID", _process.Id.ToString() },
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            taskkill?.WaitForExit(5000);
            _process.WaitForExit(5000);
        }
        catch (Exception)
        {
            // Fall through to the force-kill below.
        }

        if (!HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // Best-effort; nothing more we can do.
            }
        }

        DisposeLogWriter();
    }

    private void DisposeLogWriter()
    {
        lock (_logLock)
        {
            _logWriter?.Dispose();
            _logWriter = null;
        }
    }

    /// <summary>Stops the current instance (if any) and starts a new one against the given profile.</summary>
    public void Restart(string cliPath, string profilePath, int verbosity, string? logFilePath = null)
    {
        Stop();
        Start(cliPath, profilePath, verbosity, logFilePath);
    }
}
