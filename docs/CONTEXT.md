# SaveLocker-Playnite — Session Context

Read this and [[REPO_MAP]] at the start of every session.

**What:** The Playnite half of SaveLocker's Windows pre-launch save gate — pull-or-block *before* a
game starts, matching the guarantee Linux/Steam Deck already has via `savelocker run`. Full design in
the main `SaveLocker` repo: `docs/tasks/playnite-plugin/plan.md` +
`implementation-grouping.md`. This repo is the plugin-side half; the agent-side half (Groups 1–2)
already shipped in the main repo.

**Repo:** created 2026-09-15 under `Marwanello` (the authenticated GitHub account this session used —
the user's own account, distinct from the project's canonical `SkorcherX` org the main repo lives
under: https://github.com/Marwanello/SaveLocker-Playnite). Sibling on disk:
`D:\Projects\SaveLocker\SaveLocker-Playnite`, next to `SaveLocker` and `SaveLocker-Decky`.

## Status — Group 3 (Phases 8–11) built this session, not yet hardware-verified

`implementation-grouping.md`'s Group 3 — "the spine: scaffold, settings, core gate, matching" — is
implemented end to end and **builds and packs clean**, but has not yet been run inside a real,
running Playnite. That's the one thing no automated check here can prove (this whole track is
manual/hardware-verified by design, per the plan doc) — **next action for whoever picks this up is
`docs/Build and Run.md`'s manual verification walkthrough, steps 1–6, on a real or portable Playnite.**

| Phase | Status |
|---|---|
| 8 — Scaffold + "hello world" load | ✅ Built 2026-09-15 — `dotnet build` clean, packed to a real `.pext`. **Not yet confirmed to actually load in a running Playnite** (see above). |
| 9 — Local API client + settings page | ✅ Built 2026-09-15 — `LocalApiClient`, settings page with Agent URL / State dir + a "Test connection" check. Not yet run against a live agent from inside Playnite itself. |
| 10 — Core pre-launch/post-exit gate | ✅ Built 2026-09-15 — `OnGameStarting`/`OnGameStopped`, `ActivateGlobalProgress`, the "this device / the cloud" resolve window. Not yet hardware-verified — needs a real seeded conflict (`Build and Run.md` step 4). |
| 11 — Automatic matching chain | ✅ Built 2026-09-15 — Steam AppID → InstallDir → name/Alias. **Required a small, additive companion change in the main repo** (below) — InstallDir was never exposed over the local API before this session. |
| 12–16 | ⏳ Not started (Groups 4–6) — see the main repo's `implementation-grouping.md`. |

## What actually got built this session

- Full repo bootstrap: `extension.yaml`, `net462` SDK-style csproj referencing the real installed
  `Playnite.SDK.dll` (not NuGet), and eight source files — `SaveLockerPlugin`, `GameMatcher`,
  `LocalApiClient`, `Contracts`, `Json`, `SaveLockerSettings` (+ ViewModel), `SaveLockerSettingsView`,
  `ConflictResolveWindow`, `PropertyChangedBase`. See [[REPO_MAP]] for what each does.
- **Every Playnite SDK signature used here was confirmed by reflecting the actual installed
  `Playnite.SDK.dll` (v6.16.0.0) from PowerShell** — `Assembly.LoadFile` +
  `GetMethods`/`GetProperties`/`GetConstructors` — rather than trusted to the official docs, which
  turned out to be thin on exact signatures for `GenericPlugin`/`ISettings`/`IDialogsFactory`. Worth
  repeating this check if a future session hits a signature mismatch after a Playnite update.
- `dotnet build src\SaveLocker.Playnite.csproj -c Release` **built clean on the first real attempt**
  — the toolchain risk `plan.md` flagged as this whole plan's highest uncertainty (first net462 +
  Playnite SDK + WPF build in this environment) did not materialize for the build step itself.
- `Toolbox.exe pack` **is broken on this dev box** (missing `NLog.dll` — not caused by this plugin;
  confirmed the file is genuinely absent from the Playnite install). Worked around by hand-zipping
  `extension.yaml` + the DLL into a `.pext` (confirmed: a `.pext` IS just a renamed zip). Produced a
  real, installable `dist\SaveLocker.pext`. Full detail: [[Gotchas]].
- **Companion change in the main `SaveLocker` repo** (not this one): `TrackedGameDto` never exposed
  `InstallDir` over the local API even though the field has existed agent-side since Phase 2 —
  `plan.md`'s own matching design assumed it was already there and wasn't. Added it (additive,
  optional, defaults null) on branch `claude/playnite-plugin-group-3-acd4eb` in the main repo,
  confirmed `Agent.Core` still builds clean, **committed locally on that branch but not pushed** —
  push it (or open a PR) before Phase 11's InstallDir tier can be exercised against a real agent.

## Not done, on purpose, this session

- **No hardware verification.** Nothing above has actually been launched inside Playnite, matched
  against a real game, or blocked on a real conflict. `docs/Build and Run.md` has the full manual
  walkthrough — do that before trusting any of this.
- **No XAML.** Both WPF views are built in plain C# to remove one more unknown from the riskiest
  phase — a deliberate simplification, not a discovered limitation. See [[Gotchas]].
- **Tier 4 of matching (the "couldn't match, link it" nudge)** is not built — it depends on Phase
  12's picker, which is Group 4, not this one. An unmatched game today behaves exactly like an
  untracked one: no gate, no nudge, no error.
- **No status chip, no self-update consumption, no test infra (`testenv` integration)** — Phases
  13–15, Groups 5–6, genuinely not started.

## Next action

1. Run `docs/Build and Run.md`'s manual verification, steps 1–6, against a portable Playnite +
   test agent (or, once confident, the real installed one).
2. Push (or PR) the `InstallDir` change on `claude/playnite-plugin-group-3-acd4eb` in the main repo
   — Phase 11's second matching tier is silently a no-op without it.
3. Once verified: update this file, move today's write-up into `docs/logs/`, and update the Group 3
   row in the main repo's `implementation-grouping.md` to `✅ Shipped`.
