# How ChildStream's desktops work

ChildStream currently creates one Windows child session using the local RDP
ActiveX viewer. The helper runs inside that session and starts its streaming host
and apps there. The normal desktop and child have separate windows/process
sessions, while using the same Windows installation and user account. This is the
[Windows child-session mechanism](https://learn.microsoft.com/en-us/windows/win32/termserv/child-sessions).

| Part | What is separate | What is shared |
| --- | --- | --- |
| Windows child session | Windows, session processes and interactive desktop | OS, user account, files, registry and installed software |
| Child streaming host | Ports, host identity, pairing state, web credentials and apps config | GPU, encoders, network and Windows drivers/services |
| Child Chrome/Edge | Browser process and a stable child data directory | Installed browser binary; Google account can be signed into separately |
| Elevated apps | An app can request elevation from inside the child session | Administrative authority still applies to the whole computer |

## Helper recovery

A missing helper is restarted through a temporary Windows Task Scheduler task,
using the current user, normal privileges and the actual child session ID. Its
only action is the installed EXE with a session-bound helper command. The task
registration is removed after launch; the existing helper singleton handles any
duplicate startup. A 15-second cooldown limits failed attempts. Sunshine starts
only after the usual user confirmation. This uses Microsoft's
[session-targeted RunEx API](https://learn.microsoft.com/en-us/windows/win32/api/taskschd/nf-taskschd-iregisteredtask-runex).

## Browser independence

Use **Chrome (child desktop)** on the desktop, or Chrome under **Open app**.
The helper-created shortcut chooses the child data directory when run in its child
session. Clicking it on the physical desktop uses ordinary Chrome instead.
Ordinary Chrome icons and URL handlers still use the ordinary Chrome data folder.
Chrome's separate data directories are the supported way to run independent
instances. Sign in and use sync yourself for account/bookmark access; a live shared
folder would reintroduce process/data conflicts.
[Chromium profile guidance](https://www.chromium.org/developers/creating-and-using-profiles/).

## Elevation

An app started in the child can request a normal Windows administrator consent
prompt there; a Sunshine elevation prompt was previously accepted through the
local child preview. That does not establish that every secure prompt can be
captured and controlled over Moonlight. UAC normally switches to a protected
Winlogon desktop, with restricted access. General secure-desktop capture/input
requires separate host-specific testing. No automatic consent, global UAC change
or blanket elevation is used. **Open app / Run as administrator** explicitly
requests Windows `runas` from the child helper. Only one app launch can remain
pending at a time. Status checks remain responsive while consent waits, and
canceling it does not retry.
[Microsoft desktop and UAC behavior](https://learn.microsoft.com/en-us/windows/win32/winstation/desktops).

Installing a driver or changing a Windows service from the child still changes
the shared computer. More permission does not turn the child into an isolated OS.

## Multiple clients

Microsoft documents **one active, connected child session per system**. This
mechanism therefore cannot become an arbitrary number of simultaneous independent
client desktops. Windows Task View workspaces are also not separate user sessions;
Win32 desktop objects in one interactive window station have only one active
input desktop at a time. Streaming extra monitors/workspaces alone does not
provide separate app profiles and input sessions.
[Child-session limit](https://learn.microsoft.com/en-us/windows/win32/termserv/child-sessions),
[active input desktop](https://learn.microsoft.com/en-us/windows/win32/winstation/desktops).

For a future multi-user architecture, investigate separate Windows VMs or a
supported multi-session Windows deployment, then validate GPU allocation, host
capture/input, app/anti-cheat compatibility and the applicable Windows licensing.
Windows Enterprise multi-session is a specific deployment model, not a feature
unlocked by installing another streaming host.
[Microsoft multi-session overview](https://learn.microsoft.com/en-us/azure/virtual-desktop/windows-multisession-faq).

Changing Sunshine to Apollo/VibePollo cannot remove Windows session/input limits.
VibePollo child coexistence is still unverified and intentionally refused by this
build; it needs its own adapter/integration work before independent child startup
can be claimed.
## Viewer sizing

Resizing/maximizing ChildStream requests a child resolution matching the desktop
preview area after a short debounce. The initial resolution in `display.cfg`
is used for connection startup; its scale remains in effect afterward. The viewer
has no extra internal padding. If Windows rejects dynamic display updates, the
current resolution stays connected and is scaled proportionally, with a notice.
A streaming host may need its own display/capture refresh after a resolution
change; remote video acceptance must be tested on the chosen host/client.
