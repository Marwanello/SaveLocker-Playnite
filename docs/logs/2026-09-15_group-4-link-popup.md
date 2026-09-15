# 2026-09-15 — Group 4: "Link to SaveLocker" enroll/link popup

New worktree/branch off `origin/main` (which already carries Group 3's own fixes,
`fix-conflict-resolve-race-and-fail-open` / PR #2, merged). This session is Phase 12 only.

## What shipped

`LinkToSaveLockerWindow.cs` — the five-tier flow from `tasks/playnite-plugin/plan.md`'s "Link to
SaveLocker" section, as one state-machine window (screens swap `window.Content`, same convention as
`ConflictResolveWindow`):

1. **Search already-tracked games** — re-runs `GameMatcher.FindMatch` on demand.
2. **Automatic manifest lookup** — `POST /api/candidates/lookup` with Playnite's own name/InstallDir/
   SteamAppId/store.
3. **Manual manifest search** — `GET /api/manifest/search`, pre-filled with Playnite's title.
4. **Manual folder browse** — Playnite's own `IDialogsFactory.SelectFolder()`, then
   `POST /api/candidates/{id}/folder`. Deliberately not the plan's own suggested WebView2-embedded
   Add Games view — `SelectFolder()` does the same job with zero new dependencies.
5. **Pick an existing tracked game** — a filterable list, available from every other screen too (the
   plan's own "manual override always available" point).

Every enroll path also backfills `Alias` when the enrolled name differs from Playnite's title (e.g. a
manifest pick), so tier 3's name/Alias matching actually recognizes the game on the next launch —
otherwise this popup's own result would silently never be found again.

Also built: the Tier-4 nudge from `plan.md`'s "Automatic game matching" section, deferred from Group
3 because it needed this picker. `SaveLockerPlugin.MaybeShowLinkNudge` fires a dismissible
`NotificationMessage` (with a click action opening the popup) the first time `OnGameStarting` finds no
match for a game *and the agent was reachable* — `FindMatch` now returns whether it actually reached
the agent, since nudging when the agent is simply not running would be noise on every unmatched
launch. `NudgeState.cs` is a flat one-Guid-per-line file under the plugin's own data directory
(`GetPluginUserDataPath()`) tracking which games have already been offered it — shown once ever,
regardless of outcome, matching the plan's own "silently skippable forever" design.

New `LocalApiClient` methods: `EnrollAsync` (`POST /api/enroll`) and `SetCandidateFolderAsync`
(`POST /api/candidates/{id}/folder`) — the two routes Group 2 already shipped agent-side but this
plugin had no caller for yet. `Contracts.cs` gained `EnrollResult` and a `CandidateName` field on the
existing `CandidateLookupResult` (the candidate's own name — the manifest's canonical spelling for a
tier-3 pick, not necessarily Playnite's title).

## One deliberate deviation from `plan.md`, worth knowing

The plan's own text for the manual-browse tier says "reusing the existing agent-ui Add Games view in
a small WebView2 popup rather than rebuilding it natively in WPF." That was dropped in favor of
Playnite's native `IDialogsFactory.SelectFolder()` (confirmed via the Playnite SDK docs — takes an
optional initial directory, returns the picked path or null on cancel) once it was clear that method
exists and does exactly what's needed. Reasoning: WebView2 would be a genuinely new dependency in a
project whose own `docs/Gotchas.md` already treats "zero third-party dependencies to pack" as a value
worth defending (the same reasoning that chose `JavaScriptSerializer` over Newtonsoft/System.Text.Json
in Phase 9) — a native SDK call that already does the job outright avoids that cost for a single,
last-resort tier.

## A real, if minor, correctness note found while building this, not a bug

`SaveLockerPlugin.FindMatch` used to swallow "agent unreachable" and "reached the agent, no match"
into the same `null` return — fine for the pre-launch gate itself (both cases mean "no gate, launch
normally"), but wrong for deciding whether to nudge: nudging on every unmatched launch while the agent
is simply not running would fire constantly and teach players to ignore it. `FindMatch` now takes an
`out bool agentReachable` so `OnGameStarting` can tell the two cases apart.

## Not done

**No hardware verification** — nothing here has been loaded into a running Playnite, matched a real
game, fired the nudge, or walked through enrolling/linking against a real test agent. It does build
clean against the real installed Playnite SDK on this box (`dotnet build`, 0 warnings/errors) and
packs to a real `.pext`. See `docs/CONTEXT.md` → "Next action" and `docs/Build and Run.md` (which
doesn't yet have a Phase-12-specific manual-verification step — worth adding alongside the hardware
pass). Also not done, on purpose: the right-click menu entry point (`GetGameMenuItems`) is Phase
13/Group 5, so this popup is reachable only through the Tier-4 nudge for now.
