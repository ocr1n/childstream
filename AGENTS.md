# ChildStream contributor instructions

## Scope and stack

- Work in the existing checkout; do not create redundant installations without need.
  Preserve the original MIT attribution.
- Native Windows x64, WinForms/RDP ActiveX, .NET Framework 4.x, C# 5-compatible
  sources. No managed dependency install, SDK or web development server.
- Read README.md, docs/design.md and docs/session-model.md before changing session,
  helper, host ownership or browser profile behavior.

## Commands (repository root)

- Build: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1`
- Test: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1`
- Also test: `pwsh.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1`
- Whitespace: `git diff --check`.
- Compiler warnings are errors; the runner also parses PowerShell. There is no
  separate linter, package manager or type-check command.
- Build output is ignored ChildStream.exe. Close this installation's processes
  before replacing it. A failed build must preserve the previous EXE.
- Tests use disposable fixtures and local pipes outside app-container isolation.
  Do not run setup or change real Windows/host settings merely to validate code.
- Setup is EXE-contained and deliberately elevated. The optional setup.ps1 wrapper
  builds and configures Windows; it is not a test/dependency-install command.

## Runtime and delivery

- Deliver the compiled EXE by default. Do not generate ZIP/source/checksum bundles
  unless requested. Keep scripts/release-files.txt synchronized with source.
- Preserve Hosts/, Sunshine/, credentials, browser data, selections, preferences
  and logs when updating. Never commit private runtime data, downloaded hosts or
  generated binaries. No secrets/.env are needed to build.
- Viewer and helper must use the same installation. Copying an EXE does not
  configure Windows or enable control of another folder's helper.
- Do not stop games, a live child, main Steam or hosts without user authorization.
- Keep unknown states explicit; preserve session/identity checks, UAC, certificate
  verification, app licensing and anti-cheat restrictions.
- Distinguish automated policy/process checks from live video, audio, controller
  and game acceptance. Report actual verification and remaining gaps.

## Documentation lookup

<!-- context7 -->
Use Context7 MCP to fetch current documentation whenever the user asks about a library, framework, SDK, API, CLI tool, or cloud service — even well-known ones like React, Next.js, Prisma, Express, Tailwind, Django, or Spring Boot. This includes API syntax, configuration, version migration, library-specific debugging, setup instructions, and CLI tool usage. Use even when you think you know the answer — your training data may not reflect recent changes. Prefer this over web search for library docs.

Do not use for: refactoring, writing scripts from scratch, debugging business logic, code review, or general programming concepts.

## Steps

1. Always start with `resolve-library-id` using the library name and what to look up in the library's documentation, unless the user provides an exact library ID in `/org/project` format
2. Pick the best match (ID format: `/org/project`) by: exact name match, description relevance, code snippet count, source reputation (High/Medium preferred), and benchmark score (higher is better). If results don't look right, try alternate names or queries (e.g., "next.js" not "nextjs", or rephrase the question). Use version-specific IDs when the user mentions a version
3. `query-docs` with the selected library ID and what to look up in the library's documentation (not single words), scoped to a single concept. If the question spans multiple distinct concepts (e.g. routing and auth and caching), make a separate `query-docs` call per concept with the same library ID, unless the question is about how the concepts interact — combined queries dilute ranking and return shallow results for each topic
4. Answer using the fetched docs
<!-- context7 -->
