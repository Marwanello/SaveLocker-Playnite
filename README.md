# SaveLocker — Playnite plugin

Gives [SaveLocker](https://github.com/SkorcherX/SaveLocker) a genuine pre-launch save gate on
Windows — pull the newer save (or block on a real conflict) *before* Playnite starts the game, the
same guarantee the Linux agent's `savelocker run -- %command%` wrapper already gives Steam Deck.

**You need the SaveLocker Windows agent installed and running for this to do anything.** The plugin
talks to the agent's local API on `localhost:5178`; with no agent reachable it fails **open** —
games always launch, SaveLocker is never the reason one won't start.

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
- **Nothing else.** No game discovery, no save-path picking, no replacement for the agent's own UI —
  those stay exactly as they are today for a game launched any other way.

Full design: [`docs/tasks/playnite-plugin/plan.md`](https://github.com/SkorcherX/SaveLocker/blob/main/docs/tasks/playnite-plugin/plan.md)
in the main SaveLocker repo (the agent-side half of this feature lives there; this repo is the
plugin-side half, same split as [SaveLocker-Decky](https://github.com/SkorcherX/SaveLocker-Decky)).

## Install

Not yet on Playnite's official add-on database (see the plan doc's Phase 16) — until then, install
the `.pext` from this repo's [releases](../../releases) by double-clicking it (Playnite is
registered as the handler) or via **Add-ons → Install add-on from file**.

## Status

Early development — see [`docs/CONTEXT.md`](docs/CONTEXT.md) for where this stands right now.
