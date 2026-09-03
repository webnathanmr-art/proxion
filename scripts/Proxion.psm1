Set-StrictMode -Version Latest

function Test-ProxionAdmin {
    [CmdletBinding()]
    param()

    if (-not $IsWindows) { return $false }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-ProxionSTA {
    [CmdletBinding()]
    param()

    if (-not $IsWindows) { return $false }
    return ([System.Threading.Thread]::CurrentThread.GetApartmentState() -eq [System.Threading.ApartmentState]::STA)
}

function Set-ProxionConsoleVisible {
    [CmdletBinding()]
    param([Parameter(Mandatory)][bool]$Visible)

    if (-not $IsWindows) { return }

    if (-not ('Proxion.NativeMethods' -as [type])) {
        Add-Type -Namespace Proxion -Name NativeMethods -MemberDefinition @'
            [DllImport("kernel32.dll")]
            public static extern IntPtr GetConsoleWindow();
            [DllImport("user32.dll")]
            public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
'@
    }

    $handle = [Proxion.NativeMethods]::GetConsoleWindow()
    if ($handle -eq [IntPtr]::Zero) { return }

    # SW_HIDE = 0, SW_SHOW = 5
    [Proxion.NativeMethods]::ShowWindow($handle, $(if ($Visible) { 5 } else { 0 })) | Out-Null
}

function Write-ProxionLog {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet('INFO', 'WARN', 'ERROR')][string]$Level = 'INFO',
        [string]$LogFile
    )

    $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $line = "[$stamp][$Level] $Message"

    switch ($Level) {
        'WARN' { Write-Warning $Message }
        'ERROR' { Write-Error $Message -ErrorAction Continue }
        default { Write-Host $line }
    }

    if ($LogFile) {
        Add-Content -LiteralPath $LogFile -Value $line -Encoding utf8
    }
}

