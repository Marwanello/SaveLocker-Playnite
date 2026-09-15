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

## Install — fastest path: the main repo's `tests/testenv.ps1`

If you also have the main `SaveLocker` repo checked out (a sibling of this one), its throwaway test
rig now knows about this plugin too — `build`/`up`/`clean` build, install and remove it alongside
the rig's own test console + Windows agent, exactly like it already does for the Decky plugin:

```powershell
cd ..\SaveLocker
.\tests\testenv.ps1 build -PlaynitePath C:\SaveLockerTest\Playnite
.\tests\testenv.ps1 up    -PlaynitePath C:\SaveLockerTest\Playnite
```

`up` starts the test console and a test Windows agent (port `:5188` by default — see
`tests\testenv.ps1`'s own `-WinPort`, not the `:5177` used further below in the fully-manual
walkthrough, which predates this integration and doesn't touch `testenv.ps1` at all) alongside
installing this plugin into `C:\SaveLockerTest\Playnite\Extensions\SaveLocker`. `clean` removes
just that folder, never the rest of the Playnite install. `-PlaynitePluginRepo` (or
`$env:SAVELOCKER_PLAYNITE_PLUGIN_REPO`) points it at a specific checkout/worktree of this repo if
the default sibling-directory guess (`..\SaveLocker-Playnite`) isn't the one you want built; set
`$env:SAVELOCKER_PLAYNITE_PATH` once to skip retyping `-PlaynitePath` every call. `tests\testenv.ps1
status` reports whether the plugin is currently installed at that path. You still need the
Add-ons → SaveLocker → Settings screen for Agent URL/State directory, the same as every other
install method below — `testenv.ps1` installs the plugin files, it doesn't write plugin settings.

## Install — fast path without the main repo: `scripts/Install-ToPortable.ps1`

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
   really see on localhost), then the game launches normally.

   **The real signal to check for is NOT a literal `pre-launch-sync` line — that text is never
   logged.** `pre-launch-sync` calls `SyncEngine.PrepareLaunchAsync`, which logs through the
   ordinary push/pull messages instead: `"[<game>] pushed new version."` / `"[<game>] no local
   changes since last sync."` (the commit-before-choose push), then `"[<game>] already up to date."`
   or a real pull line (the pull that follows it) — both landing in the same second as Playnite's
   own `"Starting <game>... Using plugin to start a game."` line in its own `playnite.log`.
   Cross-reference the two logs by timestamp for a confirmed read.
   **Do NOT look for `"running — lease taken, launch pull skipped"`** — that message is
   `SyncEngine.OnGameLaunchAsync`, the older reactive `ProcessWatcher` that runs independently of
   any launcher and fires on its own polling interval regardless of whether the Playnite plugin
   matched anything. Seeing it proves the process was noticed, not that the plugin's gate ran —
   confirmed by confusing the two while first verifying this on hardware 2026-09-15.

3. **Lease held elsewhere (`ProceedSyncPaused`)** — while the game from step 4 is checked out by
   another sync in progress (or fake it: acquire a lease against the test server directly via
   `POST http://localhost:5199/api/games/{id}/lease` with the machine's API key — see
   `docs/API Reference.md` in the main repo), launch the game again. Expect: it launches anyway, and
   a Playnite notification appears naming the holder machine.

