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

$bridgeProc = $null
try {
    Write-ProxionLog -Message 'Starting ProxyBridge CLI (traffic routing begins now)...' -LogFile $logFile
    $bridgeProc = Start-ProxyBridgeCli -CliPath $cliPath -ProfilePath $profilePath -Verbosity $config.verbose
    Start-Sleep -Seconds 2
    if ($bridgeProc.HasExited) {
        throw "ProxyBridge CLI exited immediately (exit code $($bridgeProc.ExitCode)). Check that this shell is elevated and the profile is valid: $profilePath"
    }

    Write-ProxionLog -Message 'Launching NCSOFT PURPLE...' -LogFile $logFile
    Start-Process -FilePath $purplePath | Out-Null

    Write-ProxionLog -Message 'Waiting for PURPLE (or a configured game) to appear...' -LogFile $logFile
    $deadline = (Get-Date).AddSeconds($config.startupTimeoutSeconds)
    while (-not (Test-AnyProcessRunning -Names $monitoredNames) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
    }

    if (-not (Test-AnyProcessRunning -Names $monitoredNames)) {
        Write-ProxionLog -Message 'Timed out waiting for PURPLE to start; stopping ProxyBridge.' -Level 'WARN' -LogFile $logFile
    } else {
        Write-ProxionLog -Message 'PURPLE is running. Proxion will stay active in the background and keep routing traffic until PURPLE and every configured game process have closed.' -LogFile $logFile
        while (Test-AnyProcessRunning -Names $monitoredNames) {
            Start-Sleep -Seconds $config.pollIntervalSeconds
        }
        Write-ProxionLog -Message 'PURPLE and all monitored game processes have closed.' -LogFile $logFile
    }
}
finally {
    Write-ProxionLog -Message 'Stopping ProxyBridge CLI and restoring direct traffic...' -LogFile $logFile
    Stop-ProxyBridgeCli -Process $bridgeProc
    if (-not $KeepProfile) {
        Remove-Item -LiteralPath $profilePath -ErrorAction SilentlyContinue
    }
    Write-ProxionLog -Message 'Proxion session ended.' -LogFile $logFile
}
