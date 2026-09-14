# SaveLocker-Playnite — Agent Instructions

## Session start (mandatory)

Read `docs/CONTEXT.md` and `docs/REPO_MAP.md` before doing anything else. Do not read any other
files, ask clarifying questions, or begin work until both files are loaded.

## Vault structure

`docs/` is this repo's own small vault. The actual design (phase list, local-API contract) lives in
the main `SaveLocker` repo's `docs/tasks/playnite-plugin/plan.md` and `implementation-grouping.md`
(sibling checkout at `../SaveLocker` on this machine) — read those before changing behavior.

| File | When to read it |
|------|----------------|
| `docs/CONTEXT.md` | Every session start — project state, quick-ref commands, gotchas |
| `docs/REPO_MAP.md` | Every session start — codebase layout |
| `docs/Gotchas.md` | Before touching the build, the pack step, or a real Playnite install |
| `docs/Build and Run.md` | When building, packing, or installing a test build |
| `docs/logs/` | When asked about session history |

## Task execution

1. Read the relevant phase(s) in the main repo's `docs/tasks/playnite-plugin/plan.md`.
2. Execute only what that phase describes, then stop and report.
3. One commit per phase successfully completed.
4. Update the status table in the main repo's `plan.md`/`implementation-grouping.md` the same
   session a phase ships.

## Session handoff (end of session)

1. Update `docs/CONTEXT.md` — current status, any new gotchas, next action.
2. Add a dated write-up under `docs/logs/` for anything nontrivial this session did.
3. Commit with a `Docs:` prefix commit message.

## Coding conventions

- Target framework is **net462** (Playnite's own SDK target), own toolchain, no `SaveLocker.sln`.
- `Playnite.SDK.dll` is referenced from the real installed Playnite, not NuGet — `docs/Gotchas.md`.
- The plugin holds no sync rules of its own — it calls the agent's local API and acts on the
  `LaunchDecision` it gets back. Fail open, religiously: only an explicit `Blocked` may stop a launch.
- No comments unless the WHY is non-obvious. No trailing summaries after diffs.
