# Child-bound privileged Sunshine capture

The user confirmed on 2026-10-18 that a temporary LOCAL_SYSTEM Sunshine process
bound to the actual child session, using DXGI (ddx), streamed and accepted a Steam
UAC prompt through Moonlight. The previous administrator/WGC process streamed the
ordinary child desktop but omitted its protected Windows consent screen.

The release integrates this capture path into the native launcher, preserving
one installation and EXE-only delivery. Actual Windows consent always remains
a human action. No policy change or automatic approval is part of this design.

## Release behavior

For Sunshine with initialized child web credentials, Start host requests human
Windows elevation on the host PC's physical desktop, snapshots the selected host into an administrator-protected
per-run folder and starts a demand-only SYSTEM service. That service launches
only the selected Sunshine snapshot, on the verified child session's default
desktop, in a private kill-on-close job. It is immediately marked for deletion;
there is no automatic or permanent service registration. Its lifetime follows
the exact viewer, helper and elevated setup process identities, not a 20-minute
test timer. The physical console host and ordinary child apps are never stopped.

The authenticated named-pipe viewer PID is bound into the plan. A temporary
least-privilege task opens a normal-user approval relay on the physical desktop,
then its registration is deleted after acknowledgment. The resulting elevated
supervisor stays in that parent session, holding the profile lease across child
logoff until host shutdown and pairing commit finish. Startup has a five-minute
immutable expiry, a retained deny-only cancellation marker and a supervisor-owned
stop event. SYSTEM reads only the protected plan and event, never user cancellation
files. A timed-out request cannot launch a host after the caller reports failure.

First-time uninitialized credentials use the existing local WGC host to establish
the child web login. The UI and README explain that quitting that host and
starting it again enables remote UAC. An unauthenticated SYSTEM web UI is forbidden.
Apollo keeps its existing launch path; privileged child capture is not certified
for Apollo or VibePollo.

## Data and trust

A stable protected profile under Program Files is keyed by Windows SID, portable
installation path and selected Sunshine path, excluding version metadata.
Initial migration copies only the known child credentials/state/key/certificate
files, hash verifies their bytes and never overwrites an established protected
identity. Main-host data and original child files remain untouched. Desktop-only
apps and a bounded ddx configuration are rebuilt; arbitrary preparation commands,
original apps or scripts are not copied into privileged execution.

Per-launch JSON is bounded, validated before elevation and frozen in the
protected snapshot. Executables, root DLLs and non-executable assets are hash
pinned. Reparse points, escapes, ADS and ordinary-user write grants are refused.
Runtime state remains writable only by SYSTEM/Administrators and readable by the
approved user. Backup locations and Desktop-only support are documented.

Normal helpers cannot inspect this SYSTEM host's image or start time on the
tested Windows build. A fresh, protected, atomically replaced heartbeat therefore
attests its PID, creation time, child session, profile and original selection.
The worker writes it only while its exact owned process is alive. Helpers reject
stale, foreign, malformed or writable status. This is reporting, never authority
to start another privileged executable.

## Lifecycle and validation

Cancellation, payload changes, occupied ports, identity changes and lost ownership
fail explicitly. No silent console fallback or consent replay. Firewall rules
are scoped to the exact snapshot, host port family and local subnet, then removed.
Cleanup removes only the owned run snapshot after confirmed host/job shutdown;
pairing data persists. UAC policy and client certificate verification are unchanged.
Sunshine advertises Desktop; open apps through the child shell/ChildStream because
stock SYSTEM Sunshine app-launch helpers target the active physical console.

Automated checks cover path, ACL, profile/port isolation, credential gating,
attestation freshness, cancellation/error reporting and existing app/session/UI
behavior. Live evidence is the user's Steam UAC test on the temporary host, not
a guarantee for every application, security product, GPU, driver or client.
The staged release still needs its first human-approved live activation.
