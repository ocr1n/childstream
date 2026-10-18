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

**Open app / Run as administrator** requests Windows `runas` from the actual
child helper. The user clarified that Windows consent appears inside the local
ChildStream RDP viewer, whose preview turns blue, while Moonlight keeps showing
the normal child desktop without that prompt. The earlier description of a
physical-main-desktop prompt was a misinterpretation. Helper and consent process
session checks are consistent with the clarified observation.

Initialized Sunshine child profiles now use a demand SYSTEM host with DXGI
capture, explicitly bound to the child session. This addresses the gap observed
with the earlier administrator/WGC host: WGC showed consent in the local RDP
preview while Moonlight kept showing the ordinary desktop. Microsoft restricts
secure-desktop duplication to LOCAL_SYSTEM. The stock Sunshine service chooses
the physical console, so ChildStream must bind its own token to the child.
[Sunshine's UAC discussion](https://github.com/LizardByte/Sunshine/issues/3487),
[DXGI access requirements](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgioutput1-duplicateoutput),
[Sunshine service targeting](https://github.com/LizardByte/Sunshine/blob/master/tools/sunshinesvc.cpp).

First-use web-login setup remains WGC; quit that host and start it again through
ChildStream after setting its credentials. For WGC or an unsupported host,
respond to Windows yourself through the local ChildStream preview. With protected
Sunshine capture running, the child prompt can appear through Moonlight. Only one
app launch can remain pending; status checks remain responsive and cancellation
does not retry. No automatic consent or global UAC policy change is used.

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

The viewer uses proportional smart sizing. Resizing/maximizing its window does
not change the child desktop resolution. The connection starts with the existing
`display.cfg` values, or the default 1920x1080; subsequent display changes remain
under Windows and the streaming host/client's control. Automatic matching to the
host PC's preview size was reverted because it interfered with client resolution
selection. Different aspect ratios can leave letterboxing rather than stretching
or cropping the image. No host display configuration is changed by this rollback.


## Protected Sunshine capture

The temporary SYSTEM/ddx test succeeded on 2026-10-18: the user connected a
Moonlight PC client and saw and accepted Steam UAC in the streamed child desktop.
The release routes initialized Sunshine child profiles through a demand service
that binds its SYSTEM token to the exact child session. It never chooses the
physical console as a fallback, changes UAC policy, or approves a prompt.

This is capture/input support for the Windows protected desktop, not blanket
administrator permission for applications. First-use web-login setup remains
local WGC until Sunshine is quit and restarted through ChildStream. Apollo and
VibePollo privileged capture remain unverified. The initial service approval is
on the host PC's physical desktop. A temporary normal-user task starts the
approval relay there and is deleted after acknowledgment. Its elevated parent
supervisor survives child logoff to hold the profile lease through teardown and
pairing commit. An alternate administrator account is not supported for this path.

Pairing state, certificates and web credentials use protected, version-independent
profile generations with a last-good backup. The active host works on a disposable
copy; complete validated state commits atomically after its host/job stop. Changing
the original child files afterward cannot silently replace the protected identity.

The demand service is marked for deletion immediately and follows the exact
viewer/helper/setup process lifetimes. There is no 20-minute release limit or
automatic startup. A worker heartbeat is protected by administrator/SYSTEM write
permissions; ordinary helpers use it because Windows refuses their native
SYSTEM-process inspection. Stale or inconsistent evidence blocks duplicate starts.

Use Moonlight's Desktop entry, then launch apps in the child. Upstream Sunshine's
SYSTEM application helper selects the active physical console user. Desktop-only
is the supported workflow; the Sunshine web UI can still add app commands, so it
is not an enforced API restriction.

The release implementation has automated policy, atomic-state and lifecycle
checks. Its first human-approved live activation remains separate from the
successful temporary feasibility test.
