# SaveLocker-Playnite — Build and Run

## Build

```powershell
dotnet build src\SaveLocker.Playnite.csproj -c Release
```

Needs Playnite actually installed (`Playnite.SDK.dll` is referenced from
`%LocalAppData%\Playnite`, not NuGet — `docs/Gotchas.md`). Output lands at
`src\bin\Release\net462\`, `extension.yaml` copied alongside the DLL automatically.

## Pack into a `.pext`

`Toolbox.exe pack` is broken on this dev box (missing `NLog.dll` — `docs/Gotchas.md`). Zip manually:

```powershell
$src = "src\bin\Release\net462"
New-Item -ItemType Directory -Force dist | Out-Null
Compress-Archive -Path "$src\extension.yaml", "$src\SaveLocker.Playnite.dll" -DestinationPath dist\SaveLocker.zip -Force
Rename-Item dist\SaveLocker.zip SaveLocker.pext -Force
```

## Install — fast path: `scripts/Install-ToPortable.ps1`

Builds and copies `extension.yaml` + the DLL straight into `<PlaynitePath>\Extensions\SaveLocker`
in one step — no `.pext` pack, no double-click install. Works against any Playnite install root,
portable or real; Playnite loads an unpacked extension folder exactly the same as a packed one.

```powershell
.\scripts\Install-ToPortable.ps1 -PlaynitePath C:\SaveLockerTest\Playnite
```

Close Playnite first if it's already running from that path — it locks the DLL while loaded, so a
reinstall over a live instance fails with a clear "close Playnite first" error. `-SkipBuild` reuses
the last build output; `-Configuration Debug` installs a Debug build. This is the fastest edit-build-
reinstall loop for the portable test instance below; the manual three ways in the next section are
what it's doing internally, spelled out for when you want the `.pext` itself (e.g. to hand someone
a file) or don't want to touch a script.

## Install — three manual ways

### A. Straight into your real, everyday Playnite (fastest, touches your real library)

```powershell
$ext = "$env:AppData\Playnite\Extensions\SaveLocker"
New-Item -ItemType Directory -Force $ext | Out-Null
Copy-Item src\bin\Release\net462\extension.yaml, src\bin\Release\net462\SaveLocker.Playnite.dll $ext -Force
```

Restart Playnite. This is safe to do on your real install — the plugin makes **zero** changes to
your library, and until the agent is running and a game is actually tracked, `OnGameStarting` does
nothing. Uninstall the same way any other extension is removed: Playnite → Add-ons → SaveLocker →
Uninstall (or just delete the `SaveLocker` folder above and restart).

### B. Double-click the `.pext`

Playnite registers itself as the `.pext` handler; double-clicking `dist\SaveLocker.pext` shows
Playnite's own install prompt. Functionally identical to (A).

### C. A portable Playnite test instance (recommended for anything beyond "does it load")

Keeps every test — including a deliberately seeded conflict — off your real synced fleet and your
real library. See the next section for the full setup.

## Testing against a portable Playnite + a test agent

Nothing here is `testenv`-automated yet (that's Phase 15, Group 5, not built) — this is the manual
equivalent, step by step. Two things are isolated from your real setup: the **Playnite instance**
(your real library/settings/accounts copied in, so matching behaves realistically) and the
**SaveLocker agent it talks to** (a throwaway test one, so nothing touches your real synced saves).

### 1. Set up portable Playnite with your real library copied in

Playnite's `.7z` release (not the installer `.exe`) is portable: extracted anywhere, it stores its
config/library/Extensions inside its own folder instead of `%AppData%`.

```powershell
$portable = "C:\SaveLockerTest\Playnite"
New-Item -ItemType Directory -Force $portable | Out-Null

# Download the latest release .7z (check https://github.com/JosefNemec/Playnite/releases for the
# current filename — it changes every version, e.g. 10.60.7z) and extract it to $portable using
# 7-Zip, or any archive tool that reads .7z (Windows' own Explorer does not, by default).

