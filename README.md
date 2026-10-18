# ChildStream

**A second Windows desktop for games and apps, with your main desktop still available.**

[![Windows checks](https://github.com/ocr1n/childstream/actions/workflows/checks.yml/badge.svg?branch=master)](https://github.com/ocr1n/childstream/actions/workflows/checks.yml)
[![Platform: Windows x64](https://img.shields.io/badge/platform-Windows%20x64-446A80)](#requirements)
[![License: MIT](https://img.shields.io/badge/license-MIT-3F7059)](LICENSE)

ChildStream opens a native viewer into a Windows child session. Use that desktop
locally, or run a separate Sunshine/Apollo host inside it and connect from a
Moonlight client. Your physical desktop stays available for other work.

This fork builds on [mattxslv/childstream](https://github.com/mattxslv/childstream).
It adds a native launcher, host discovery, separate child host profiles, helper
recovery, app launching and themes. The original MIT attribution is preserved.
This is an experimental same-user Windows session, not a virtual machine.

[Get the EXE](#get-childstream) · [Quick start](#quick-start) ·
[Pair a client](docs/pairing.md) · [Troubleshooting](docs/troubleshooting.md) ·
[Compatibility](docs/compatibility.md)

## What it does

- **Start a desktop immediately.** A missing streaming host never blocks local use.
- **Keep streaming separate.** Supported child hosts have their own ports,
  pairing state, web credentials, configuration and logs.
- **Ask before starting a host.** Startup runs inside the attached child session;
  you handle Windows elevation there.
- **Recover a missing helper.** The launcher attempts a bounded, session-specific
  restart without signing out the child or closing games.
- **Open apps in the child.** Chrome/Edge use persistent independent profiles.
  Browse to installed apps or games; Steam reports main-session conflicts.
- **Keep controls simple.** Status, pairing addresses, reconnect, tray controls,
  readable diagnostics, and six themes with miniature window previews.

## Requirements

| Component | Requirement |
| --- | --- |
| Host PC | x64 Windows with child-session support and built-in RDP ActiveX. Windows 11 Pro is live-tested; other editions are unverified. |
| Runtime | .NET Framework 4.x. No .NET SDK, NuGet install or browser runtime is needed. |
| Account | A Windows account password, not a Windows Hello PIN. Domain, Entra and passwordless accounts are unverified. |
| First setup | Administrator approval to enable child sessions and register the helper. Run the viewer normally afterward. |
| Optional streaming | Compatible GPU/encoder, supported host, and Moonlight-family client. Start on the same LAN. |

Windows allows **one active, connected child session per system**. Files, installed
software, user settings, services and hardware remain shared. App licensing,
single-instance locks, anti-cheat and capture restrictions still apply.
See the [session model](docs/session-model.md) for elevation and scaling limits.

## Get ChildStream

1. Open [Windows checks](https://github.com/ocr1n/childstream/actions/workflows/checks.yml).
2. Choose a successful **master** run and download its **ChildStream-windows-x64**
   artifact. GitHub requires sign-in for downloads; artifacts expire after 30 days.
3. Put **ChildStream.exe** in a permanent, writable folder owned by your Windows
   user. The EXE contains setup and the helper; source files are not needed to run it.

Or [build the EXE locally](#build-from-source). Streaming hosts and drivers are
installed separately; they are not bundled in this repository.

## Quick start

1. **Open ChildStream.exe.** Choose **Setup / repair** on a new installation and
   approve the Windows prompt yourself. Setup enables child sessions, applies RDP
   preferences and registers this installation's idle helper. It downloads no host.
2. **Choose Start desktop.** Enter your Windows account password if asked and wait
   for **Connected**. Credentials are protected for your Windows user in `cred.bin`.
3. **Use the child desktop.** You can install and run software there even when
   ChildStream says **No host detected**.
4. **Add streaming when ready.** Install [Sunshine](https://github.com/LizardByte/Sunshine/releases)
   or [Apollo](https://github.com/ClassicOldSong/Apollo/releases), then **Refresh**.
   Use **Browse...** for a portable or nonstandard host location.
5. **Confirm Start [host].** Approve any elevation prompt inside the child preview.
   Declining leaves the desktop usable. Select the intended host if several exist.
6. **Pair the client** using the two distinct addresses below. Keep the viewer
   attached while streaming; **Minimize to tray** keeps the connection alive.

With no host installed, **Install a host...** is prominent. With a detected host,
the install action becomes subdued and asks before opening another host's GitHub
page. Each choice opens the official project repository; you download and set up
the host yourself. ChildStream does not download or run its installer. VibePollo is detected, but independent child startup is unsupported in this version.

## Pair a client

| Displayed field | Where to use it | Example only |
| --- | --- | --- |
| **Host web UI** | A browser on this host PC, including the main desktop | `https://127.0.0.1:48990/` |
| **Client address** | **Add PC** in Moonlight on the other device | `MY-PC:48989` |

Use the values displayed by ChildStream; ports can differ. If the PC name fails,
substitute its LAN IP and keep the port. `127.0.0.1` is only for a browser on the host.

Open the child's **Host web UI** and set up its web login. Add the **Client address**
in Moonlight, read the client's PIN, and submit it in the **PIN** tab of that child's
web UI. Return to Moonlight and choose **Desktop**.

Two discovered PCs can be normal when both hosts run. Their pairing state and
ports are separate. The main host's default web port, usually 47990, is not the
child's settings page. See the [complete pairing guide](docs/pairing.md).

## Apps, themes and controls

App discovery reads Windows App Paths, supported uninstall records and Steam's
registered location in both per-user and system 32/64-bit views. It also checks
running executable paths, Steam libraries/app manifests and standard OS folders.
These are resolved on your PC, with no developer-specific drive or username.
Unregistered portable apps need Browse once; the saved executable path is reused
by app discovery and the child browser shortcut. If an app moves without updating
its registrations, Browse to the new location rather than relying on a guessed path.

**Open app** lists detected browsers, Steam and Wallpaper Engine. **Browse app...**
adds a local installed EXE. Registered apps take priority, newest choice first.
Up to 16 registrations are saved; long paths can reduce the displayed list.
Browse an existing app again to bring it forward. Shortcuts, scripts, shell
commands and arbitrary launch flags are not accepted.

Child Chrome/Edge use `%LOCALAPPDATA%\ChildStream\Chrome-Child` and `Edge-Child`.
Main Chrome can stay open. Ordinary Chrome icons and URL handlers still use its
main profile; use **Open app** or the explicit child browser shortcut. Profiles
are not merged automatically. Sign in and enable browser sync yourself if wanted.

Exit Steam on the main desktop before opening it in the child. ChildStream reports
the conflict rather than terminating it. Use normal game launchers when required.
Wallpaper Engine can share settings; check the child's display mapping first.

| Control | Behavior |
| --- | --- |
| **Themes** | Light, Dark, Ocean, Sunset, Forest or Rose, with a saved preference. Windows high contrast takes precedence. |
| **Details** | Wrapped, scrollable diagnostics without long native hover strips. |
| **Reconnect** | Reattaches without signing out the child. |
| **Minimize to tray** | Keeps the viewer attached; double-click the tray icon to restore it. |
| **Exit viewer** | Does not sign out, but can interrupt capture. |
| **End session** | Asks before signing out the actual child and closing its apps. |

## Update and local data

Keep the same installation folder. Save work, end the child, close the viewer and
stop that installation's idle `ChildStream.exe -agent` helper before replacing the
EXE. Reopen afterward. If you move the folder, run **Setup / repair** again.
For recovery without signing out, see [troubleshooting](docs/troubleshooting.md).

Preserve these files when updating; they are excluded from Git and source bundles:

| Local data | Purpose |
| --- | --- |
| `Hosts/`, existing `Sunshine/` | Child host config, pairing state, certificates, keys and logs. |
| `cred.bin` | User-scoped DPAPI-protected Windows password; forget it from the tray menu. |
| `appearance.cfg`, optional `display.cfg` | Theme and desktop resolution/scale preferences. |
| `host-selection.txt`, `child-apps.txt` | Remembered host and app choices. |
| `launcher.log`, `autostart.log` | Private diagnostics; review/redact before sharing. |

The installation record and separate browser data live under
`%LOCALAPPDATA%\ChildStream`. Never publish host identities, keys or credentials.

Optional `display.cfg` accepts `widthxheight` or `widthxheightxscale`, for example
`2800x1272x225`. Dimensions: 200–8192; scale: 100–500 percent, or 0 for no override.
Invalid input falls back to 1920×1080 without a scale override. Hardware limits may be stricter.

## Build from source

From the repository root on x64 Windows, in Windows PowerShell 5.1 or PowerShell 7:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

Output: **ChildStream.exe** in the root. The build uses the installed 64-bit
.NET Framework compiler at
`%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` and framework assemblies.
Sources remain C# 5 compatible. Warnings are errors; a failed compile preserves the
previous EXE. Close this installation's processes before replacing its executable.

Build and tests do not run Windows setup. The optional `scripts/setup.ps1` wrapper
builds and configures Windows; use it only when you intend to change setup.
A normal EXE install needs no PowerShell command.

## Tests and contribution

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
```

Tests compile production sources with warnings as errors and check native forms,
host/profile identity, helper transport/recovery, app policies and PowerShell/packaging.
They use temporary fixtures and local pipes/listeners, without starting real hosts,
signing out sessions or running Windows setup. Pipe tests need to run outside app-container isolation.

Automated checks do not certify remote video, audio, controllers or every game.
See [tested behavior and gaps](docs/compatibility.md). For changes, keep the native
stack, add regressions for consequential bugs, run both shells, and exclude private
runtime files. Report Windows/host/client versions and the first failing step with issues.

| Location | Contents |
| --- | --- |
| `src/` | Viewer, helper, setup, host/app policies and native UI. |
| `scripts/` | Build/test, optional setup wrapper and explicit-allowlist packaging. |
| `tests/` | C# and PowerShell regressions. |
| `docs/` | Pairing, troubleshooting, compatibility, session model and design notes. |

## License and credits

[MIT](LICENSE). Original project and copyright: **mattxslv**.
This fork: [ocr1n/childstream](https://github.com/ocr1n/childstream).
Sunshine, Apollo, VibePollo and Moonlight are independent projects with their own
licenses and support channels. Their software is not redistributed in this source tree.
