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

## Status — Group 4 (Phase 12) built 2026-09-15, not yet hardware-verified

The "Link to SaveLocker" enroll/link popup (`LinkToSaveLockerWindow.cs`) is implemented end to end —
all five tiers (search tracked games, automatic manifest lookup, manual manifest search, manual
folder browse via Playnite's own native `IDialogsFactory.SelectFolder()`, pick an existing tracked
game), plus the Tier-4 "couldn't automatically match" nudge notification (`SaveLockerPlugin
.MaybeShowLinkNudge`, `NudgeState.cs`) that's the only entry point into it today — the right-click
menu entry point is Phase 13/Group 5, not built here.

**Builds clean against the real installed Playnite SDK on this box** (`dotnet build` — 0 warnings, 0
errors — and packed to a real `dist\SaveLocker.pext`). **Not yet hardware-verified**: nothing above has
actually been loaded into a running Playnite, matched a real unmatched game, fired the nudge
notification, or walked through enrolling/linking against a real test agent. `docs/Build and Run.md`'s
manual walkthrough doesn't yet have a Phase-12-specific step — whoever picks this up next should add
one (seed an untracked, unmatched game in the portable Playnite + test agent setup, launch it, confirm
the nudge fires once and not again on a second launch, click it, and walk all five tiers).

One deliberate deviation from `plan.md`'s own suggestion, worth knowing: the manual-folder-browse tier
uses Playnite's native `IDialogsFactory.SelectFolder()` instead of embedding the agent-ui Add Games
view in a WebView2 popup — confirmed via the Playnite SDK docs that `SelectFolder()` exists and does
exactly what's needed, so this avoids a new dependency (WebView2 NuGet + runtime, copy-local packing)
entirely in a project whose own `docs/Gotchas.md` already treats minimizing dependencies as a value.

## Status — Group 3 (Phases 8–11) shipped and hardware-verified, 2026-09-15

`implementation-grouping.md`'s Group 3 — "the spine: scaffold, settings, core gate, matching" — is
implemented end to end and has been run against a real, running portable Playnite (Harmony theme):
the plugin loads, matches a real Steam-installed game via AppID, blocks on a genuine seeded conflict,
resolves it through a theme-driven window, pushes on exit, and fails open when the agent is down.

**Not yet re-confirmed on hardware, both fixed late in the same session:**
- The Fullscreen-mode resolve window (`ConflictResolveWindowFullscreen.cs`) — Desktop mode's window
  was confirmed unstyled/non-controller-friendly there and replaced; the replacement builds clean but
  hasn't been retested in Fullscreen mode yet, including whether a real controller drives it.
- The "lease held elsewhere" (`ProceedSyncPaused`) manual test case — blocked earlier by a lease-test
  script that was authenticating as the wrong machine; fixed, not yet re-run.

Whoever picks this up next: re-run `docs/Build and Run.md`'s step 7 (Fullscreen) and step 3 (lease)
specifically — the rest of the 6-step walkthrough is already confirmed.

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

1. Hardware-verify Group 4: set up (or reuse) the portable Playnite + test agent from `docs/Build and
   Run.md`, seed an untracked game with no automatic match, launch it once to confirm the nudge fires
   (and doesn't fire again on a second launch), then walk all five tiers of the popup against a real
   test agent — automatic lookup resolving, manifest search finding a renamed title, manual folder
   browse, and picking an existing tracked game.
2. Re-run `docs/Build and Run.md`'s steps 3 (lease held elsewhere) and 7 (Fullscreen mode) from the
   Group 3 write-up below if they still haven't been reconfirmed since those fixes.
3. Once Group 4 is hardware-verified: update `implementation-grouping.md`'s Group 4 row in the main
   repo to `✅ Done`, and move this session's write-up into `docs/logs/`.
4. Group 5 (Phases 13–15 + 17 — status chip/buttons, self-update consumption, test infra, release CI)
   is next per `implementation-grouping.md`'s recommended order.

Already done in the Group 3 session: the `InstallDir` change is pushed and PR'd
(`Marwanello/SaveLocker#38`); `implementation-grouping.md`'s Group 3 row in the main repo is marked
`✅ Done`; that session's plugin PR is open (`Marwanello/SaveLocker-Playnite#1`).
