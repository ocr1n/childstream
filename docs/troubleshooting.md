# Troubleshooting

Use **Details** for the full wrapped diagnostic. A missing helper is different
from a missing streaming host.

| Symptom | What to do |
| --- | --- |
| Setup required | Keep the EXE in its final writable folder and choose Setup / repair. Approve elevation, then start the desktop. |
| Password rejected | Use the Windows password, not a Hello PIN. Forget saved credentials from the tray menu and retry. Domain/Entra/passwordless flows are unverified. |
| No host detected | The local desktop still works. Install Sunshine/Apollo, Refresh, or Browse to its EXE. |
| Helper unavailable / timed out | Leave the child attached for recovery. Verify the registered installation folder and repair Startup if needed; see below. |
| Status unknown | Windows could not verify a host's session/config. Check Details before starting another copy. |
| Host exits / port collision | Check its private sunshine.log. Resolve the collision yourself; ChildStream does not kill existing listeners. |
| Browser fails to open settings | Copy Host web UI into a working main-desktop browser. Child Chrome is optional for pairing. |
| Client cannot find child | Add Client address manually. Substitute the LAN IP if the name fails, keeping the displayed port. Check host firewall/network guidance. |
| Chrome icon opens main Chrome | Use Open app or Chrome (child desktop). Normal icons use the normal profile. |
| Steam already running elsewhere | Exit main-desktop Steam yourself, then open it through ChildStream. |
| Process starts without usable UI | Check the child taskbar and normal app launcher. Dispatch does not verify rendering, sign-in, anti-cheat or RDP support. |
| Administrator prompt missing from Moonlight | Check the local ChildStream preview on the host PC. It can show the child's protected Windows prompt while Sunshine's WGC stream cannot. |
| Client resolution changes when maximizing | Update to the build with viewport-driven resolution changes reverted. Reconnect after updating; choose resolution through your streaming host/client as before. |
| Stream stops when viewer closes | Keep the viewer attached; use Minimize to tray instead of Exit viewer. |

## Helper recovery

On a pipe timeout, the attached launcher attempts a temporary Windows task in the
actual child session, as the same user at normal privilege. The registration is
removed after launch. Failed attempts have a 15-second cooldown. Sunshine startup
still requires confirmation.

If recovery fails:

1. Open the EXE from the folder named in Details. Avoid a copied download/build EXE
   in a different folder.
2. Choose **Setup / repair** to rewrite and reenable ChildStream's Startup entry
   for future sign-ins. Task Scheduler policy or an old helper can still block recovery.
3. In an existing child, use **Restart ChildStream helper** if present, or run that
   installation's EXE with `-agent` there. Starting an idle main-desktop helper
   alone does not establish child control.
4. If the running helper is an older build, close it inside the child before
   starting the updated one. Alternatively, save work, **End session**, then start
   a fresh child. Ending the session closes its apps.

Record the exact error if Windows policy blocks Task Scheduler. Do not turn off
Windows protections merely to hide a transport failure.

## UniqueID / unauthorized client / certificate error 401

Two discovered PCs can be normal when main and child hosts run. Select the child's
displayed Client address and pair through its Host web UI. Submit one current PIN
request at a time.

If the first pairing works and later attempts fail, check Sunshine's version.
Old build `2026.914.233613` reproduced duplicate-identity/certificate errors;
fixed build `2026.1017.22144` passed repeated-pairing checks. Use an updated
[official Sunshine build](https://github.com/LizardByte/Sunshine/releases) containing
the fixes. Close the selected child host before updating its executable and
preserve its config, identity, certificates and pairing state.

Try the existing client entry after upgrading. If requested, complete one new PIN
through the child's displayed web UI. Do not disable certificate checks, delete
every paired device or reset the host ID as a blanket fix. Deliberate re-pairing
may be needed after a certificate change; identify the failing instance first.

## Setup changes and uninstalling

Explicit setup enables Windows child sessions and writes:

| Location | Change |
| --- | --- |
| `HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server` | `fDenyTSConnections = 0` |
| Same path, subkey `WinStations` | `DWMFRAMEINTERVAL = 8` |
| `HKCU\Software\Microsoft\Terminal Server Client` | `RemoteDesktop_SuppressWhenMinimized = 2` |
| Current user's Startup | `ChildStream helper.lnk` targeting this EXE; only its disabled Startup approval is cleared. |
| `%LOCALAPPDATA%\ChildStream\installation.txt` | Registered installation folder. |

Setup is not transactional and does not save previous registry values. Partial
failure reports that earlier steps may have changed. It installs no host, driver
or firewall rule and does not disable UAC.

To uninstall, save work and end the child, exit the viewer/helper, remove its
Startup and helper/browser shortcuts, then remove the installation folder.
Remove the installation record and separate browser data under
`%LOCALAPPDATA%\ChildStream` only if you want to discard them. Back up host
identities/browser data if you may return.

Restore Windows child-session/RDP preferences only to recorded previous values,
after checking whether other software needs them. There is no automatic rollback.
Older setups may have a common Startup `childstream-sunshine.cmd` or firewall
rule `ChildStream Sunshine`; inspect ownership separately before removing one.

## An app is missing, opens elsewhere, or waits for administrator consent

Use **Open app**, search its name or path, and **Refresh** after installing it.
Browse to the installed EXE if it has no App Paths or normal EXE Start menu entry.
A portable app moved to another folder needs to be browsed again. Shell/script,
Store/UWP and advertised installer shortcuts are not general app launch targets.

An app can hand off to its existing same-user instance. In particular, exit main
Steam yourself before opening child Steam and launching a game there. Running an
EXE as administrator does not transfer an existing main-desktop window.

Choose **Run as administrator** explicitly when needed. Check the local
ChildStream preview on the host PC for Windows consent. The preview may turn
blue and show the child's protected prompt while Moonlight keeps showing the
ordinary desktop without it. This was the user's clarified observation in the
Winver tests; the prompt was not taking over the physical desktop.

The current child host uses WGC. Sunshine's maintainer describes UAC visibility
with its service and DXGI capture, rather than the direct administrator/WGC
startup used here. The stock service targets the console session, so installing
or restarting it does not automatically solve child-session capture.
[Upstream UAC discussion](https://github.com/LizardByte/Sunshine/issues/3487).

Respond to Windows yourself through the local preview. The app picker reports
pending startup and disables repeat launches until it settles. Canceling consent
ends that request. ChildStream keeps UAC enabled. Remote secure-desktop capture
and input need a separately validated child-aware implementation.

A possible Steam workaround is to exit Steam yourself, open child Steam through
**Run as administrator**, approve the initial prompt through the local
ChildStream preview, then test the game. This gives Steam and games it directly launches administrator privileges.
It may reduce later prompts through
[Windows token inheritance](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/user-account-control/how-it-works),
but separate launchers, prerequisites and anti-cheat can behave differently.
This is unverified for individual games and does not add secure-desktop capture.

After upgrading, the viewer and helper must both load the new EXE. A version-3
helper cannot exchange app messages with an old viewer. Save child work and end
that session normally before replacing the EXE; restart ChildStream afterward.
