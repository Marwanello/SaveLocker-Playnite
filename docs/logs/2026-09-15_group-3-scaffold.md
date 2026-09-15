# 2026-09-15 — Group 3: scaffold, settings, core gate, matching

Repo and branch just created this session; this is Group 3's first pass, not a continuation.

## What shipped

Phases 8–11 (`implementation-grouping.md`'s Group 3), all four built in one session:

- **Phase 8 — scaffold.** `extension.yaml` + `net462` SDK-style csproj, `SaveLockerPlugin :
  GenericPlugin`. `dotnet build` clean on the first real attempt; hand-packed a real `.pext`
  (`Toolbox.exe` itself is broken on this box — missing `NLog.dll`, see `docs/Gotchas.md`).
- **Phase 9 — local API client + settings page.** `LocalApiClient` (HttpClient + `JavaScriptSerializer`,
  no NuGet JSON dependency), settings page (Agent URL / State dir, "Test connection" check) built in
  plain C# rather than XAML.
- **Phase 10 — core gate.** `OnGameStarting` → `pre-launch-sync` inside `ActivateGlobalProgress`;
  `OnGameStopped` → `post-exit-sync`, fire-and-forget; `ConflictResolveWindow` for a confirmed
  `Blocked` decision — This device / The cloud, matching the convention
  `AgentApiServer.cs`'s own comment states (VersionB is always this machine, VersionA always the
  cloud head).
- **Phase 11 — matching.** Steam AppID (exact) → normalized InstallDir → name/Alias, in that order,
  per `plan.md`'s own priority chain.

## The one real gap found, not anticipated by the plan

`plan.md`'s matching design assumes `TrackedGame.InstallDir` is available to the plugin the same way
Steam AppID is — it wasn't. `TrackedGameDto` (the `/api/games` local-API response) never exposed it,
even though the field has existed agent-side since Phase 2. Fixed with a small, additive change in
the main repo (`TrackedGameDto` gains an optional `InstallDir`, defaults null) on branch
`claude/playnite-plugin-group-3-acd4eb` — committed there, not yet pushed.

## Every Playnite SDK signature here came from reflecting the real installed DLL

`Playnite.SDK`'s own docs (api.playnite.link) are thin on exact `GenericPlugin`/`ISettings`/
`IDialogsFactory` signatures. Rather than guess, reflected `%LocalAppData%\Playnite\Playnite.SDK.dll`
(v6.16.0.0) directly from PowerShell — `Assembly.LoadFile`, then `GetMethods`/`GetProperties`/
`GetConstructors` on `Plugin`, `GenericPlugin`, the `OnGame*EventArgs` types, `IDialogsFactory`,
`GlobalProgressOptions`/`GlobalProgressActionArgs`, `NotificationMessage`, `GameSource`, and
`IGameDatabase`. Confirmed along the way: `ISettings` is `IEditableObject` + `VerifySettings` only
(no `ObservableObject` base ships in this SDK version — hence this repo's own tiny
`PropertyChangedBase`), and `Game.Source.Name == "Steam"` really is the whole AppID-matching
condition the plan assumed.

## Not done

No hardware verification — nothing here has run inside an actual launched Playnite session yet. See
`docs/CONTEXT.md` → "Next action" and `docs/Build and Run.md`'s manual walkthrough.
