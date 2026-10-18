# Compatibility and verification

Discovery, process startup, pairing, remote rendering and game compatibility are
different checks. This page records the available evidence for this fork.

## Hosts and clients

| Component | Implementation | Live evidence / limits |
| --- | --- | --- |
| Windows 11 Pro x64 | Native RDP child session and helper | Used on the development PC. Other editions, domain/Entra and passwordless accounts are unverified. |
| Sunshine | Separate child config/state/certificates, ports and elevated startup | Main and child ran concurrently. User confirmed Moonlight Desktop connected after the repeated-pairing fix. |
| Apollo | Child adapter, isolated config and display-option policy | Automated profile/policy tests. No completed live acceptance on a real installed Apollo build. |
| VibePollo | Detection and install link | Independent child startup is refused until shared display/helper behavior is verified. |
| Moonlight PC | Standard host protocol; no client modifications | User confirmed Desktop connection and reported the Windows/Linux/macOS PC client family. Each OS was not separately certified. |
| Other Moonlight-family clients | Expected to use the host's normal protocol | Device-specific video, input, controller and extension tests are still needed. |

No blanket claim covers HDR, every codec, audio routing, controllers, Internet
access or every host/GPU combination. Drivers are separate installations.
Test a default Desktop stream on the LAN first.

## Apps and recovery

| Behavior | Evidence | Limit |
| --- | --- | --- |
| Independent Chrome | User confirmed Google through Open app beside main Chrome. Explicit child shortcut dispatch was observed. | Normal Chrome icons/URL handlers keep the main profile. No profile merge is implemented. |
| Steam and games | Steam library and Metro Exodus menu/loading rendered after a deliberate main-session handoff; user subsequently reported games working. | Concurrent same-user Steam, all games, launchers and anti-cheat are not certified. |
| Wallpaper Engine | Child and main processes coexisted. | Playback/effects and monitor mapping are unverified; settings may be shared. |
| Helper recovery | A deliberately stopped child helper recovered in about 2.6 seconds while both Sunshine hosts stayed running. | Task Scheduler policy, mismatched installations and outdated helpers can still need repair. |
| Elevation | User previously approved Sunshine UAC through the local preview. Automated tests cover explicit runas dispatch, child validation, pending consent and cancellation. | New app elevation and remote secure-desktop capture/control need live acceptance; main Steam can retain its own prompts. |
| General app discovery | Native metadata enumeration finds Office apps on the development PC. Fixtures cover Blender/Office at custom paths, EXE shortcuts, argument/working-directory preservation, search and byte-bounded pages. | Installed Blender/Office rendering and all portable/Store launchers are not certified. |
| Maximize / resize | Geometry, reconnect, limits and failure-backoff tests; installed RDP control exposes the display-update API. | Connected desktop resizing and host capture changes still require live acceptance of this build. |
| UI/themes | Production-manifest native form checks cover layouts, contrast, theme access, bounded diagnostics and app-picker state. | Synthetic tests do not certify every monitor/DPI or assistive technology. |

## Pairing regression

Older Sunshine `2026.914.233613` failed repeated pairing with the same client
identity. A separate local protocol fixture reproduced duplicate-certificate 401
failures. With fixed build `2026.1017.22144`, repeated pairing authenticated,
saved pairing survived restart, and unpaired certificates still received 401.
The user then confirmed the real Desktop connection.

These were Sunshine host fixes. This repository does not bundle a patched
Sunshine binary. Use an updated upstream build with the repeated-pairing fixes,
preserve the child identity, and keep certificate verification enabled.
See [troubleshooting](troubleshooting.md).

## Automated verification

Run `scripts/test.ps1` under Windows PowerShell 5.1 and PowerShell 7. Coverage
includes display/session validation, setup failures, host selection after upgrades,
profile/port isolation, stale approvals, JSON validation, real local pipes,
helper recovery policy, browser isolation, catalog limits, UI busy states, themes,
pending app consent, search/paging races, hover/pressed contrast, viewport fitting,
and exclusion of private files from optional bundles. CI builds the production EXE.

Tests do not launch real hosts/games or certify capture/hardware. Results come
from the runner and GitHub Actions. Report Windows/GPU/driver/host/client versions,
the first failing step and redacted Details. Never attach credential, certificate,
private-key, host state or browser profile files.
