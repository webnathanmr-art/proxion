#Requires -Version 5.1
<#
.SYNOPSIS
    Routes NCSOFT PURPLE launcher (and its games) through a proxy via ProxyBridge,
    for the lifetime of the launcher/game session.
.PARAMETER ConfigPath
    Path to a proxion.config.json (see config/proxion.config.example.json).
.PARAMETER DryRun
    Resolve paths and generate the ProxyBridge profile, but don't launch anything.
.PARAMETER KeepProfile
    Don't delete the generated .pbprofile file when the session ends.
#>
[CmdletBinding()]
param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot '..\config\proxion.config.json'),
    [switch]$DryRun,
    [switch]$KeepProfile
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Proxion.psm1') -Force

if (-not $IsWindows) {
    throw 'Proxion only runs on Windows: ProxyBridge relies on the WinDivert kernel driver, and NCSOFT PURPLE is a Windows-only launcher.'
}

$logDir = Join-Path $PSScriptRoot '..\logs'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$logFile = Join-Path $logDir ('proxion-{0}.log' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))

Write-ProxionLog -Message "Loading config from $ConfigPath" -LogFile $logFile
$config = Get-ProxionConfig -Path $ConfigPath

Write-ProxionLog -Message 'Resolving PurpleLauncher.exe...' -LogFile $logFile
$purplePath = Resolve-PurpleLauncherPath -ConfiguredPath $config.purpleLauncherPath
Write-ProxionLog -Message "Found PURPLE launcher: $purplePath" -LogFile $logFile

Write-ProxionLog -Message 'Resolving ProxyBridge_CLI.exe...' -LogFile $logFile
$cliPath = Resolve-ProxyBridgeCliPath -ConfiguredPath $config.proxyBridgeCliPath
Write-ProxionLog -Message "Found ProxyBridge CLI: $cliPath" -LogFile $logFile

if (-not $DryRun -and -not (Test-ProxionAdmin)) {
    throw 'Proxion must run elevated (as Administrator) because ProxyBridge loads the WinDivert kernel driver. Re-run this script from an elevated PowerShell prompt (or right-click PowerShell > Run as Administrator).'
}

$runtimeDir = Join-Path $PSScriptRoot '..\.runtime'
New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
$profilePath = Join-Path $runtimeDir 'proxion.pbprofile'
New-ProxionProfile -Config $config -PurpleLauncherPath $purplePath -OutFile $profilePath | Out-Null
Write-ProxionLog -Message "Generated ProxyBridge profile: $profilePath" -LogFile $logFile

$monitoredNames = @([System.IO.Path]::GetFileName($purplePath)) + @($config.gameProcessNames)

if ($DryRun) {
    Write-ProxionLog -Message 'Dry run requested; nothing was launched. Plan:' -LogFile $logFile
    Write-ProxionLog -Message "  ProxyBridge CLI : `"$cliPath`" --profile `"$profilePath`" --verbose $($config.verbose)" -LogFile $logFile
    Write-ProxionLog -Message "  PURPLE launcher : $purplePath" -LogFile $logFile
    Write-ProxionLog -Message "  Monitored procs : $($monitoredNames -join ', ')" -LogFile $logFile
    Write-ProxionLog -Message 'Generated profile contents:' -LogFile $logFile
    Get-Content -LiteralPath $profilePath | ForEach-Object { Write-Host $_ }
    return
}

