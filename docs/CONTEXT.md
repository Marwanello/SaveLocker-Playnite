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

## Status — Group 6, Phase 16 prep done 2026-09-16/17 (branch `playnite-plugin-group-6`), PR NOT opened

Asked directly: prepare the `JosefNemec/PlayniteAddonDatabase` submission, but verify first and don't
open the PR yet. Full write-up: `docs/logs/2026-09-16_group-6-phase-16-prep.md`.

`docs/addon-submission/addon-manifest.yaml` (the primary listing, still staged — not yet submitted
anywhere) was built against the addon database's own README (fetched directly, not from memory) and
cross-checked against a real merged entry for exact formatting. Its `InstallerManifestUrl` pointed at
`.../master/installer.yaml` in the first draft — **wrong**, this repo's default branch is `main`, not
`master` (confirmed via `gh repo view`); fixed before anything was submitted.

**A real `v0.1.0` tag was pushed and Phase 17's workflow ran for real on 2026-09-16** (run
`35151589595`, `success`, 1m19s) — the first genuine exercise of that workflow. Confirmed by
downloading the actual release and checksumming it: `SaveLocker.zip: OK` against `SHA256SUMS.txt`. A
real `installer.yaml` now lives at this repo's root with the real values (`Version: 0.1.0`,
`ReleaseDate: 2026-09-16`, the real `PackageUrl`) — no longer a draft. `RequiredApiVersion: 6.17.0` is
not a guess: downloaded Playnite's own current release (10.60) and read `Playnite.SDK.dll`'s real
`FileVersion` directly (a different number space from the app's own 10.60 — confirmed, not assumed).

**Phase 18/19 also shipped 2026-09-16 in the main `SaveLocker` repo** (agent-side, no changes to this
repo): a `PlayniteLibrary.cs` LiteDB reader (`GameScanner`'s fourth broad-sweep source, so a
Playnite-only game surfaces in Add Games without this plugin ever being installed) and an `agent-ui`
"Playnite plugin" suggest/install card. Branch `claude/playnite-group6-phase17-82e72b`. Unrelated to
this repo's own build, mentioned here only so the next session doesn't re-derive that they exist.

**What actually still blocks the PR now — not the release anymore, that part is done and verified:**
this plugin has never been loaded into a real, running Playnite (see Group 5's and Group 3's own notes
below — "builds clean" is the status of every phase, hardware-verified is the status of none since
Group 3). The add-on database's own bar is "submit something that functions, not a scaffold" — a
listing this plugin has never actually been proven to work from inside Playnite would not honestly
clear that bar yet, tag or no tag.

**Whoever picks this up next:**
1. Hardware-verify the plugin end to end against a real portable Playnite + test agent — the same
   gate Group 5's own notes below already call out, now the ONLY remaining one for Phase 16.
2. (Optional but worth doing first) Exercise the agent's self-update path against this real v0.1.0
   release — host it in the main SaveLocker server's Agent Updates config
   (`playnite-plugin` row) and confirm `PlaynitePlugin.CheckAsync`/`InstallFirstTimeAsync` can
   actually fetch, verify and install it. Nothing has exercised `InstallAsync` against a real package
   yet (Phase 7's own long-standing caveat).
3. Fork `JosefNemec/PlayniteAddonDatabase`, add `addon-manifest.yaml`'s content as
   `addons/generic/Marwanello_SaveLocker.yaml`, open the PR.

## Status — Group 5 (Phases 13-15+17) built 2026-09-16, not yet hardware-verified

`implementation-grouping.md`'s Group 5 — "status surface + self-update + test infra + release CI" —
is implemented end to end and builds clean, but nothing in it has run inside a real, live Playnite yet.

- **Phase 13 (status chip + action buttons) — built, then removed as confirmed dead code.**
  `GameStatusControl` originally replaced Group 4's binary Link/Synced `LinkStatusButton` with a real
  status surface: Not linked / Agent offline / Not synced yet / In sync / Conflict, each with one
  contextual action button. A user report of no chip under Playnite's stock **Default** theme (not
  just Harmony, which had been assumed the one exception) prompted checking Playnite's own source
  directly (`ControlTemplateTools.InitializePluginControls`): `GetGameViewControl` only ever fires for
  a plugin that both registers via `AddCustomElementSupport` (SaveLocker never has) *and* whose active
  theme's XAML names a matching `ContentControl` slot for it — true of no stock theme, Default
  included. There is no code fix for that; it would need a theme author to add the slot, or SaveLocker
  to ship its own theme. Neither is realistic right now, so `GameStatusControl.cs` and the
  `GetGameViewControl` override were removed outright (commit `5498ee7`) rather than kept as permanent
  dead code. `GetGameMenuItems`'s "Sync now"/"Resolve conflict…"/"Link to SaveLocker" and the
  `SaveLocker: Linked` Tag remain — both are genuinely theme-independent. **One deviation from
  `plan.md` that still applies to "Sync now," worth knowing before anyone goes looking for separate
  Push/Pull buttons**: the agent's local API has no per-game push-only or pull-only route — only
  `pre-launch-sync` (push-then-pull-or-block, wrapped as "Sync now" by Group 4's `SyncNowAction`) and
  `post-exit-sync` (fires automatically, not a button's job). Adding new agent-side routes was judged
  out of this group's plugin-side scope, so one "Sync now" item covers both halves instead of two.
- **Follow-up (commit `c3273be`): dialog-based feedback for "Sync now"/"Resolve conflict…".** Per an
  explicit UX request, both menu items now always open a real dialog stating the outcome instead of a
  notification (which the user pointed out gives no instant feedback): "Sync now" on an unlinked game
  shows a "'{game}' isn't linked to SaveLocker yet." dialog with Link/Cancel buttons (Link runs the
  same auto-match/enroll chain as "Link to SaveLocker"); "Resolve conflict…" shows the same dialog when
  unlinked, "No conflicts found for '{game}'" when linked with nothing open, or the real resolve window
  when a conflict exists; agent-unreachable is now a dialog too (shared by both items). Uses
  `IDialogsFactory.ShowMessage(..., List<MessageBoxOption>)` with `MessageBoxOption(title, isDefault,
  isCancel)` for the literal Link/Cancel pair — confirmed via reflecting `Playnite.SDK.dll` directly.
- **Phase 14 (plugin-side self-update consumption).** New agent-side `GET /api/playnite-plugin`
  (main repo, commit `31f8b9b` on `claude/group-5-playnite-plugin-3d3aae` — **not yet merged to
  main**, needs a PR) mirrors `/api/decky`'s shape but calls `PlaynitePlugin.CheckAsync(apply:false)`,
  since the answer genuinely depends on the server. `OnApplicationStarted` calls it once per session
  and surfaces a restart notice when the agent has a newer package waiting — no new branching needed,
  since a check running from inside a live Playnite process always hits `CheckAsync`'s own
  "close Playnite first" message. **Verified live**: the new route was exercised against a real
  scratch server + registered Windows agent and returned a correct `NotInstalled` status reflecting
  this box's actual (plugin-less) Playnite install. The plugin's own consumption of it has not been
  run inside a real Playnite session.
- **Phase 15 (test infrastructure).** New `tests/SaveLocker.Playnite.Tests` (xUnit, net462), 25/25
  passing. Turned out to be a smaller gap than `plan.md` sized it: the portable-Playnite `testenv`
  target and the Windows `seed-test-conflict` equivalent it also calls for were **already shipped** in
  the main repo's `9397e9d` ("Playnite plugin test-rig integration," 2026-09-15/16, before this
  session) — `testenv.ps1 build/up/status/clean -PlaynitePath ...` and `testenv.ps1 conflict -Windows
  -Wsl` already do that job. This session's actual gap was purely the "automated coverage stays cheap"
  half: `GameMatcherTests` (pure logic — InstallDir/name/Alias tiers, the ambiguous-tier-stops rule,
  `FindByPathOrName`, `MapStore`; **deliberately not** the Steam AppID tier, confirmed by reflection
  that `Game.Source` always resolves null outside a live Playnite database) and `LocalApiClientTests`
  (an `HttpListener` stub of the local API — token header, JSON parsing, the `tolerateConflict` 409
  split). `SaveLocker.Playnite.csproj` gained `<InternalsVisibleTo>` for the test assembly; no other
  production change.
- **Phase 17 (release CI workflow).** New `.github/workflows/release.yml` — this repo had **no**
  `.github/workflows` at all before this. On a `v*` tag: fetches `Playnite.SDK.dll` from Playnite's
  own portable `.7z` release (not on NuGet — confirmed by actually downloading the real 10.60 release
  and listing it with `7z l` that the DLL sits flat at the archive root before writing the extraction
  step around that fact), builds, packages `extension.yaml` + the DLL as both `SaveLocker.zip` and
  `SaveLocker.pext` (identical bytes), and publishes both plus `SHA256SUMS.txt`. **The filenames are
  load-bearing, not a style choice** — traced the main repo's `AgentInstallerService.cs` first:
  the `PlaynitePlugin` slot's asset filter requires a name starting with "SaveLocker" and ending
  `.zip`, and `VerifyHashAsync` looks up that exact filename inside a `SHA256SUMS*.txt` asset in the
  same release. **Verified further than "it parses"**: built this plugin locally against the
  freshly-downloaded Playnite SDK 6.17.0.0 (one minor ahead of this box's installed 6.16.0.0,
  via `-p:LocalAppData=<scratch>` so the real install was never touched) — 0 warnings, 0 errors.
  **Not run for real**: pushing an actual tag needs the user's go-ahead (a real GitHub Release, real
  Actions minutes) — not done in this session.

**Not done, on purpose, this session**: no hardware verification at all (nothing above has been
loaded into a running Playnite); Phase 16 (add-on database submission) is untouched, per
`implementation-grouping.md`'s own reasoning for keeping it last and separate.

**Branch: `playnite-plugin-group-5`, PR opened.** Seven commits: `77ab148` (Phase 13), `09a7532` (14),
`3f5352a` (15), `20e690d` (17), `dd6ae86` (docs), `c3273be` (dialog-based sync/conflict feedback
follow-up), `5498ee7` (removed the dead `GameStatusControl` status chip).

**Whoever picks this up next:**
1. Hardware-verify against a real portable Playnite + test agent: the right-click menu items and their
   new dialogs (Phase 13 + the `c3273be` follow-up), the restart notice actually appearing when a
   plugin update is deliberately staged server-side (Phase 14).
2. Push a real `v*` tag once ready to actually exercise Phase 17's workflow end to end, and confirm
   the agent's self-updater can fetch and verify what it produces — the one piece of this whole plugin
   that has never been tested against a real release.
3. Group 6 (Phase 16, add-on database submission) is next per `implementation-grouping.md`'s
   recommended order, once Group 5 is hardware-verified.

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
notification, or walked through enrolling/linking against a real test agent. `docs/Build and Run.md`
now has a full Phase 12 manual-verification section (steps 7–14, right after the existing Phase 8–11
walkthrough) — nudge-fires-once, nudge-suppressed-while-agent-down, all four tiers, cancel-stays-
cancelled, and the alias-backfill check that confirms a linked game auto-matches on its next launch.
That checklist is written but **not yet run** — whoever picks this up next should actually run it
against a real portable Playnite + test agent.

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

1. Hardware-verify Group 4 by actually running `docs/Build and Run.md`'s new "Phase 12 manual
   verification" section (steps 7–14) against a real portable Playnite + test agent.
2. Re-run `docs/Build and Run.md`'s steps 3 (lease held elsewhere) and 7 (Fullscreen mode) from the
   Group 3 write-up below if they still haven't been reconfirmed since those fixes.
3. Once Group 4 is hardware-verified: update `implementation-grouping.md`'s Group 4 row in the main
   repo to `✅ Done`, and move this session's write-up into `docs/logs/`.
4. Group 5 (Phases 13–15 + 17 — status chip/buttons, self-update consumption, test infra, release CI)
   is next per `implementation-grouping.md`'s recommended order.

Already done in the Group 3 session: the `InstallDir` change is pushed and PR'd
(`Marwanello/SaveLocker#38`); `implementation-grouping.md`'s Group 3 row in the main repo is marked
`✅ Done`; that session's plugin PR is open (`Marwanello/SaveLocker-Playnite#1`).
