# Development and design notes

## Architecture

Native Windows x64, C# 5, .NET Framework 4.x, WinForms and the built-in RDP ActiveX
control. No external managed packages, embedded browser or separate helper binary.

| Source | Responsibility |
| --- | --- |
| ChildStream.cs / ChildStream.Core.cs | Entry point, RDP interop, credentials, display/session policy. |
| DesktopSetup.cs | Explicit setup, Startup registration and installation identity. |
| LauncherApplication.cs / LauncherPolicy.cs | Async orchestration, session generations and confirmations. |
| LauncherWindow.cs / ChildAppWindow.cs / ThemePickerWindow.cs / AppThemes.cs | Native forms, semantic theme tokens, previews and diagnostics. |
| SessionAgent.cs / AgentMessages.cs / HelperRecovery.cs | Bounded protocol, peer/session validation and recovery. |
| HostDiscovery.cs / NativeHostDiscovery.cs / HostProfiles.cs / StreamingHost.cs | Host identity, selection, profile/port isolation and ownership. |
| WindowsInstallPaths.cs | Validated registry paths, App Paths/uninstall/Steam discovery and library manifests. |
| ChildApps.cs / NativeAppShortcuts.cs / ChildBrowser.cs | App discovery/dispatch and independent browser profiles. |

The parent obtains the actual child ID from Windows. Pipe names include user,
installation and session. Both peers verify executable/session identity; helper
ACLs permit the user and deny network logons. Version-3 JSON frames are bounded
to 4096 UTF-8 bytes. Opaque host/app IDs bind selections to current identities.
These checks do not isolate mutually trusted processes running as the same user.

Session generations reject old replies and approvals after disconnect/selection
changes. Shared busy state disables main/app actions while retaining catalog
rows and selection. Availability is separate, so completion cannot reenable a
disconnected picker. Search remains editable during requests; the latest pending
list query is queued with the picker, session generation and child identity.
Outdated query/page results are ignored. App queries use a connection generation
that survives unrelated host selection changes; disconnect/close invalidates it.
Restored picker connections request fresh data and pending-launch status. Launch
actions are never queued/replayed.
Native Browse dialogs suppress background host polling.

The app catalog searches up to 4,096 candidates in bounded pages with explicit
next offsets and totals. Saved EXEs are limited to 256 and prioritized. Start menu
EXE shortcut arguments and working directories are included in opaque identity;
caller-provided flags remain forbidden. Native registration metadata is cached
for 30 seconds; explicit Refresh invalidates it. A single STA launch worker owns
pending app dispatch/consent, revalidates app and child identity, and reports its
result through subsequent app-list requests. Pending launch buttons stay disabled;
cancellation is a terminal result, never a retry.

Host startup revalidates installation and prepared profile identity. Child
profiles isolate ports, credentials, certificates, pairing, app list and logs.
Process ownership needs matching session/path/config or a validated launch receipt.
Ambiguity stays unknown. Main services and conflicting listeners are never stopped.

Discovery resolves user/system Start menu EXE shortcuts, HKCU/HKLM and 32/64-bit
App Paths, supported uninstall metadata,
Steam registrations/libraries/manifests, running app paths, standard OS folders and
installation-scoped Browse choices. Browser shortcut resolution uses the full
candidate source rather than the display-limited catalog. Unregistered portable
locations require an explicit Browse choice; do not scan every drive or guess an
app's new location after it moves. Host install actions open official GitHub
repositories for manual setup. Chrome policies and ordinary icons are unchanged.

Recovery is a bounded, same-user, least-privilege Task Scheduler launch of this
EXE in the actual child. Starting a streaming host remains separately confirmed.
See the [session model](session-model.md) for shared state and elevation limits.

## Interface conventions

Keep the workspace central and companion controls quiet. Use clear session/host
labels, a stable Setup / repair and Themes footer, bounded wrapped diagnostics,
and full text in Details. Promote installation only after confirmed host absence.
Detected hosts leave a subdued action with default-No confirmation naming them.

Themes use semantic canvas, surface, inset, text, muted, action, action-text,
border, divider, focus, quiet, success and error tokens. Avoid tinting every control
the same color. Supported IDs: light, dark, ocean, sunset, forest, rose. Old
preferences migrate; Windows high contrast overrides custom colors. Miniature
window previews use production tokens. Enabled controls have distinct idle,
hover and pressed fills, readable action text and visible keyboard focus. Rounded
native buttons retain their keyboard/accessibility behavior. Layout does not
bounce or animate on hover; high contrast uses native system rendering.

Display setup retains the original pre-connect resolution/scale configuration.
There is no resize timer or UpdateSessionDisplaySettings call: maximizing the
viewer must not overwrite a client-selected resolution. Smart sizing scales the
preview proportionally and AxHost fills its zero-padding workspace. Differing
screen aspect ratios can leave letterboxing; do not crop/stretch to conceal it.

Preserve accessible names, native keyboard navigation, visible focus, readable
disabled controls, gentle dark palettes and minimum-size scrolling. Form tests
use the production manifest; actual DPI/assistive-technology acceptance is separate.

## Development

Build and test from the root using scripts/build.ps1 and scripts/test.ps1.
Test in Windows PowerShell 5.1 and PowerShell 7. Do not run setup, terminate live
sessions or start real hosts merely to validate source.

Keep runtime hosts, credentials, profiles, settings, logs and binaries out of Git.
scripts/release-files.txt is the explicit source allowlist for optional packaging;
update it with source/test/doc changes. Routine delivery is the compiled EXE.
Optional bundles are created only when requested; package tests use temporary fixtures.