4. **A genuine, confirmed conflict — the one case that must block.** Needs a real, launchable
   Playnite entry to click Play on — a placeholder game exe makes that possible without needing an
   actual title. See **"Conflict Game" fake exe**, below, for what it is and how to seed one; once
   its Playnite entry exists, the seeding itself is one command:

   ```powershell
   # From the main SaveLocker repo:
   .\tests\testenv.ps1 conflict -Windows -Wsl
   ```

   **Both flags are required.** `-Wsl` alone seeds only WSL's side — Windows never gets "Conflict
   Game" added locally, so the plugin's `FindMatch` finds nothing, its controller never engages,
   and Playnite silently falls back to a plain launch (no pause, no popup, easy to mistake for a
   plugin bug — confirmed by hitting exactly this on hardware 2026-09-15). `-Windows` on its own is
   fine too, seeding Windows as the side that discovers the divergence against whatever the server
   already holds — useful for reseeding just the Windows side after a `-Wsl`-only run. Neither flag
   given auto-picks Windows + WSL (or Windows + Deck if one is configured), which is equivalent to
   `-Windows -Wsl` here since no Deck is configured in this rig.

   This seeds a diverging save for "Conflict Game" on Windows (this rig's own test agent — the SAME
   one Playnite is pointed at) and on WSL as the second, disagreeing side — no manual
   second-machine-identity dance needed. The Windows test tray must also be running (`testenv.ps1
   up`, or `up -Only windows` if seeding stopped it) — the plugin's pre-launch check talks to it
   over the local API, and with it down `FindMatch` fails the same silent-fallback way described
   above. Launch "Conflict Game" from the portable Playnite:
   `PrepareLaunchAsync` runs its commit-before-choose push, sees the genuine divergence, and expect
   the progress dialog to be replaced by the **"This device / The cloud"** resolve window — **the
   game does not start** until you pick a side or cancel. Pick a side → the window closes → the fake
   exe launches. Cancel → back to the library, nothing changed, nothing launched. Confirm in the
   agent log and (if the test server has a console attached) the dashboard that the conflict is now
   resolved. Re-seeding needs a full `testenv.ps1 clean` first — see that command's own header
   comment in `tests/testenv.ps1`.

   Prefer full manual control over both sides instead (no `testenv.ps1`, or a specific machine name)?
   The equivalent by hand:

   ```powershell
   # Terminal 3 — a second machine identity, same test server, different state/port:
   cd ..\SaveLocker\src\Agent\bin\Debug\net10.0-windows
   $env:SAVELOCKER_STATE_ROOT = "C:\SaveLockerTest\agent-state-2"
   $env:SAVELOCKER_TRAY_PORT = "5176"
   .\SaveLocker.Agent.exe set-server --url http://localhost:5199
   .\SaveLocker.Agent.exe register --name "OtherPC"
   .\SaveLocker.Agent.exe add-game --name "Conflict Game" --dir "C:\SaveLockerTest\other-save-copy"
   # Edit a file under that folder, then:
   .\SaveLocker.Agent.exe push
   ```

   Then edit the save under the FIRST machine's tracked folder too, without pulling first, and
   launch "Conflict Game" through the portable Playnite the same as above.

   ### "Conflict Game" fake exe

   `SaveLocker.Agent.exe fake-game` (built as part of the ordinary Windows Agent build — no
   separate project) opens a small window — "Conflict Game is running" and an **Exit** button in
   the middle — standing in for a real game so this step has something real to click Play on and
   close. It's at:

   ```
   <main SaveLocker repo>\src\Agent\bin\Debug\net10.0-windows\SaveLocker.Agent.exe
   ```

   with the argument `fake-game`. Playnite has no CLI to add a library entry, so wiring it in is a
   **one-time manual step** (the entry persists across `testenv.ps1 clean`/re-seeding, since `clean`
   only wipes SaveLocker-test state, never Playnite's own library): in the portable Playnite,
   **Add game → Custom game**, then set
   - **Name**: `Conflict Game` (exact — this is what `GameMatcher`'s name/alias tier matches on)
   - **Executable**: the path above
   - **Arguments**: `fake-game`

   `.\tests\testenv.ps1 conflict` prints this same Name/Executable/Arguments after seeding the
   Windows side, so there's nothing to memorize.

5. **Post-exit push** — close the game from step 2 (or the fake exe's Exit button, for "Conflict
   Game"). Expect no interruption (it's non-blocking by design). Same caveat as step 2: check for an
   ordinary push line (`"pushed new version."` / `"no local changes since last sync."`) shortly
   after — there is no literal `post-exit-sync` line either, since that route also just calls into
   `SyncEngine.OnGameExitAsync`'s own `PushAsync`.

6. **Agent not running at all — the fail-open case.** Stop the test agent entirely, then launch the
   tracked game. Expect: it launches immediately, no delay, no error dialog. This is the single most
   important thing to confirm before trusting this on a real library — SaveLocker must never be why
   a game won't start.

## Phase 12 manual verification: "Link to SaveLocker"

Extends the exact same portable Playnite + test agent + test server setup from steps 1–4 above —
same `$portable`, same test agent on `:5177`, same test server on `:5199`. Nothing new to start,
just a handful of extra disposable Custom Game entries and two scratch folders, all still confined
to `C:\SaveLockerTest\`.

Every path below assumes the layout from earlier in this doc: the main `SaveLocker` repo checked
out at `D:\Projects\SaveLocker\SaveLocker` (adjust if yours lives elsewhere), the portable Playnite
at `C:\SaveLockerTest\Playnite`, and the test agent's state at `C:\SaveLockerTest\agent-state`.

### Reusable helper: reading tracked-game state directly

Several steps below want to see `Alias`/`SteamAppId`/`InstallDir` on a tracked game, which
`SaveLocker.Agent.exe list` doesn't print. Call the same local API the plugin itself calls, from a
plain PowerShell prompt (works from anywhere, not just the agent's own terminal):

```powershell
function Get-SaveLockerGames {
    param(
        [string]$AgentUrl  = "http://127.0.0.1:5177",
        [string]$StateDir  = "C:\SaveLockerTest\agent-state"
    )
    $token = (Get-Content (Join-Path $StateDir "api-token") -Raw).Trim()
    Invoke-RestMethod -Uri "$AgentUrl/api/games" -Headers @{ "X-SaveLocker-Token" = $token }
}

# Usage — run this any time you want to see what's actually tracked:
Get-SaveLockerGames | Format-Table id, name, alias, steamAppId, installDir, path -AutoSize
```

(`:5177` and `C:\SaveLockerTest\agent-state` are this doc's own test-agent defaults from step 2/3
above — match whatever you actually used if you changed them.)

### 7. Add the test Custom Game entries

Playnite still has no CLI to add library entries, so — same one-time manual step as "Conflict Game"
above — add each of these via **Add game → Custom game** in the portable Playnite
(`C:\SaveLockerTest\Playnite\Playnite.DesktopApp.exe`). The Custom Game editor has a **Name** field
at the top, an **Installation** tab (leave **Install directory** blank for all of these — a blank
`InstallDirectory` is exactly what a manually-added, never-installed-through-a-launcher entry
looks like, and it's fine: `GameMatcher`'s InstallDir tier just never fires for these, same as it
wouldn't for a real manually-added game), and an **Actions** tab where you add one Play Action:

```
Type:              File
Path:              D:\Projects\SaveLocker\SaveLocker\src\Agent\bin\Debug\net10.0-windows\SaveLocker.Agent.exe
Arguments:         fake-game
Working directory: (leave blank)
```

(Build the Windows Agent first if that path doesn't exist yet:
`dotnet build src\Agent\SaveLocker.Agent.csproj --no-incremental` from the main repo root.)

Create these four entries, changing only the **Name** field each time (Path/Arguments/Working
directory are identical for all of them — that's deliberate, so every entry is genuinely launchable
and closable through the same fake exe):

| # | Name (exact) | Tests |
|---|---|---|
| 1 | `Unmatched Test Game` | Tier-4 nudge → falls through to Tier 3/4 |
| 2 | `Second Unmatched Game` | The agent-down nudge-suppression case (step 9) |
| 3 | *(pick a real, already-installed, never-tracked-by-SaveLocker game you've actually played — see below)* | Tier 2 automatic resolve |
| 4 | *(the SAME real game as #3, added a second time under a nickname — e.g. `Civ 6` for "Sid Meier's Civilization VI")* | Tier 3 manifest search |
| 5 | `Totally Fake Game XYZ` | Tier 4 manual folder browse + the refusal check |
| 6 | `Nudge Rearm Test` | Re-triggering the popup for step 13 without disturbing #1's own nudge record |

Entries #3/#4 need something real, since Tier 2's automatic resolve only succeeds against an
actual Ludusavi manifest entry with an actual save folder already on disk. Pick something small you
already own that's not yet tracked by this SaveLocker install — its real save folder gets read, and
once you click Enroll, tracked and uploaded on the next sync, same spirit as step 4's "small and
disposable-feeling" pick. It needs: (a) not already tracked here, (b) played at least once so its
save folder exists on disk, (c) a title Ludusavi's manifest recognizes (most well-known titles are —
the manifest itself isn't in this repo, it's fetched at runtime from
[mtkennerly/ludusavi-manifest](https://github.com/mtkennerly/ludusavi-manifest); easiest is to just
try it — step 10 below tells you immediately whether it resolved, and if not, entry #4's manifest
search in step 11 lets you search that same manifest by hand to check). **If you'd rather not risk
any real game's data at all, skip #3/#4 and only run steps 8, 9, 12, and 13.**

Also create two disposable scratch folders for the manual-browse step:

```powershell
New-Item -ItemType Directory -Force C:\SaveLockerTest\manual-folder-test | Out-Null
Set-Content C:\SaveLockerTest\manual-folder-test\savefile.txt "test"
```

### 8. The nudge fires once, and only once

In the portable Playnite, select **Unmatched Test Game** and click **Play**. Expect:
- The fake exe's window opens immediately ("Conflict Game is running" / an Exit button — same
  window `fake-game` always shows, the label is cosmetic) — the nudge is never blocking.
- A Playnite notification appears (bell icon, top right, or a toast if enabled): *"SaveLocker
  couldn't automatically match 'Unmatched Test Game' — click to link it and sync this game."*

Click **Exit** on the fake exe, then launch **Unmatched Test Game** a second time. Expect **no
second notification**. Confirm directly:

```powershell
Get-Content "C:\SaveLockerTest\Playnite\ExtensionsData\4d7017e5-87c0-4011-92c4-83f5dde2ada2\shown-link-nudges.txt"
```

(the portable install's `ExtensionsData\<PluginId>` — `4d7017e5-87c0-4011-92c4-83f5dde2ada2` is
`SaveLockerPlugin.PluginId`, see `docs/REPO_MAP.md`). Expect exactly one GUID printed, for this one
game — `NudgeState` writes one line per Playnite game `Id` the first time it's offered the nudge,
never again after.

### 9. The nudge is suppressed while the agent is unreachable

Stop the test agent (Ctrl+C in its terminal, or close the window it's running in). Launch **Second
Unmatched Game**. Expect: the fake exe opens immediately, **no notification at all** — re-run the
`Get-Content` command from step 8 and confirm this game's id is still **absent** (genuinely never
offered, not "already shown"). Restart the test agent:

```powershell
cd D:\Projects\SaveLocker\SaveLocker\src\Agent\bin\Debug\net10.0-windows
$env:SAVELOCKER_STATE_ROOT = "C:\SaveLockerTest\agent-state"
$env:SAVELOCKER_TRAY_PORT  = "5177"
.\SaveLocker.Agent.exe
```

Launch **Second Unmatched Game** again. Expect: **now** the nudge appears, and the `Get-Content`
check shows a second GUID in the file. This is `SaveLockerPlugin.FindMatch`'s `agentReachable`
distinction doing its job — an unreachable agent must never look identical to "reached the agent,
no match" for nudging purposes, or every unmatched launch while the agent happens to be down would
fire one.

### 10. Tier 2 — automatic resolve and enroll

Launch entry #3 (the real, never-tracked game) and click its notification. Expect the popup window
to open on a brief loading screen ("Looking for a match…", then "Checking SaveLocker's game
database…"), then land directly on:

> **Track "\<your title\>" as \<same or manifest-normalized title\>?**
> Save found at: *\<a real path under this machine's profile — e.g.
> `C:\Users\<you>\AppData\Local\<Publisher>\<Game>\Saves`\>*

Click **Enroll**. Expect a Playnite notification *"SaveLocker: now tracking '\<title\>'."* and the
popup window closes on its own. Confirm with the helper from earlier:

```powershell
Get-SaveLockerGames | Format-Table id, name, alias -AutoSize
```

Expect a new row for this game, `alias` **empty** if the manifest's name matched Playnite's title
exactly, or **set to your Playnite title** if the manifest normalized it to something slightly
different.

**Alias-backfill check** (the fix that makes the popup's result actually stick — without it,
`GameMatcher`'s name/Alias tier would never recognize this game again): launch entry #3 a **second**
time. Expect **no popup, no nudge** — it auto-matches silently now, the same as any already-tracked
game.

**If it landed on the manifest-search screen instead** (`SuggestedSaveDir` resolved to nothing),
that just means this particular title/save-folder combination didn't auto-resolve — pick a
different game for entry #3, or continue straight into step 11's search flow with this same entry.

### 11. Tier 3 — manual manifest search

Launch entry #4 (the nickname, e.g. "Civ 6"). Expect the manifest-search screen: *"SaveLocker
couldn't automatically find 'Civ 6' — search its game database by name,"* search box pre-filled
with `Civ 6` (and likely an empty or irrelevant result list from that auto-search). Clear the box,
type a distinctive fragment of the real title — e.g. `civilization` — and click **Search**. Expect a
scrollable list including the manifest's exact entry (e.g. "Sid Meier's Civilization VI"). Double-
click it. Expect either:

- the confirm-enroll screen (likely, since entry #3 already proved this title/folder resolves), or
- the honest *"SaveLocker knows … but hasn't found a save folder"* screen, if it genuinely didn't.

Either way this confirms the search → re-lookup round trip. If you land on confirm-enroll, click
**Enroll**, then re-run:

```powershell
Get-SaveLockerGames | Format-Table id, name, alias -AutoSize
```

Expect **one row**, not two — `name` is the manifest's real spelling (not "Civ 6"), and `alias` is
now `Civ 6` (the backfilled alias that lets THIS Playnite entry match on its next launch, separate
from entry #3's own alias if the two titles differed). Confirm by launching entry #4 again: expect
**no popup**.

### 12. Tier 4 — manual folder browse, and a clean refusal

Launch **Totally Fake Game XYZ**. Expect the manifest-search screen with **zero results** for any
query you try (it's genuinely absent from the ~53,000-name manifest). Click **Browse for the folder
myself** — Playnite's own native Windows folder-picker dialog should open (a real `SelectFolder()`
call — not a web page, not a WebView2 popup; confirms the deliberate deviation from `plan.md`'s own
WebView2 suggestion, see `docs/CONTEXT.md`). Navigate to and select:

```
C:\SaveLockerTest\manual-folder-test
```

Expect a brief "Checking that folder…" screen, then the confirm-enroll screen showing that exact
path. Click **Enroll** — expect the same success notification as step 10, and a new row from
`Get-SaveLockerGames` with `path` equal to (or a canonicalized form of) that folder.

**Refusal check** (`plan.md`'s "Link to SaveLocker" problem 4 — the popup must surface *why* a
folder was refused, not fail silently). Add one more throwaway Custom Game entry (same Path/
Arguments as the others, Name e.g. `Refusal Test Game`), launch it, reach the same "Browse for the
folder myself" screen, but this time pick **`C:\`** itself (the drive root) in the folder picker.
Expect a plain-English refusal on the error screen — something like *"Can't use that folder: …"* —
**never** a raw `{"error":"..."}` blob or an unhandled exception, with a working **Try again**
button that reopens the folder picker.

### 13. Pick an existing tracked game (available from every screen)

Launch **Nudge Rearm Test** (entry #6 — untouched so far, so its nudge fires fresh) and click its
notification. From whichever screen the popup opens on, click **Pick an existing tracked game
instead**. Expect a filterable list of every game tracked so far (from step 4's baseline, plus
whichever of steps 10–12 you ran). Type part of the step-4 baseline game's name into the filter box
at the top, confirm the list narrows to matches, then double-click the correct one. Expect a
Playnite notification *"SaveLocker: linked '\<Playnite title\>' to '\<tracked name\>'."* and the
popup closes. Confirm:

```powershell
Get-SaveLockerGames | Format-Table id, name, alias -AutoSize
```

Expect the step-4 baseline game's row now shows `alias` = `Nudge Rearm Test` (no new row was
created — this tier links, it never enrolls).

### 14. Cancel is always safe, and stays cancelled

Add one final throwaway Custom Game entry, launch it, let its nudge fire, open the popup, and click
**Cancel** (or the window's own titlebar close button) on whichever screen you land on. Expect: the
window closes, `Get-SaveLockerGames` shows no new row and no changed `alias` anywhere. Launch that
same entry again — expect the nudge does **not** reappear. That second part is a deliberate
tradeoff already called out in `docs/CONTEXT.md`, not a bug: the nudge is marked shown the moment it
fires, regardless of what the player does with it afterward, so a cancelled (or errored-out) attempt
doesn't get a second automatic chance until Phase 13's right-click menu entry exists. Confirm it
matches this expectation rather than treating it as a defect.

No separate teardown for any of this — the Cleanup step below already deletes everything Phase 12's
testing touched, since none of it ever left `C:\SaveLockerTest\`.

## Cleanup

Stop both agent processes and the test server (Ctrl+C in each terminal), then delete
`C:\SaveLockerTest\` entirely — nothing under it is real state. Your actual installed Playnite,
its real library, and your real SaveLocker agent (`%ProgramData%\SaveLocker`, port `:5178`) were
never touched by any of the above.
