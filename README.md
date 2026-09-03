# Proxion

Routes NCSOFT **PURPLE** launcher (and the games it launches, e.g. Blade & Soul,
Lineage 2, Guild Wars 2, Throne and Liberty) through a proxy of your choosing —
for example an ISP-provided SOCKS5/HTTP proxy — using
[ProxyBridge](https://github.com/InterceptSuite/ProxyBridge)'s CLI as the
underlying traffic-redirection engine.

Proxion is a thin orchestration layer: it generates a ProxyBridge `.pbprofile`
from your config, starts `ProxyBridge_CLI.exe` headlessly, launches PURPLE,
and stays running in the background — routing traffic the whole time — until
PURPLE and every game process you configured have closed. It then stops
ProxyBridge automatically so your traffic goes back to direct.

**Platform: Windows only.** ProxyBridge intercepts traffic via the WinDivert
kernel driver, and PURPLE is a Windows-only launcher, so this tool only runs
on Windows (PowerShell 5.1+ or PowerShell 7+).

## How it works

1. You describe your proxy (host/port/type/credentials) and install paths in
   `config/proxion.config.json`.
2. `Start-Proxion.ps1` resolves `PurpleLauncher.exe` and `ProxyBridge_CLI.exe`
   (auto-detected, or from your config), then generates a `.pbprofile` with a
   `PROXY` rule for `PurpleLauncher.exe` plus any game executables you list.
3. It launches `ProxyBridge_CLI.exe --profile <generated profile>`
   (requires Administrator, since WinDivert needs kernel access), then
   launches PURPLE.
4. It polls in the background while PURPLE or any configured game process is
   running.
5. When they've all closed, it stops the ProxyBridge CLI process and deletes
   the generated profile, restoring normal direct traffic.

## Requirements

- Windows 10+, PowerShell 5.1+ (built in) or PowerShell 7+.
- [ProxyBridge](https://github.com/InterceptSuite/ProxyBridge) installed —
  e.g. `winget install InterceptSuite.ProxyBridge`.
- NCSOFT PURPLE installed.
- A proxy to route through (SOCKS5 or HTTP), e.g. one provided by your ISP.
- Administrator privileges when running Proxion (required by ProxyBridge's
  WinDivert driver).

## Setup

1. Copy the example config and edit it:

   ```powershell
   Copy-Item config\proxion.config.example.json config\proxion.config.json
   notepad config\proxion.config.json
   ```

2. Fill in your proxy details:

   ```json
   {
     "proxy": {
       "type": "socks5",
       "host": "203.0.113.10",
       "port": 1080,
       "username": "",
       "password": ""
     }
   }
   ```

   `type` must be `socks5` or `http`. Leave `username`/`password` empty for
   an unauthenticated proxy. Note that UDP traffic is only proxied through a
   SOCKS5 proxy that supports `UDP ASSOCIATE` — an HTTP proxy will fall back
   to direct for UDP (see ProxyBridge's own docs for details).

3. (Optional) Set `purpleLauncherPath` / `proxyBridgeCliPath` explicitly if
   auto-detection doesn't find them (e.g. non-default install locations).
   Leave them as empty strings to auto-detect.

4. Add the executable name(s) of the game(s) you play under
   `gameProcessNames`, so Proxion keeps running (and keeps routing traffic)
   for as long as the game itself is open — not just while the PURPLE
   launcher window is up. A few common ones are pre-filled as examples;
   check Task Manager's "Details" tab while the game is running if you're
   not sure of the exact process name, and edit the list to match.

## Usage

From an **elevated** PowerShell prompt (Run as Administrator):

```powershell
cd path\to\proxion
.\scripts\Start-Proxion.ps1
```

Proxion will:
- print what it's doing to the console and to a timestamped file under `logs\`
- start routing PURPLE + your configured games through the proxy
- block in the foreground, monitoring those processes, until they all close
- clean up automatically when they do

Press `Ctrl+C` to stop early — the `finally` block still stops ProxyBridge
and restores direct traffic.

### Dry run

Check what Proxion would do (resolved paths, generated profile) without
launching anything:

```powershell
.\scripts\Start-Proxion.ps1 -DryRun
```

### Recovering from a crash

If PowerShell was killed and left `ProxyBridge_CLI.exe` running (traffic
still routed through the proxy), run:

```powershell
.\scripts\Stop-Proxion.ps1
```

## Config reference

| Field | Description | Default |
|---|---|---|
| `proxy.type` | `socks5` or `http` | required |
| `proxy.host` | Proxy hostname or IP | required |
| `proxy.port` | Proxy port | required |
| `proxy.username` / `proxy.password` | Proxy credentials, if required | `""` |
| `localhostViaProxy` | Route `127.0.0.0/8` / `::1` traffic through the proxy too | `false` |
| `trafficLogging` | Enable ProxyBridge's connection logging | `true` |
| `verbose` | ProxyBridge CLI verbosity: `0` silent, `1` logs, `2` connections, `3` both | `1` |
| `proxyBridgeCliPath` | Explicit path to `ProxyBridge_CLI.exe` | auto-detected |
| `purpleLauncherPath` | Explicit path to `PurpleLauncher.exe` | auto-detected under `Program Files (x86)\NCSOFT` |
| `gameProcessNames` | Extra process names (games) to route + monitor | `[]` |
| `pollIntervalSeconds` | How often to check whether PURPLE/games are still running | `3` |
| `startupTimeoutSeconds` | How long to wait for PURPLE/a game to appear before giving up | `30` |

## Project layout

```
config/
  proxion.config.example.json   # copy to proxion.config.json and edit
scripts/
  Proxion.psm1                  # core logic (config, profile generation, process control)
  Start-Proxion.ps1             # main entry point
  Stop-Proxion.ps1              # manual cleanup if a session was killed abnormally
logs/                            # created at runtime, one log file per session
.runtime/                        # created at runtime, holds the generated .pbprofile
```

## Notes & limitations

- This repo was developed and syntax/logic-tested on Linux with PowerShell 7
  (the platform-independent parts: config parsing, validation, and profile
  generation). It has **not** been exercised against a real Windows install
  of PURPLE or ProxyBridge, or against a live proxy connection — there is no
  Windows environment available in the sandbox this was built in. Please
  test locally before relying on it, especially the auto-detection paths and
  process-name matching for your specific games.
- If auto-detection of PURPLE fails (e.g. a regional variant installs under
  a differently-named folder, such as `Purple_TW` or `Purple_KR`), set
  `purpleLauncherPath` explicitly in your config.
- ProxyBridge's `.pbprofile` matches processes by executable filename, not
  full path, and by design matches every process with that name — the
  generated profile just lists PURPLE's filename plus your configured game
  filenames in one `PROXY` rule.
- Use only with a proxy you're authorized to use (e.g. one your own ISP
  provided you), and only for your own account/traffic.