# Copy your REAL profile in — everything portable mode reads from its own folder instead of AppData:
Copy-Item "$env:AppData\Playnite\*" $portable -Recurse -Force -Exclude "browsercache","cache"
```

That copies your real `config.json` (accounts, settings), `library\` (every installed/tracked game
Playnite knows about), `Extensions\`/`ExtensionsData\` (your other add-ons), and `Themes\`/`Backup\`.
`browsercache`/`cache` are excluded on purpose — large, regenerated automatically, not needed.
**This is a copy, not a move or a sync** — your real `%AppData%\Playnite` is untouched, and nothing
you do in the portable copy (including a deliberately seeded conflict) can write back to it.

Run it: `$portable\Playnite.DesktopApp.exe`. Confirm it's actually portable —
Settings → About should show a portable-mode indicator, and a new `config.json`/`library\` should
appear directly under `$portable`, not under `%AppData%\Playnite`.

Install the plugin into it:

```powershell
.\scripts\Install-ToPortable.ps1 -PlaynitePath $portable
```

(equivalent to method A above, but into `$portable\Extensions\SaveLocker\` instead of
`%AppData%\Playnite\Extensions\SaveLocker\`, and it builds for you first).

### 2. Start a throwaway test server + a test Windows agent

From the main `SaveLocker` repo checkout (a sibling of this one):

```powershell
# Terminal 1 — a scratch server, isolated storage:
cd ..\SaveLocker\src\Server
$env:Storage__DbPath = "C:\SaveLockerTest\server\test.db"
$env:Storage__ArchiveRoot = "C:\SaveLockerTest\server\archives"
$env:ASPNETCORE_URLS = "http://localhost:5199"
dotnet run
```

```powershell
# Terminal 2 — a second, TEST Windows agent, isolated state + port (never the real installed one):
cd ..\SaveLocker\src\Agent\bin\Debug\net10.0-windows
$env:SAVELOCKER_STATE_ROOT = "C:\SaveLockerTest\agent-state"
$env:SAVELOCKER_TRAY_PORT = "5177"
.\SaveLocker.Agent.exe set-server --url http://localhost:5199
.\SaveLocker.Agent.exe register --name "PlayniteTestPC"
# Then run it as a daemon so the local API (:5177) and process watcher stay up:
.\SaveLocker.Agent.exe
```

(Build the agent first if you haven't: `dotnet build src\Agent\SaveLocker.Agent.csproj --no-incremental`
from the main repo root.)

### 3. Point the plugin at the test agent

In the portable Playnite instance: Add-ons → SaveLocker → Settings — set **Agent URL** to
`http://127.0.0.1:5177` and **State directory** to `C:\SaveLockerTest\agent-state`. Click
**Test connection**; it should report "Connected — PlayniteTestPC, 0 game(s) tracked".

### 4. Track a real, small game for the test

Pick something small and disposable-feeling from your copied library (a tiny indie game, not your
main save file you care about). From the same agent terminal:

```powershell
.\SaveLocker.Agent.exe add-game --name "Your Game Name" --dir "C:\path\to\its\save\folder"
```

## Manual verification, step by step

With the portable Playnite + test agent from above running:

1. **Untracked game, baseline** — launch any game in the portable Playnite that is NOT tracked.
   Expect: launches immediately, no dialog, nothing in the agent log about it. Confirms the plugin
   is inert for anything it hasn't matched — the same as no plugin installed at all.

2. **Tracked game, no conflict (the common case)** — launch the game you tracked in step 4 above.
   Expect: a brief "SaveLocker: checking for a newer save…" progress dialog (often too fast to
   really see on localhost), then the game launches normally. Check the agent's log
   (`C:\SaveLockerTest\agent-state\agent.log`) for a `pre-launch-sync` line.

3. **Lease held elsewhere (`ProceedSyncPaused`)** — while the game from step 4 is checked out by
   another sync in progress (or fake it: acquire a lease against the test server directly via
   `POST http://localhost:5199/api/games/{id}/lease` with the machine's API key — see
   `docs/API Reference.md` in the main repo), launch the game again. Expect: it launches anyway, and
   a Playnite notification appears naming the holder machine.

4. **A genuine, confirmed conflict — the one case that must block.** Simulate a second machine on
   the same box, the same way the main repo's own `tests/seed-test-conflict.sh` does on Linux —
   a second config/port/identity against the SAME test server:

   ```powershell
   # Terminal 3 — a second machine identity, same test server, different state/port:
   cd ..\SaveLocker\src\Agent\bin\Debug\net10.0-windows
   $env:SAVELOCKER_STATE_ROOT = "C:\SaveLockerTest\agent-state-2"
   $env:SAVELOCKER_TRAY_PORT = "5176"
   .\SaveLocker.Agent.exe set-server --url http://localhost:5199
   .\SaveLocker.Agent.exe register --name "OtherPC"
   .\SaveLocker.Agent.exe add-game --name "Your Game Name" --dir "C:\SaveLockerTest\other-save-copy"
   # Edit a file under that folder, then:
   .\SaveLocker.Agent.exe push
   ```

   Now edit the save under the FIRST machine's tracked folder too (the one from step 4) without
   pulling first, and launch the game through the portable Playnite. `pre-launch-sync` runs a
   commit-before-choose push, sees the genuine divergence, and this time expect: the progress dialog
   is replaced by the **"This device / The cloud"** resolve window, and **the game does not start**
   until you pick a side or cancel. Pick a side → the window closes → the game launches. Cancel →
   back to the library, nothing changed, nothing launched. Confirm in the agent log and (if the test
   server has a console attached) the dashboard that the conflict is now resolved.

5. **Post-exit push** — close the game from step 2. Expect no interruption (it's non-blocking by
   design); check the agent log for a `post-exit-sync` line shortly after.

6. **Agent not running at all — the fail-open case.** Stop the test agent entirely, then launch the
   tracked game. Expect: it launches immediately, no delay, no error dialog. This is the single most
   important thing to confirm before trusting this on a real library — SaveLocker must never be why
   a game won't start.

## Cleanup

Stop both agent processes and the test server (Ctrl+C in each terminal), then delete
`C:\SaveLockerTest\` entirely — nothing under it is real state. Your actual installed Playnite,
its real library, and your real SaveLocker agent (`%ProgramData%\SaveLocker`, port `:5178`) were
never touched by any of the above.
