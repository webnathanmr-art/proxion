# Proxion

A Windows app that routes NCSOFT **PURPLE** launcher's games through a proxy of your
choosing — for example an ISP-provided SOCKS5/HTTP proxy — using
[ProxyBridge](https://github.com/InterceptSuite/ProxyBridge) as the underlying
traffic-redirection engine. ProxyBridge is embedded in `Proxion.exe` itself (it's
MIT-licensed), so there's nothing else to install.

Run `Proxion.exe`, fill in where PURPLE is installed and your proxy details, click
**Start** — Proxion launches PURPLE and sits in the system tray for as long as PURPLE
(or a game it launched) is running, then cleans up automatically. No PowerShell, no
config files to hand-edit.

**Platform: Windows only.** ProxyBridge intercepts traffic via the WinDivert kernel
driver, and PURPLE is a Windows-only launcher.

There are two builds in this repo — see [Which build should I use?](#which-build-should-i-use) below.

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

## Which build should I use?

| | `Proxion.App` | `Proxion.Personal` |
|---|---|---|
| Proxy settings | You enter them in a setup window | Hardcoded at build time |
| PURPLE path | Auto-detected, or Browse | Auto-detected, or Browse |
| Launches PURPLE | Automatically, right after setup | From a **Launch PURPLE** button you click |
| Auto-run on startup | N/A (always launches PURPLE) | Optional tickbox: skip the window next time and launch PURPLE immediately |
| Re-opening the window | N/A (only shown once per run) | Tray icon → **Show Settings**, any time, even mid-session |
| Icon | PURPLE's own icon | Live ping (ms) to the proxy, refreshed every 10s — falls back to a cat if the proxy doesn't answer ICMP pings |
| Changing the proxy later | Re-run the setup window | Tray icon → **Advanced Settings...** — overrides the hardcoded default without rebuilding |
| Intended for | Anyone — general-purpose | **Personal use only**, by whoever built it |

`Proxion.Personal` exists for one person's own repeated use across their own machines,
where re-entering the same proxy details every time is pure friction. Its proxy is
compiled in as a constant in `app/src/Proxion.Personal/PersonalProxyConfig.cs` — **it
is not meant to be given to other people to run**, since they'd have no visible way to
see or change what proxy it's hardcoded to route their traffic through. Its window
exists specifically so whoever *does* run it (i.e. its own builder) always sees the
host/port before anything starts, and can change the PURPLE path or auto-run setting
whenever they want via the tray icon.

If you want a build to hand to someone else, that's what `Proxion.App` is for.

**Advanced Settings** (tray icon → Advanced Settings...) lets you change the proxy
type/host/port/username/password at runtime, without rebuilding. The first time you
save a change there, it's written to the same settings file `Proxion.App` uses
(`%AppData%\Proxion\settings.json`, password encrypted) and takes over as the
effective proxy from then on — `PersonalProxyConfig.cs`'s hardcoded values are only
ever used as the *initial* default, before any override has been saved. If PURPLE is
already running when you save a change, ProxyBridge is restarted immediately with the
new proxy.

## How it works

1. **Setup window** (`Proxion.App`) or **settings window** (`Proxion.Personal`) opens.
   Both let you browse to `PurpleLauncher.exe` (auto-detected under
   `Program Files (x86)\NCSOFT` if possible). `Proxion.App` also has fields for your
   proxy's type, host, port, and optional username/password (`Proxion.Personal`'s proxy
   is fixed at build time, but its host/port are always shown in the window so you know
   what it's routing through). Settings are remembered (password encrypted for your
   Windows account, for `Proxion.App`) so you don't need to re-enter them next time.
   - `Proxion.Personal` also has a tickbox: **Automatically launch PURPLE next time
     Proxion starts**. Leave it unchecked to always see the window first; check it to
     skip straight to launching PURPLE (and hiding to tray) on future runs.
2. You click **Start** (`Proxion.App`) or **Launch PURPLE** (`Proxion.Personal`) —
   or, if you ticked auto-run, this happens immediately without showing the window.
   Proxion generates a ProxyBridge `.pbprofile`, starts the embedded ProxyBridge CLI
   headlessly (this needs Administrator — see below), and launches PURPLE.
3. The window hides and a **tray icon** appears — the console-less app has nothing
   else on screen. Right-click it for **Stop Proxion** and **Open Log File**; in
   `Proxion.Personal`, also **Show Settings** (brings the window back to change the
   PURPLE path or the auto-run tickbox, without stopping anything already running) and
   **Advanced Settings...** (override the hardcoded proxy itself — see below).
   Double-click the tray icon to stop (`Proxion.App`) or show settings (`Proxion.Personal`).
   In `Proxion.Personal`, the tray icon itself shows the current ping (in milliseconds)
   to the proxy, re-checked every 10 seconds; if the proxy doesn't respond to ICMP pings
   (common for many servers/firewalls), it falls back to the cat icon instead of
   showing a permanently-stuck or misleading number.
4. Proxion polls the process tree in the background. Whenever PURPLE launches something
   new, that process's name is added to the proxy rule and ProxyBridge is restarted with
   the updated rule (a brief, sub-second interruption).
5. When PURPLE and everything it launched have closed — or you stop it from the tray —
   Proxion stops ProxyBridge and deletes the generated profile, restoring direct traffic.

## Requirements

- Windows 10+ (64-bit).
- NCSOFT PURPLE installed.
- A proxy to route through (SOCKS5 or HTTP), e.g. one provided by your ISP.
- Administrator privileges — `Proxion.exe` requests elevation itself (a UAC prompt) on
  launch, since ProxyBridge's WinDivert driver needs it.

ProxyBridge does **not** need to be separately installed — it's bundled inside
`Proxion.exe` and extracted to `%LocalAppData%\Proxion\bin` on first run.

## Getting the exe

Every push to this repo builds `Proxion.exe` (the general `Proxion.App`) via GitHub
Actions (`.github/workflows/build.yml`) and uploads it as a build artifact — see the
**Actions** tab, pick the latest successful "Build Proxion" run, and download the
`Proxion-windows-x64` artifact.

**On this branch only**, CI also builds `Proxion.Personal` (as `Proxion-Personal-windows-x64`)
via a second job, `publish-personal-exe`. That job is commented in `build.yml` with why
it's not something to copy to a more widely-shared branch or repo without thinking it
through first: it bakes `PersonalProxyConfig.cs`'s hardcoded credentials into the
published binary, and publishing that as a build artifact gives anyone with read access
to the repo a one-click download of them — a more casually-discoverable copy than the
source file itself. That's a deliberate, accepted tradeoff here for a private, personal
branch; don't merge this job as-is into `main` or any branch/repo with wider access.

To build either one yourself (works from Windows, Linux, or macOS — the .NET SDK can
cross-compile a Windows executable):

```bash
# The general app:
dotnet publish app/src/Proxion.App/Proxion.App.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish

# Or the personal fork (edit PersonalProxyConfig.cs first):
dotnet publish app/src/Proxion.Personal/Proxion.Personal.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish-personal
```

The resulting `Proxion.exe` is a single, self-contained file (~160 MB, since it bundles
the .NET 8 runtime, WinForms, and ProxyBridge) — copy it anywhere and run it, no .NET
install required on the target machine. If your users already have the .NET 8 Desktop
Runtime installed, you can drop `--self-contained true` and the two `Publish*`
properties for a framework-dependent build instead (well under 1 MB, but requires that
runtime).

## Project layout

```
app/
  src/
    Proxion.Core/      # Pure logic: profile generation, process-tree scoping, proxy
                        # validation. No Windows dependency - runs and tests anywhere.
    Proxion.Windows/    # Shared Windows-only services: settings storage, PURPLE
                        # auto-detection, WMI process-tree snapshots, ProxyBridge
                        # process control, and the embedded ProxyBridge/icon assets.
    Proxion.App/        # The general-purpose WinForms app: setup window + tray icon.
    Proxion.Personal/   # The personal fork: hardcoded proxy, settings window (PURPLE
                        # picker + Launch button + auto-run tickbox) + tray.
  test/
    Proxion.Core.Tests/ # xUnit tests for Proxion.Core (30 tests covering profile
                        # generation and, especially, the process-tree scoping logic).
.github/workflows/build.yml  # CI: runs the tests, then publishes Proxion.App's exe
```

`Proxion.Core` deliberately has zero Windows-specific dependencies, so the logic that
decides *which* processes are "PURPLE's own" (`ProcessTreeAnalyzer`,
`SessionRuleTracker`) and *what* the generated `.pbprofile` looks like
(`PbProfileBuilder`) is fully unit-tested — run `dotnet test app/test/Proxion.Core.Tests`
on any platform.

## Third-party components bundled in

- **[ProxyBridge](https://github.com/InterceptSuite/ProxyBridge)** v4.0.0 (MIT license) -
  `ProxyBridge_CLI.exe` and `ProxyBridgeCore.dll`, taken unmodified from InterceptSuite's
  official `ProxyBridge-Setup-4.0.0.exe` release installer.
- **[WinDivert](https://github.com/basil00/Divert)** - `WinDivert.dll` and
  `WinDivert64.sys`, bundled the same way ProxyBridge itself bundles them (see
  WinDivert's own license for terms).

All four files live under `app/src/Proxion.Windows/Assets/ProxyBridge/` and are embedded
as resources (`EmbeddedProxyBridge.cs` extracts them to
`%LocalAppData%\Proxion\bin` on first run).

## Notes & limitations

- This was built and tested in a Linux sandbox with no Windows machine, real PURPLE
  install, or live proxy available. `Proxion.Core`'s logic is covered by unit tests
  (`dotnet test`, 30 passing), and both `Proxion.App` and `Proxion.Personal` were
  verified to compile and publish cleanly for `win-x64` — including the embedded
  `requireAdministrator` manifest, the embedded ProxyBridge binaries, and the embedded
  icon — but the actual WinForms UI, tray icon, WMI process-tree polling,
  PURPLE/ProxyBridge process management, and a real proxy connection have **not** been
  exercised end-to-end. Please test locally before relying on it.
- If auto-detection of PURPLE fails (e.g. a regional variant installs under a
  differently-named folder, such as `Purple_TW` or `Purple_KR`), use the **Browse**
  button next to the PURPLE field in the setup/settings window.
- The process-tree scoping is polling-based (every 3 seconds) rather than event-driven,
  so there's a small window (well under the poll interval, in practice) where a
  just-launched game hasn't been detected yet. If PURPLE hands off to a game and exits
  *itself* within that window, Proxion could miss adding the game to the rule — in
  normal use PURPLE stays running alongside its games, so this is an edge case rather
  than the common path.
- The ping-based tray icon uses ICMP (`System.Net.NetworkInformation.Ping`), which many
  proxy servers/firewalls block entirely — that's the normal case the cat-icon fallback
  handles, not a bug. A successful ping also only confirms the host answers ICMP, not
  that the proxy port itself is reachable or working.
- **Committing hardcoded credentials is a real tradeoff.** `PersonalProxyConfig.cs` puts
  a plaintext username/password in source control. That's fine for a private repo you
  fully control, but worth remembering if this repo's visibility or ownership ever
  changes — anyone with read access to the repo (now or via its history, even after a
  later edit) can read those credentials.
- Use only with a proxy you're authorized to use (e.g. one your own ISP provided you),
  and only for your own account/traffic.
