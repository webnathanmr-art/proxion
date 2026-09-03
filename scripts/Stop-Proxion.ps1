#Requires -Version 5.1
<#
.SYNOPSIS
    Force-stops any running ProxyBridge_CLI instance started by Proxion,
    e.g. after a crash left traffic routed through the proxy.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Proxion.psm1') -Force

if (-not $IsWindows) {
    throw 'Proxion only runs on Windows.'
}

$procs = Get-Process -Name 'ProxyBridge_CLI' -ErrorAction SilentlyContinue
if (-not $procs) {
    Write-Host 'No running ProxyBridge_CLI process found. Nothing to do.'
    return
}

foreach ($p in $procs) {
    Write-Host "Stopping ProxyBridge_CLI (PID $($p.Id))..."
    Stop-ProxyBridgeCli -Process $p
}

Write-Host 'Done. Traffic should now be routed directly again.'
