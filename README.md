# Proxion

A Windows app that routes NCSOFT **PURPLE** launcher's games through a proxy of your
choosing — for example an ISP-provided SOCKS5/HTTP proxy — using
[ProxyBridge](https://github.com/InterceptSuite/ProxyBridge)'s CLI as the underlying
traffic-redirection engine.

Run `Proxion.exe`, fill in where PURPLE is installed and your proxy details, click
**Start** — Proxion launches PURPLE and sits in the system tray for as long as PURPLE
(or a game it launched) is running, then cleans up automatically. No PowerShell, no
config files to hand-edit.

**Platform: Windows only.** ProxyBridge intercepts traffic via the WinDivert kernel
driver, and PURPLE is a Windows-only launcher.

## Only PURPLE's own games are ever routed

Proxion does not proxy based on a fixed list of game executable names you have to
maintain. Instead, once PURPLE is running, Proxion watches the Windows process tree:
it starts by routing only `PurpleLauncher.exe` itself, and each time PURPLE launches a
new process (the actual game, a patcher, etc.) Proxion detects it as PURPLE's child (or
grandchild) process and adds it to the proxy rule — automatically, with no game-specific
configuration needed. Nothing else on your system is ever added to the rule, because
nothing else is a descendant of PURPLE's process.

One caveat worth knowing: ProxyBridge itself matches traffic by *executable name*, not
by process ID. So if you happened to be running some unrelated program with the exact
same filename as a game PURPLE launches, its traffic would also be routed once that
name is added to the rule. This is a limitation of ProxyBridge's rule engine, not
something Proxion's process-tree tracking can fully close — but it means Proxion is
already far more precise than a hand-maintained name list, since only names actually
observed as PURPLE's own descendants ever get added in the first place.

## How it works

1. **Setup window**: browse to `PurpleLauncher.exe` (auto-detected under
   `Program Files (x86)\NCSOFT` if possible) and to `ProxyBridge_CLI.exe` (auto-detected
   from PATH / the default install location), then fill in your proxy's type, host,
   port, and optional username/password. Settings are remembered (with the password
   encrypted for your Windows user account) so you don't need to re-enter them next time.
2. Click **Start**. Proxion generates a ProxyBridge `.pbprofile`, starts
   `ProxyBridge_CLI.exe` headlessly (this needs Administrator — see below), and launches
   PURPLE.
3. A **tray icon** appears (using PURPLE's own icon) and the console-less app has
   nothing else on screen. Right-click it for **Stop Proxion** and **Open Log File**, or
   double-click it to stop.
4. Proxion polls the process tree in the background. Whenever PURPLE launches something
   new, that process's name is added to the proxy rule and ProxyBridge is restarted with
   the updated rule (a brief, sub-second interruption).
5. When PURPLE and everything it launched have closed — or you stop it from the tray —
   Proxion stops ProxyBridge and deletes the generated profile, restoring direct traffic.

## Requirements

- Windows 10+ (64-bit).
- [ProxyBridge](https://github.com/InterceptSuite/ProxyBridge) installed — e.g.
  `winget install InterceptSuite.ProxyBridge`.
- NCSOFT PURPLE installed.
- A proxy to route through (SOCKS5 or HTTP), e.g. one provided by your ISP.
- Administrator privileges — `Proxion.exe` requests elevation itself (a UAC prompt) on
  launch, since ProxyBridge's WinDivert driver needs it.

## Getting the exe

Every push to this repo builds `Proxion.exe` via GitHub Actions
(`.github/workflows/build.yml`) and uploads it as a build artifact — see the **Actions**
tab, pick the latest successful "Build Proxion" run, and download the
`Proxion-windows-x64` artifact.

To build it yourself (works from Windows, Linux, or macOS — the .NET SDK can
cross-compile a Windows executable):

```bash
dotnet publish app/src/Proxion.App/Proxion.App.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish
```

`publish/Proxion.exe` is a single, self-contained file (~160 MB, since it bundles the
.NET 8 runtime and WinForms) — copy it anywhere and run it, no .NET install required on
the target machine. If your users already have the .NET 8 Desktop Runtime installed,
you can drop `--self-contained true` and the two `Publish*` properties for a
framework-dependent build instead (a few hundred KB, but requires that runtime).

## Project layout

```
app/
  src/
    Proxion.Core/     # Pure logic: profile generation, process-tree scoping, proxy
                       # validation. No Windows dependency - runs and tests anywhere.
    Proxion.App/       # The WinForms app: setup window, tray icon, ProxyBridge/PURPLE
                       # process management, WMI process-tree snapshots. Windows-only.
  test/
    Proxion.Core.Tests/ # xUnit tests for Proxion.Core (30 tests covering profile
                        # generation and, especially, the process-tree scoping logic).
.github/workflows/build.yml  # CI: runs the tests, then publishes Proxion.exe
```

`Proxion.Core` deliberately has zero Windows-specific dependencies, so the logic that
decides *which* processes are "PURPLE's own" (`ProcessTreeAnalyzer`,
`SessionRuleTracker`) and *what* the generated `.pbprofile` looks like
(`PbProfileBuilder`) is fully unit-tested — run `dotnet test app/test/Proxion.Core.Tests`
on any platform.

## Notes & limitations

- This was built and tested in a Linux sandbox with no Windows machine, real PURPLE
  install, ProxyBridge install, or live proxy available. `Proxion.Core`'s logic is
  covered by unit tests (`dotnet test`, 30 passing), and `Proxion.App` was verified to
  compile and publish cleanly for `win-x64`, including the embedded
  `requireAdministrator` manifest — but the actual WinForms UI, tray icon, WMI
  process-tree polling, PURPLE/ProxyBridge process management, and a real proxy
  connection have **not** been exercised end-to-end. Please test locally before relying
  on it, especially: the setup window's auto-detection and validation, the tray icon's
  Stop/Open Log actions, and that new game processes are actually detected and added to
  the rule while PURPLE is running.
- If auto-detection of PURPLE fails (e.g. a regional variant installs under a
  differently-named folder, such as `Purple_TW` or `Purple_KR`), use the **Browse**
  button next to the PURPLE field in the setup window.
- The process-tree scoping is polling-based (every 3 seconds) rather than event-driven,
  so there's a small window (well under the poll interval, in practice) where a
  just-launched game hasn't been detected yet. If PURPLE hands off to a game and exits
  *itself* within that window, Proxion could miss adding the game to the rule — in
  normal use PURPLE stays running alongside its games, so this is an edge case rather
  than the common path.
- Use only with a proxy you're authorized to use (e.g. one your own ISP provided you),
  and only for your own account/traffic.