# Everything below needs WinForms (for the tray icon) and an STA thread. It's
# wrapped in a function - rather than run inline - so that a -DryRun never
# pulls in System.Drawing/System.Windows.Forms at all.
function Start-ProxionSession {
    # The tray icon (System.Windows.Forms.NotifyIcon) needs an STA thread. Relaunch
    # under one if we're not already running in it, rather than asking the user to
    # remember a -STA flag.
    if (-not (Test-ProxionSTA)) {
        Write-ProxionLog -Message 'Relaunching in STA mode (required for the tray icon)...' -LogFile $logFile
        $psExe = (Get-Process -Id $PID).Path
        $relaunchArgs = @('-NoProfile', '-STA', '-File', $PSCommandPath, '-ConfigPath', $ConfigPath)
        if ($KeepProfile) { $relaunchArgs += '-KeepProfile' }
        $relaunched = Start-Process -FilePath $psExe -ArgumentList $relaunchArgs -PassThru -Wait
        exit $relaunched.ExitCode
    }

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing

    $bridgeProc = $null
    $trayIcon = $null
    $timer = $null
    $script:shutdownDone = $false

    function Invoke-ProxionShutdown {
        if ($script:shutdownDone) { return }
        $script:shutdownDone = $true

        Write-ProxionLog -Message 'Stopping ProxyBridge CLI and restoring direct traffic...' -LogFile $logFile
        Stop-ProxyBridgeCli -Process $bridgeProc
        if (-not $KeepProfile) {
            Remove-Item -LiteralPath $profilePath -ErrorAction SilentlyContinue
        }
        if ($timer) { $timer.Stop(); $timer.Dispose() }
        if ($trayIcon) { $trayIcon.Visible = $false; $trayIcon.Dispose() }
        Set-ProxionConsoleVisible -Visible $true
        Write-ProxionLog -Message 'Proxion session ended.' -LogFile $logFile
        [System.Windows.Forms.Application]::Exit()
    }

    try {
        Write-ProxionLog -Message 'Starting ProxyBridge CLI (traffic routing begins now)...' -LogFile $logFile
        $bridgeProc = Start-ProxyBridgeCli -CliPath $cliPath -ProfilePath $profilePath -Verbosity $config.verbose
        Start-Sleep -Seconds 2
        if ($bridgeProc.HasExited) {
            throw "ProxyBridge CLI exited immediately (exit code $($bridgeProc.ExitCode)). Check that this shell is elevated and the profile is valid: $profilePath"
        }

        Write-ProxionLog -Message 'Launching NCSOFT PURPLE...' -LogFile $logFile
        Start-Process -FilePath $purplePath | Out-Null

        $proxyLabel = "$($config.proxy.type.ToUpper()) $($config.proxy.host):$($config.proxy.port)"

        $icon = $null
        try { $icon = [System.Drawing.Icon]::ExtractAssociatedIcon($purplePath) } catch { $icon = $null }
        if (-not $icon) { $icon = [System.Drawing.SystemIcons]::Application }

        $menu = New-Object System.Windows.Forms.ContextMenuStrip
        $statusItem = $menu.Items.Add("Routing via $proxyLabel")
        $statusItem.Enabled = $false
        $menu.Items.Add('-') | Out-Null
        $openLogItem = $menu.Items.Add('Open Log File')
        $exitItem = $menu.Items.Add('Stop Proxion')

        $trayIcon = New-Object System.Windows.Forms.NotifyIcon
        $trayIcon.Icon = $icon
        $trayIcon.Text = "Proxion - routing PURPLE via $proxyLabel"
        $trayIcon.ContextMenuStrip = $menu
        $trayIcon.Visible = $true
        $trayIcon.ShowBalloonTip(4000, 'Proxion', "Routing PURPLE traffic through $proxyLabel. Right-click the tray icon to stop.", [System.Windows.Forms.ToolTipIcon]::Info)

        $openLogItem.Add_Click({ Start-Process -FilePath 'notepad.exe' -ArgumentList $logFile }.GetNewClosure())
        $exitItem.Add_Click({ Invoke-ProxionShutdown })
        $trayIcon.Add_DoubleClick({ Invoke-ProxionShutdown })

        Write-ProxionLog -Message 'Proxion is now running in the background. Use the tray icon to stop it (right-click > Stop Proxion, or double-click).' -LogFile $logFile
        Set-ProxionConsoleVisible -Visible $false

        $script:sawMonitoredProcess = $false
        $script:startupDeadline = (Get-Date).AddSeconds($config.startupTimeoutSeconds)

        $timer = New-Object System.Windows.Forms.Timer
        $timer.Interval = [Math]::Max(1000, [int]$config.pollIntervalSeconds * 1000)
        $timer.Add_Tick({
            if (-not $script:sawMonitoredProcess) {
                if (Test-AnyProcessRunning -Names $monitoredNames) {
                    $script:sawMonitoredProcess = $true
                    Write-ProxionLog -Message 'PURPLE is running. Proxion will keep routing traffic until PURPLE and every configured game process have closed.' -LogFile $logFile
                } elseif ((Get-Date) -gt $script:startupDeadline) {
                    Write-ProxionLog -Message 'Timed out waiting for PURPLE to start; stopping ProxyBridge.' -Level 'WARN' -LogFile $logFile
                    Invoke-ProxionShutdown
                }
            } elseif (-not (Test-AnyProcessRunning -Names $monitoredNames)) {
                Write-ProxionLog -Message 'PURPLE and all monitored game processes have closed.' -LogFile $logFile
                Invoke-ProxionShutdown
            }
        })
        $timer.Start()

        [System.Windows.Forms.Application]::Run()
    }
    finally {
        # Safety net: if we got here without Invoke-ProxionShutdown having run
        # (e.g. an unhandled exception before Application.Run), clean up anyway.
        Invoke-ProxionShutdown
    }
}

Start-ProxionSession
