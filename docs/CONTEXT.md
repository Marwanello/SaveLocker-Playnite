# SaveLocker-Playnite — Session Context

Read this and [[REPO_MAP]] at the start of every session.

**What:** The Playnite half of SaveLocker's Windows pre-launch save gate — pull-or-block *before* a
game starts, matching the guarantee Linux/Steam Deck already has via `savelocker run`. Full design in
the main `SaveLocker` repo: `docs/tasks/playnite-plugin/plan.md` + `implementation-grouping.md`. This
repo is the plugin-side half; the agent-side half (Groups 1–2) already shipped in the main repo.

**Repo:** created 2026-09-15, sibling on disk to `SaveLocker` and `SaveLocker-Decky` at
`D:\Projects\SaveLocker\SaveLocker-Playnite`.

## Status

Freshly scaffolded — no plugin code yet. Group 3 (`implementation-grouping.md`'s "the spine:
scaffold, settings, core gate, matching") is the first work planned here; see the main repo's
`implementation-grouping.md` for the full phase/group breakdown.

## Next action

Start Group 3 (Phases 8–11) on its own branch/worktree, per this repo's own `AGENTS.md` task-execution
convention.
