using System.Diagnostics;

namespace Proxion.App.Services;

/// <summary>Starts, stops, and restarts ProxyBridge_CLI.exe against a given .pbprofile.</summary>
public sealed class ProxyBridgeProcessRunner
{
    private Process? _process;

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

    public void Start(string cliPath, string profilePath, int verbosity)
    {
        var psi = new ProcessStartInfo
        {
            FileName = cliPath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Minimized,
        };
        psi.ArgumentList.Add("--profile");
        psi.ArgumentList.Add(profilePath);
        psi.ArgumentList.Add("--verbose");
        psi.ArgumentList.Add(verbosity.ToString());

        _process = Process.Start(psi);
    }

    /// <summary>Stops the running ProxyBridge CLI process, gracefully if possible.</summary>
    public void Stop()
    {
        if (_process is null || HasExited)
        {
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
    }

    /// <summary>Stops the current instance (if any) and starts a new one against the given profile.</summary>
    public void Restart(string cliPath, string profilePath, int verbosity)
    {
        Stop();
        Start(cliPath, profilePath, verbosity);
    }
}
