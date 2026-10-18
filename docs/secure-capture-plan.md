# Child-bound secure capture implementation plan

> Implement directly using executing-plans; obtain one independent final review.

**Goal:** Integrate the confirmed SYSTEM/ddx child capture path without changing
UAC policy, destabilizing pairing, or retaining the temporary 20-minute prototype.
**Spec:** docs/secure-capture-design.md
**Stack:** C# 5 / .NET Framework 4.x / native WinForms, SCM, WTS and job objects.

## Constraints and review focus

Keep one existing checkout. No permanent/automatic service. Never approve UAC,
stop unrelated apps/hosts or replace the running EXE. Preserve original child
identity bytes and use protected persistent data after migration. Refuse missing
credentials, reparse/ADS/path escapes and ordinary-user-writable privileged files.

Review startup cancellation and partial copies; worker/setup crashes and job
assignment failure; stale/reused PID reporting; first-use/upgrade pairing; and
Sunshine's console-targeting app commands. Live GPU/UAC tests require a human;
automated tests must never launch a host, service or real elevation.

## Task 1: Policy and regressions

- [x] Add SecureCaptureTests via reflection and observe missing-policy failures.
- [x] Add bounded configuration, payload paths, protected-store keys/ACL checks,
  credentials validation, and heartbeat freshness in SecureCapturePolicy.cs.
- [x] Run the full suite and record actual results.

## Task 2: Launcher, protected runtime and status

- [x] Add runtime launch-plan construction, elevation dispatch and attested status.
- [x] Adapt the reviewed temporary SCM/job/token implementation into named,
  maintainable production files. Keep pairing data outside disposable run copies.
- [x] Route only initialized Sunshine profiles to this launcher; preserve initial
  local setup/Apollo paths. Report privileged hosts through protected status.
- [x] Add negative entry-point/lifecycle regressions; run both shell suites.

## Task 3: Release and publication

- [x] Update README, session model, troubleshooting, compatibility and allowlist.
- [x] Build a staged EXE while the current viewer remains open.
- [x] Independent final review; fix material findings with regression evidence.
- Check the final diff/private-file exclusions, commit and push master.
- Confirm the pushed commit's Windows checks and report live acceptance limits.
- [x] Verify temporary-test teardown without stopping ordinary hosts or apps.

## Ledger

Baseline e57e03b486fbbcab9bb85f95e350f6516bd2b326, source clean.
User explicitly requested unattended completion and push after live Steam UAC success.


### Implementation and verification

- Added policy tests first: missing secure-capture policy failed before the
  implementation. Additional red/green regressions exposed missing UUID checks,
  the protected status hook, atomic-reader sharing, lease exclusion, unexpected
  UUID replacement, cancellation, supervisor session and recovery-state handling.
- Independent source review identified late startup after timeout, ambiguous
  viewer enumeration and a hidden failed-pairing-save warning. These are fixed
  with deadline/cancellation gates, authenticated caller PID propagation and
  RecoveryRequired. Internal run snapshots are also excluded from discovery;
  its regression failed before the filter and passed afterward.
- Final focused independent review found no remaining material source defect.
- Windows PowerShell 5.1 and PowerShell 7 each passed 220 C# regressions,
  7 script checks and 3 packaging checks. Compiler warnings are errors.
- A real temporary least-privilege Task Scheduler probe verified exact parent
  session/SID targeting with no elevation. Its registration and fixtures were
  removed. The selected installed Sunshine snapshot has 114 asset files,
  1 root DLL and no forbidden executable assets; it fits the payload limits.
- The successful temporary live Steam capture test's private host, demand
  service, exact firewall rules, protected payload and desktop shortcut are gone.
  The original main Sunshine host remains untouched.

The local release is built without starting hosts or responding to consent.
Final installation requires no running viewer/helper/child host. Its production
relay, service lifetime and secure-desktop video/input still need a first
human-approved live activation. Automated checks do not certify every app,
installer, anti-cheat, credential provider, GPU or client.
