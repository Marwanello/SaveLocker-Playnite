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
3. Verify per `docs/Build and Run.md` (build/tests) and the phase's own hardware-verification note —
   CI only proves compile + unit tests; nothing but a real Playnite proves the plugin loads and its
   hooks fire.
4. Stop and report — do not continue to the next phase unless instructed.
5. One commit per phase successfully completed.
6. Update the status table in the main repo's `plan.md`/`implementation-grouping.md` the same
   session a phase ships — do not let it go stale (that repo's own `CLAUDE.md` convention).

## Session handoff (end of session)

1. Update `docs/CONTEXT.md` — current status, any new gotchas, next action.
2. Add a dated write-up under `docs/logs/` for anything nontrivial this session did.
3. Commit with a `Docs:` prefix commit message.

## Build, test, install (exact)

```powershell
dotnet build src\SaveLocker.Playnite.csproj -c Release
dotnet test tests\SaveLocker.Playnite.Tests\SaveLocker.Playnite.Tests.csproj -c Release
```

- Both need a real Playnite install on this machine: `Playnite.SDK.dll` is referenced via the
  csproj `%LocalAppData%\Playnite` HintPath, not NuGet. There is no local-only build path.
- `Toolbox.exe pack` is broken on this dev box (missing `NLog.dll` — `docs/Gotchas.md`). Pack by
  hand-zipping `extension.yaml` + the built DLL via the `Compress-Archive` snippet in
  `docs/Build and Run.md`; release CI does the same thing.
- Fastest dev loop into a portable test instance:
  `.\scripts\Install-ToPortable.ps1 -PlaynitePath <root>` — skips the `.pext` pack/install round
  trip. Close Playnite first; it locks the loaded DLL.
- Hardware verification goes through the sibling main repo's `tests\testenv.ps1`
  (`build`/`up`/`status`/`clean -PlaynitePath ...`) — the standing instruction is to use it, not
  ad-hoc scratch servers.
- Tests touch nothing real: `GameMatcherTests` is pure logic, `LocalApiClientTests` talks to an
  in-process `HttpListener` stub. The Steam-AppID matcher tier is deliberately untested (needs a
  live Playnite database).

## CI and releases

- `ci.yml`: build + `dotnet test` on `main` pushes and PRs (fetches the SDK from Playnite's own
  portable `.7z`; locally that comes from the real install instead).
- `release.yml`, on a `v*` tag: stamps `extension.yaml`'s `Version:` from the tag, builds, and
  publishes `SaveLocker.zip` + `SaveLocker.pext` + `SHA256SUMS.txt`.
- Those release filenames are load-bearing: the agent's `AgentInstallerService` matches an asset
  starting `SaveLocker` ending `.zip` and verifies it against `SHA256SUMS.txt`. Don't rename.
- The in-repo `extension.yaml` stays `Version: 0.1.0` as the dev placeholder; the tag always wins
  in CI. Root `installer.yaml` (the add-on database's installer manifest) is hand-maintained and
  bumped after a release; `docs/addon-submission/` holds the not-yet-submitted listing.
- `extension.yaml`'s `Id` must stay identical to `SaveLockerPlugin.PluginId` (same GUID string) and
  `Module` must match the built DLL's filename, or Playnite won't associate the two.

## Coding conventions

- Target framework is **net462** (Playnite's own SDK target) — not this project's `net10.0` sibling
  repos. No `SaveLocker.sln` entanglement; this is its own solution/toolchain.
- `Playnite.SDK.dll` reference is `Private=false` in the plugin csproj (Playnite supplies it at
  runtime; a second copy risks a version mismatch) but `Private=true` in the test csproj (nothing
  supplies it to a standalone test host).
- The plugin holds no sync rules of its own. It calls the agent's local API and acts on the
  `LaunchDecision` it gets back; every rule (what counts as a conflict, when to pull, when to
  block) lives in `SyncEngine` in the main repo. Don't reimplement any of it client-side.
- Fail open, religiously. A transport error, timeout, or unexpected response must never read as a
  reason to block a launch — only an explicit `Blocked` decision may.
- No XAML: every WPF view is built in plain C#. Zero third-party dependencies ship in the plugin —
  JSON goes through the GAC's `JavaScriptSerializer`, untyped, every field pulled by its exact
  camelCase key (`Contracts.cs`).
- When an SDK signature matters, reflect the installed `Playnite.SDK.dll`
  (`Assembly.LoadFile` + `GetMethods` from PowerShell) instead of trusting the docs — they're thin,
  and this repo has been burned by signature assumptions before.
- No comments unless the WHY is non-obvious. No trailing summaries after diffs.

## Auth model (consumed, not owned)

- Local API on `http://127.0.0.1:5178` (URL and state dir configurable in the plugin's settings).
  Every request carries `X-SaveLocker-Token`, read from `<StateDir>\api-token` per call — never
  cache it across an agent restart.
- Real agent state dir is `%ProgramData%\SaveLocker`. Test rigs differ (testenv defaults: `:5188` +
  `%LOCALAPPDATA%\SaveLocker-test\SaveLocker`), and **both** the URL and state dir must be changed
  in settings — not just the URL.
- The agent side must be a build of the `Marwanello/SaveLocker` fork — every route this plugin
  calls exists only on fork branches, not upstream `SkorcherX/SaveLocker` (README's compatibility
  note).

## Search trap

`.claude/worktrees/` holds stale full checkouts from past sessions (excluded via
`.git/info/exclude`, not `.gitignore`). Exclude it from Grep/Glob or you'll search duplicated,
outdated copies of `src/`.
