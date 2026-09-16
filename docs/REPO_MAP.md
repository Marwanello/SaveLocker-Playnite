# SaveLocker-Playnite — Repo Map

Static layout. Update only when adding or removing entire files/modules — this is a single-project
repo, much smaller than the main `SaveLocker` repo's map.

```
SaveLocker-Playnite/
├── extension.yaml                  # Playnite's manifest — Id/Name/Author/Version/Module/Type.
│                                   #   Id must match SaveLockerPlugin.PluginId exactly (a GUID
│                                   #   string); Module must match the built DLL's filename.
├── src/
│   ├── SaveLocker.Playnite.csproj  # net462, SDK-style. References Playnite.SDK.dll from the real
│   │                               #   installed Playnite (HintPath), not NuGet — docs/Gotchas.md.
│   ├── SaveLockerPlugin.cs         # GenericPlugin entry point. OnGameStarting (pre-launch gate),
│   │                               #   OnGameStopped (post-exit push), GetSettings/GetSettingsView,
│   │                               #   GetGameMenuItems (Link to SaveLocker / Sync now / Resolve
│   │                               #   conflict…, Phase 13), OnApplicationStarted's self-update check
│   │                               #   (Phase 14, GET /api/playnite-plugin — surfaces a restart notice
│   │                               #   when the agent has a newer package waiting). Holds no sync
│   │                               #   rules — calls LocalApiClient and acts on the LaunchDecision it
│   │                               #   gets back, fail-open except on Blocked.
│   ├── GameMatcher.cs              # Phase 11: Steam AppID → InstallDir → name/Alias chain.
│   ├── GameStatusControl.cs        # Phase 13's GetGameViewControl: status chip (Not linked / Agent
│   │                               #   offline / Not synced yet / In sync / Conflict) + one action
│   │                               #   button. Theme-dependent — see its own doc comment.
│   ├── LinkAction.cs               # The "click and link" chain shared by GameStatusControl and the
│   │                               #   right-click menu: try automatic match/enroll first, fall back
│   │                               #   to LinkToSaveLockerWindow only when nothing resolves.
│   ├── LinkedTag.cs                # Marks a linked game with a "SaveLocker: Linked" Tag — the one
│   │                               #   theme-independent per-game visual hook the SDK actually has.
│   ├── SyncNowAction.cs            # "Sync now": runs the same pre-launch-sync gate OnGameStarting
│   │                               #   does, as a toast-driven on-demand action instead of a launch
│   │                               #   block. Shared by LinkAction's post-link offer, GameStatusControl,
│   │                               #   and the right-click menu.
│   ├── ConflictResolver.cs         # The interactive "this device / the cloud" flow shared by
│   │                               #   OnGameStarting (blocking), SyncNowAction/GameStatusControl/the
│   │                               #   right-click menu (non-blocking async overload).
│   ├── LocalApiClient.cs           # HttpClient wrapper for the agent's local API (:5178). Reads
│   │                               #   the X-SaveLocker-Token from <StateDir>\api-token per call.
│   ├── Contracts.cs                # Plain POCOs mirroring the agent's DTOs, each with a
│   │                               #   FromJson(IDictionary<string,object>) factory.
│   ├── Json.cs                     # JavaScriptSerializer-backed untyped JSON helper — every field
│   │                               #   is pulled by its exact camelCase key, nothing automatic.
│   ├── SaveLockerSettings.cs       # Settings POCO (AgentUrl, StateDir) + SaveLockerSettingsViewModel
│   │                               #   (ISettings: BeginEdit/CancelEdit/EndEdit/VerifySettings, plus
│   │                               #   the "Test connection" check the settings view calls).
│   ├── SaveLockerSettingsView.cs   # Settings page UserControl, built in code (no XAML/BAML).
│   ├── ConflictResolveWindow.cs    # The "this device / the cloud" resolve dialog shown on a
│   │                               #   confirmed Blocked decision. Native WPF, no WebView2.
│   ├── ConflictResolveWindowFullscreen.cs # Fullscreen-mode counterpart — different theme keys,
│   │                               #   chromeless full-shell overlay, controller-friendly.
│   ├── LinkToSaveLockerWindow.cs   # Phase 12: the "Link to SaveLocker" five-tier enroll/link popup —
│   │                               #   search tracked games, automatic lookup, manifest search,
│   │                               #   manual folder browse (native SelectFolder, no WebView2), pick
│   │                               #   an existing tracked game. Opened from the Tier-4 nudge only.
│   ├── NudgeState.cs               # Flat one-Guid-per-line file tracking which Playnite games have
│   │                               #   already been offered the Tier-4 link nudge, once ever.
│   └── PropertyChangedBase.cs      # Minimal INotifyPropertyChanged helper (SDK ships no
│                                   #   ObservableObject base in this version — checked by reflection,
│                                   #   docs/Gotchas.md).
├── dist/SaveLocker.pext            # Hand-packed (Toolbox.exe is broken here — docs/Gotchas.md).
│                                   #   Gitignored; rebuild with docs/Build and Run.md's pack step.
├── tests/SaveLocker.Playnite.Tests/ # Phase 15: xUnit, net462. GameMatcherTests (pure logic, no
│   │                               #   Playnite host needed — the Steam-AppID tier is the one
│   │                               #   deliberate gap, see its own doc comment) and
│   │                               #   LocalApiClientTests (an HttpListener-backed stub of the
│   │                               #   agent's local API — token header, JSON parsing, the
│   │                               #   tolerateConflict 409 split). `dotnet test
│   │                               #   tests\SaveLocker.Playnite.Tests\SaveLocker.Playnite.Tests.csproj`.
│   │                               #   Needs the real installed Playnite too (Playnite.SDK.dll,
│   │                               #   same as `src/`) — this is the automated half of Phase 15;
│   │                               #   the manual/hardware half is docs/Build and Run.md's own
│   │                               #   walkthrough, which nothing here replaces.
├── scripts/Install-ToPortable.ps1  # Build + copy straight into <PlaynitePath>\Extensions\SaveLocker
│                                   #   for any Playnite install root, portable or real — skips the
│                                   #   .pext pack/install round trip. docs/Build and Run.md.
├── docs/                           # This small vault. CONTEXT.md + REPO_MAP.md + Gotchas.md +
│                                   #   Build and Run.md + logs/ — see AGENTS.md for when to read each.
├── .github/workflows/              # Empty for now — no CI here yet (nothing to build headlessly
│                                   #   verify beyond `dotnet build`; real verification is manual,
│                                   #   hardware-only, per the main repo's plan.md).
├── AGENTS.md · .agents/AGENTS.md · CLAUDE.md   # Agent instructions
├── LICENSE                         # PolyForm Noncommercial 1.0.0, same as SaveLocker/SaveLocker-Decky
└── README.md
```

## What's NOT here

The actual phase-by-phase design (the full local-API contract, the matching design, the step-by-step
UX, the size estimate) lives in the main `SaveLocker` repo's `docs/tasks/playnite-plugin/plan.md` and
`implementation-grouping.md` — this repo does not duplicate it. Read those first.

## Build identity

- **Extension GUID**: `4d7017e5-87c0-4011-92c4-83f5dde2ada2` (both `extension.yaml`'s `Id` and
  `SaveLockerPlugin.PluginId` — must stay identical or Playnite won't associate the loaded assembly
  with its manifest entry).
- **Target framework**: `net462`, matching Playnite's own SDK target exactly.
- **Playnite SDK version this was built/reflected against**: `6.16.0.0` (installed at
  `%LocalAppData%\Playnite` on the dev box this was written on) — see `docs/Gotchas.md` if a newer
  Playnite changes a signature this code relies on.
