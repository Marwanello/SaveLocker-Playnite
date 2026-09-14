# SaveLocker-Playnite — Agent Instructions

## Session start (mandatory)

Read `docs/CONTEXT.md` and `docs/REPO_MAP.md` before doing anything else. Do not read any other
files, ask clarifying questions, or begin work until both files are loaded. These two files define
the current project state and codebase layout.

## Vault structure

`docs/` is this repo's own small vault — much lighter than the main `SaveLocker` repo's, matching
`SaveLocker-Decky`'s scale rather than `SaveLocker`'s. Write-ups live under `docs/logs/`.

| File | When to read it |
|------|----------------|
| `docs/CONTEXT.md` | Every session start — project state, quick-ref commands, gotchas |
| `docs/REPO_MAP.md` | Every session start — codebase layout |
| `docs/Gotchas.md` | Before touching the build, the pack step, or a real Playnite install |
| `docs/Build and Run.md` | When building, packing, or installing a test build |
| `docs/logs/` | When asked about session history |

**The actual design lives in the main `SaveLocker` repo**, not here:
`docs/tasks/playnite-plugin/plan.md` and `implementation-grouping.md` (sibling checkout at
`../SaveLocker` on this machine) own the phase list, the local-API contract, and the
agent-side/plugin-side split. Read those before changing behavior, not just this repo's own docs —
this repo is the plugin-side half of a two-repo feature.

## Task execution

1. Read the relevant phase(s) in the main repo's `docs/tasks/playnite-plugin/plan.md`.
2. Execute only what that phase describes.
3. Verify per `docs/Build and Run.md` (build) and the phase's own hardware-verification note
   (Playnite plugin work is manual/hardware-checked — there is no CI for the "does it really load
   and really fire the hook" question).
4. Stop and report — do not continue to the next phase unless instructed.
5. One commit per phase successfully completed.
6. Update the status table in the main repo's `plan.md`/`implementation-grouping.md` the same
   session a phase ships — do not let it go stale (that repo's own `CLAUDE.md` convention).

## Session handoff (end of session)

1. Update `docs/CONTEXT.md` — current status, any new gotchas, next action.
2. Add a dated write-up under `docs/logs/` for anything nontrivial this session did.
3. Commit with a `Docs:` prefix commit message.

## Coding conventions

- Target framework is **net462** (Playnite's own SDK target) — not this project's `net10.0` sibling
  repos. No `SaveLocker.sln` entanglement; this is its own solution/toolchain.
- `Playnite.SDK.dll` is referenced from the real installed Playnite
  (`%LocalAppData%\Playnite\Playnite.SDK.dll`), not NuGet — see `docs/Gotchas.md` for why.
- The plugin holds no sync rules of its own. It calls the agent's local API and acts on the
  `LaunchDecision` it gets back; every rule (what counts as a conflict, when to pull, when to
  block) lives in `SyncEngine` in the main repo, not here. Don't reimplement any of it client-side.
- Fail open, religiously. A transport error, timeout, or unexpected response must never read as a
  reason to block a launch — only an explicit `Blocked` decision may.
- No comments unless the WHY is non-obvious. No trailing summaries after diffs.

## Build commands (exact)

```powershell
dotnet build src\SaveLocker.Playnite.csproj -c Release
# Pack (after a successful build):
& "$env:LocalAppData\Playnite\Toolbox.exe" pack bin\Release\net462 dist
```

See `docs/Build and Run.md` for the full loop, including installing into a real (or portable) Playnite.

## Auth model (consumed, not owned)

This plugin is a plain in-process `HttpClient` caller of the agent's local API on
`http://127.0.0.1:5178` (port configurable in the plugin's own settings, for testenv). Every request
carries `X-SaveLocker-Token`, read from `<agent state dir>\api-token` — see `docs/Gotchas.md` for the
exact path and why the plugin must never cache a stale token across an agent restart.
