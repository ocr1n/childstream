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
  approve its initial Windows request locally. Initialized Sunshine profiles use
  protected DXGI capture so clients can show child UAC prompts.
- **Recover a missing helper.** The launcher attempts a bounded, session-specific
  restart without signing out the child or closing games.
- **Find and open installed apps.** Search Windows app registrations and Start
  menu shortcuts, or Browse to a portable app. Choose normal or administrator
  startup inside the child; Steam reports main-session conflicts.
- **Keep display choices in control.** The viewer scales the desktop proportionally
  without replacing the resolution selected in the child or by the streaming host.
- **Keep controls clear.** Visible hover/pressed/focus states, pairing addresses,
  reconnect, tray controls and six themes with miniature window previews.

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
5. **Confirm Start [host].** For initialized Sunshine profiles, handle the initial
   Windows approval on the host PC's physical desktop. First-time setup and other
   hosts may ask in the local child preview. Declining leaves the desktop usable.
   Select the intended host if several exist.
6. **Finish first-time Sunshine setup.** Set the child web login. Quit Sunshine
   from its child-desktop tray icon, then choose **Start Sunshine** again. The
   initial local setup host uses WGC; subsequent starts use protected DXGI capture.
   Existing initialized child profiles migrate automatically.
7. **Pair the client** using the two distinct addresses below. Keep the viewer
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

**Open app** searches installed desktop apps, including programs such as Blender
and Microsoft Office when registered with Windows. Discovery reads per-user and
system Start menu shortcuts, all App Paths registrations (32/64-bit views),
supported uninstall records, Steam libraries/manifests and running app paths.
Locations come from this PC's registrations and OS folders; no developer-specific
drive or username is required. Search matches app names and executable paths.

Results are paged so long or non-English paths cannot hide the rest of the list.
Up to 4,096 candidates and 256 browsed executable locations are supported. Browsed
apps appear first. **Refresh** rescans registrations; ordinary results are cached
for up to 30 seconds. Use **Browse app...** for an unregistered portable app or an
app moved without updating its registration. Select its actual installed EXE.

Normal EXE Start menu shortcuts retain their app arguments and working directory.
Browser shortcuts instead use the separate child browser data. Script/shell
runners, network executables, Store/UWP links and advertised installer shortcuts
are not offered as general launch targets. Browsing accepts EXEs, not command
text, scripts or arbitrary flags. Discovery and dispatch cannot guarantee that an
app renders correctly or avoids its own single-instance and licensing rules.

Select an app and choose **Open in child**. If it needs elevation, explicitly
choose **Run as administrator**. Check the local ChildStream preview for Windows
consent. During first-use WGC setup the preview can switch to a blue protected
screen while Moonlight keeps showing the ordinary desktop. This indicates missing
protected-desktop capture, rather than an app launching in the physical session.
Initialized Sunshine profiles now use a child-bound LOCAL_SYSTEM host with DXGI
capture. The user confirmed both seeing and accepting Steam UAC through Moonlight
with the temporary implementation. The release preserves that capture path and
still requires human consent; no UAC policy is disabled. First-time WGC setup and
Apollo do not have this remote UAC guarantee. A pending prompt does not freeze
helper status or replay the launch. Canceling consent ends that request.

Child Chrome/Edge use `%LOCALAPPDATA%\ChildStream\Chrome-Child` and `Edge-Child`.
Main Chrome can stay open. Ordinary Chrome icons and URL handlers still use its
main profile; use **Open app** or the explicit child browser shortcut. Profiles
are not merged automatically. Sign in and enable browser sync yourself if wanted.

Exit Steam on the main desktop before opening it in the child. ChildStream reports
the conflict rather than terminating it. Games and UAC prompts started by main-desktop Steam can remain there. Use the
child Steam instance and normal game launchers when required. The administrator
action cannot move an already-running launcher or bypass anti-cheat.
Wallpaper Engine can share settings; check the child's display mapping first.

| Control | Behavior |
| --- | --- |
| **Resize / maximize** | Scales the preview proportionally without changing the child or streaming client resolution. |
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

Protected Sunshine capture keeps its authoritative identity under
`%ProgramFiles%\ChildStream Capture\<profile-id>\saved\<generation>`.
The profile key includes your Windows user, ChildStream folder and selected host
path; a host version update keeps that identity. Original child pairing files
are preserved as migration backups and are not reimported after the protected
store is initialized. Back up the protected profile as administrator; do not
publish it. Invalid updates retain the last-good identity and a failed working
copy for diagnosis. Normal shutdown removes the executable snapshot.

The capture service is demand-only and immediately marked for deletion, with no
automatic startup. It stops with the exact viewer/helper/setup instance or child
session. Starting it requires administrator consent from the same Windows account.
A temporary normal-user task opens that approval on the physical desktop, then
is deleted after acknowledgment. The elevated supervisor stays in that parent
session so it can finish saving pairing data after the child signs out. A canceled
or timed-out request cannot start later. Open apps through the child shell or
**Open app**: the supported Sunshine entry is
**Desktop**. Adding Sunshine app/preparation commands in its web UI can target the
physical console because of upstream SYSTEM launch behavior. Remote UAC streaming
does not bypass DRM, anti-cheat, application licensing or Windows security prompts.

The installation record and separate browser data live under
`%LOCALAPPDATA%\ChildStream`. Never publish host identities, keys or credentials.

Optional `display.cfg` accepts `widthxheight` or `widthxheightxscale`, for example
`2800x1272x225`. Dimensions: 200–8192; scale: 100–500 percent, or 0 for no override.
Invalid input falls back to 1920×1080 without a scale override. This is the initial
connection resolution. Changing or maximizing the viewer does not replace later
resolution choices made in the child desktop or by the host/client. Hardware
limits may be stricter.

## Build from source

From the repository root on x64 Windows, in Windows PowerShell 5.1 or PowerShell 7:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

Output: **ChildStream.exe** in the root. To build while that EXE is running, create
a staging directory and pass `-OutputPath <absolute-staged-EXE-path>`. Close the
viewer and helper before replacing the installed EXE. The build uses the 64-bit
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
