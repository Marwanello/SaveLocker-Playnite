# SaveLocker — Playnite plugin

Gives [SaveLocker](https://github.com/SkorcherX/SaveLocker) a genuine pre-launch save gate on
Windows — pull the newer save (or block on a real conflict) *before* Playnite starts the game, the
same guarantee the Linux agent's `savelocker run -- %command%` wrapper already gives Steam Deck.

**You need the SaveLocker Windows agent installed and running for this to do anything.** The plugin
talks to the agent's local API on `localhost:5178`; with no agent reachable it fails **open** —
games always launch, SaveLocker is never the reason one won't start.

**Compatibility note:** this plugin currently requires an agent built from
[`Marwanello/SaveLocker`](https://github.com/Marwanello/SaveLocker) (a fork) — not
[`SkorcherX/SaveLocker`](https://github.com/SkorcherX/SaveLocker), the main/upstream repo. Every
agent-side API addition this plugin depends on (install-directory exposure, the conflict/matching
routes, the Playnite-plugin self-update and install routes) lives only on the fork's branches for
now and has not been merged upstream. This is expected to change once those changes are merged, but
until then, point the agent you install at a build of the fork, not the upstream repo.

## Why this exists

The Windows tray already syncs every tracked game regardless of how it's launched (Steam, Epic,
GOG, a bare `.exe`) — Playnite is not required for ordinary push/pull. What Windows has never had is
a moment *before* the game's process starts that it can trust: `ProcessWatcher` only notices a
launch after the fact, after the game may already have its save file open. Playnite's
`OnGameStarting` hook (fires before Playnite spawns the process) is that missing boundary.

## What it does

- **Before launch**: matches the Playnite game to a SaveLocker tracked game automatically (Steam
  AppID → install directory → name/alias — no picker in the common case), then asks the agent
  whether it's safe to start. A newer cloud save pulls silently; a genuine conflict blocks the
  launch with a resolve dialog; anything else (agent offline, network hiccup) fails open.
- **After exit**: asks the agent to push, the moment Playnite notices the game closed — faster and
  more certain than waiting for the tray's own polling watcher, which still runs as a safety net.
- **Right-click menu**, theme-independent: **Sync now** (push-then-pull-or-block, with a dialog
  stating the outcome), **Resolve conflict…** (opens the real resolve window, or says there's
  nothing to resolve), and **Link to SaveLocker** (see below) for a game that isn't tracked yet.
  A **"SaveLocker: Linked"** tag marks a game once it's tracked.
- **Link to SaveLocker popup**, for a game the automatic matching chain couldn't place on its own:
  search tracked games, automatic manifest lookup, manual manifest search, or browse to the save
  folder by hand — plus a nudge notification the first time a launched game can't be auto-matched.
- **Self-updating**: checks the agent's own update channel on startup and replaces its files when a
  newer version is published, the same way the Windows tray keeps itself current — a restart notice
  appears since a compiled Playnite extension can't hot-reload.
- **Nothing else.** No game discovery, no save-path picking, no replacement for the agent's own UI —
  those stay exactly as they are today for a game launched any other way. (The agent itself *can*
  now discover Playnite's library directly and offer this plugin for install — see below — but that
  lives in the main agent repo, not here.)

## Install

Not yet on Playnite's official add-on database — a submission is prepared
(`docs/addon-submission/`) but the pull request hasn't been opened yet; see
[`docs/CONTEXT.md`](docs/CONTEXT.md) for exactly what's still blocking it. Until it's listed, install
either:

- **The `.pext` from this repo's [releases](../../releases)** by double-clicking it (Playnite is
  registered as the handler) or via **Add-ons → Install add-on from file**; or
- **From the agent itself** (SaveLocker agent-ui, Overview page → Playnite plugin card): a one-click
  **Install automatically** button, or the same `.pext` download link.

Either way, the agent keeps it updated by itself afterward.

## Status

Early development — see [`docs/CONTEXT.md`](docs/CONTEXT.md) for where this stands right now.