function Get-ProxionConfig {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Config file not found: $Path`nCopy config/proxion.config.example.json to config/proxion.config.json and fill in your proxy + install paths."
    }

    $raw = Get-Content -LiteralPath $Path -Raw
    $cfg = $raw | ConvertFrom-Json

    if (-not $cfg.PSObject.Properties.Match('proxy').Count) {
        throw "Config is missing required section 'proxy'."
    }
    $proxyProps = $cfg.proxy.PSObject.Properties
    if (-not $proxyProps.Match('host').Count -or -not $cfg.proxy.host) {
        throw "Config proxy.host is required."
    }
    if (-not $proxyProps.Match('port').Count -or -not $cfg.proxy.port) {
        throw "Config proxy.port is required."
    }
    $proxyType = if ($proxyProps.Match('type').Count) { $cfg.proxy.type } else { $null }
    if ($proxyType -notin @('socks5', 'http')) {
        throw "Config proxy.type must be 'socks5' or 'http', got '$proxyType'."
    }

    $defaults = [ordered]@{
        gameProcessNames     = @()
        pollIntervalSeconds  = 3
        startupTimeoutSeconds = 30
        verbose              = 1
        localhostViaProxy    = $false
        trafficLogging       = $true
        proxyBridgeCliPath   = ''
        purpleLauncherPath   = ''
    }
    foreach ($key in $defaults.Keys) {
        $existing = $cfg.PSObject.Properties.Match($key)
        if (-not $existing.Count -or $null -eq $cfg.$key) {
            $cfg | Add-Member -NotePropertyName $key -NotePropertyValue $defaults[$key] -Force
        }
    }

    return $cfg
}

function Resolve-PurpleLauncherPath {
    [CmdletBinding()]
    param([string]$ConfiguredPath)

    if ($ConfiguredPath -and (Test-Path -LiteralPath $ConfiguredPath)) {
        return (Resolve-Path -LiteralPath $ConfiguredPath).Path
    }

    $bases = @()
    foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, $env:ProgramData)) {
        if ($base) { $bases += (Join-Path $base 'NCSOFT') }
    }

    foreach ($ncBase in $bases) {
        if (-not (Test-Path -LiteralPath $ncBase)) { continue }
        $found = Get-ChildItem -LiteralPath $ncBase -Filter 'PurpleLauncher.exe' -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($found) { return $found.FullName }
    }

    throw "Could not find PurpleLauncher.exe under Program Files\NCSOFT. Set 'purpleLauncherPath' explicitly in your config."
}

function Resolve-ProxyBridgeCliPath {
    [CmdletBinding()]
    param([string]$ConfiguredPath)

    if ($ConfiguredPath -and (Test-Path -LiteralPath $ConfiguredPath)) {
        return (Resolve-Path -LiteralPath $ConfiguredPath).Path
    }

    $cmd = Get-Command 'ProxyBridge_CLI.exe' -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    if ($env:ProgramFiles) {
        $default = Join-Path $env:ProgramFiles 'ProxyBridge\ProxyBridge_CLI.exe'
        if (Test-Path -LiteralPath $default) { return $default }
    }

    throw "Could not find ProxyBridge_CLI.exe. Install ProxyBridge (winget install InterceptSuite.ProxyBridge) or set 'proxyBridgeCliPath' explicitly in your config."
}

function New-ProxionProfile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Config,
        [Parameter(Mandatory)][string]$PurpleLauncherPath,
        [Parameter(Mandatory)][string]$OutFile
    )

    $processNames = @([System.IO.Path]::GetFileName($PurpleLauncherPath)) + @($Config.gameProcessNames)
    $processNames = $processNames | Where-Object { $_ } | Select-Object -Unique
    $processRuleValue = [string]::Join('; ', $processNames)

    $profileObj = [ordered]@{
        Version                 = '1.0'
        LocalhostViaProxy       = [bool]$Config.localhostViaProxy
        IsTrafficLoggingEnabled = [bool]$Config.trafficLogging
        ProxyConfigs            = @(
            [ordered]@{
                Id       = 1
                Type     = $Config.proxy.type
                Host     = $Config.proxy.host
                Port     = "$($Config.proxy.port)"
                Username = "$($Config.proxy.username)"
                Password = "$($Config.proxy.password)"
            }
        )
        ProxyRules              = @(
            [ordered]@{
                ProcessName   = $processRuleValue
                TargetHosts   = '*'
                TargetPorts   = '*'
                Protocol      = 'BOTH'
                Action        = 'PROXY'
                IsEnabled     = $true
                ProxyConfigId = 1
            }
        )
    }

    $json = $profileObj | ConvertTo-Json -Depth 6
    Set-Content -LiteralPath $OutFile -Value $json -Encoding utf8
    return $OutFile
}

function Start-ProxyBridgeCli {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$CliPath,
        [Parameter(Mandatory)][string]$ProfilePath,
        [int]$Verbosity = 1
    )

    $cliArgs = @('--profile', $ProfilePath, '--verbose', "$Verbosity")
    return Start-Process -FilePath $CliPath -ArgumentList $cliArgs -PassThru -WindowStyle Minimized
}

function Stop-ProxyBridgeCli {
    [CmdletBinding()]
    param([System.Diagnostics.Process]$Process)

    if (-not $Process) { return }
    if ($Process.HasExited) { return }

    try {
        Start-Process -FilePath 'taskkill.exe' -ArgumentList @('/PID', "$($Process.Id)") -NoNewWindow -Wait -ErrorAction SilentlyContinue | Out-Null
        $Process.WaitForExit(5000) | Out-Null
    } catch {
        # fall through to force kill below
    }

    if (-not $Process.HasExited) {
        try { $Process.Kill() } catch {}
    }
}

function Test-AnyProcessRunning {
    [CmdletBinding()]
    param([string[]]$Names)

    foreach ($name in $Names) {
        if (-not $name) { continue }
        $bare = [System.IO.Path]::GetFileNameWithoutExtension($name)
        if (Get-Process -Name $bare -ErrorAction SilentlyContinue) {
            return $true
        }
    }
    return $false
}

Export-ModuleMember -Function Test-ProxionAdmin, Test-ProxionSTA, Set-ProxionConsoleVisible, `
    Write-ProxionLog, Get-ProxionConfig, Resolve-PurpleLauncherPath, Resolve-ProxyBridgeCliPath, `
    New-ProxionProfile, Start-ProxyBridgeCli, Stop-ProxyBridgeCli, Test-AnyProcessRunning
