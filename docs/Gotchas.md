# SaveLocker-Playnite — Gotchas

## `Toolbox.exe pack` is broken on this dev box — missing `NLog.dll`

`%LocalAppData%\Playnite\Toolbox.exe` throws `FileNotFoundException: Could not load file or assembly
'NLog, Version=4.0.0.0...'` immediately on start, from any working directory. Confirmed
`NLog.dll` genuinely does not exist anywhere under `%LocalAppData%\Playnite` or
`%AppData%\Playnite` on this install — not a PATH/cwd issue, the file is actually missing from this
particular Playnite install.

**Workaround, used for every build so far**: a `.pext` is just a zip archive of `extension.yaml` +
the built DLL (confirmed — Playnite's own packaging fallback path treats it exactly this way).
`Compress-Archive` can't target `.pext` directly (`"is not a supported archive file format"`), so zip
to `.zip` first and rename:

```powershell
$src = "src\bin\Release\net462"
Compress-Archive -Path "$src\extension.yaml", "$src\SaveLocker.Playnite.dll" -DestinationPath dist\SaveLocker.zip
Rename-Item dist\SaveLocker.zip SaveLocker.pext -Force
```

`docs/Build and Run.md` uses this by default. Worth retrying `Toolbox.exe` after a Playnite update —
if it starts working, prefer it (it does a manifest/version sanity check this manual zip does not).

## Playnite.SDK is referenced from the real install, not NuGet

Playnite does not publish `Playnite.SDK` to nuget.org. The `HintPath` in the csproj points straight
at `%LocalAppData%\Playnite\Playnite.SDK.dll`, `Private=false` (Playnite supplies this DLL to the
loaded plugin at runtime — copying a second copy into the extension's own folder risks a version
mismatch that's hard to diagnose). This means **the build requires Playnite to actually be installed
on the machine** — there is no CI here for that reason (see `.github/workflows/`, empty on purpose).

## No `ObservableObject`/settings-base helper ships in this SDK version

Checked by reflecting the installed `Playnite.SDK.dll` (`Assembly.LoadFile` + `GetType`/
`GetProperties`/`GetMethods` from PowerShell) rather than guessing from docs, which are thin on
exact signatures for `GenericPlugin`. `Playnite.SDK.ISettings` is just `IEditableObject` +
`VerifySettings(out List<string>)` — no `ObservableObject` base class exists to inherit from, hence
this repo's own tiny `PropertyChangedBase.cs`. If a future SDK version adds one, there's no reason to
switch — the tiny version is dependency-free.

## `extension.yaml`'s `Id` must be a GUID string, and must match the C# `Id` override exactly

Playnite's own docs' `extension.yaml` example (`Id: MyGenericPlugin_Playnite_Plugin`) doesn't
mention the C# side at all: `Plugin.Id` (reflected: `abstract Guid Id { get; }`) demands the SAME
value as a `System.Guid`. Both must be updated together if this plugin is ever re-keyed —
`SaveLockerPlugin.PluginId` in `SaveLockerPlugin.cs` and `Id:` in `extension.yaml`.

## No XAML/BAML in this project, deliberately

Every WPF view (`SaveLockerSettingsView`, `ConflictResolveWindow`) is built in plain C# — `Grid`/
`StackPanel`/`TextBlock` construction, no `.xaml` files, no designer. This wasn't forced by a build
failure — `dotnet build` targeting `net462` with `PresentationFramework`/`PresentationCore`/
`WindowsBase`/`System.Xaml` referenced directly built clean on the first real attempt, including
these WPF types — but XAML/BAML compilation specifically was never exercised, since these two views
were written without it from the start to remove one more unknown from Phase 8 (the plan's own
highest-toolchain-risk phase). If a future session wants XAML for a more complex view, try it — this
repo not using it is a risk-avoidance choice, not a confirmed limitation.

## JSON: `JavaScriptSerializer`, not Newtonsoft or `System.Text.Json`

net462 has neither built in. Rather than add a NuGet dependency that then needs to be copy-local'd
into the packed extension (another moving part in Phase 8), `Json.cs` uses
`System.Web.Extensions`'s `JavaScriptSerializer` — part of the .NET Framework GAC on any box that can
run Playnite at all, zero extra files to pack. Only its **untyped** `DeserializeObject`/`Serialize`
path is used (plain `Dictionary<string, object>` trees), specifically to avoid
`JavaScriptSerializer`'s own reflection-based typed (de)serialization, whose property-name-casing
behavior wasn't worth trusting blind — every field is pulled by its exact camelCase JSON key instead
(matching the server's `System.Text.Json` default naming), in `Contracts.cs`'s `FromJson` methods.

## Building requires the reference-assemblies NuGet package

`dotnet build` (the modern cross-platform SDK CLI, not classic `MSBuild.exe` from a full Visual
Studio install) needs `Microsoft.NETFramework.ReferenceAssemblies` to resolve `net462` reliably
regardless of what's globally installed on the box. Already wired into the csproj
(`PrivateAssets="all"` — it's a build-time-only reference, never shipped). If a future session drops
this package and the build still works, that's fine; if it starts failing with framework-resolution
errors, this is why it was added.

## State dir / token path — real agent vs. a test one

The plugin reads `<StateDir>\api-token` for the `X-SaveLocker-Token` header
(`SaveLocker/src/Agent.Core/LocalAuth.cs`). The real installed Windows agent always uses
`%ProgramData%\SaveLocker` — the plugin's settings default to that. A **test** agent started via the
main repo's `testenv.ps1` with `SAVELOCKER_STATE_ROOT` set writes its token somewhere else entirely,
and its local API listens on a different port (`:5177`, not `:5178`) — both need to be changed in
this plugin's settings page when pointed at a test agent, not just the URL. See
`docs/Build and Run.md` → "Testing against a portable Playnite + test agent" for the full manual loop
(this plugin doesn't yet have its own `testenv` integration — that's Phase 15, Group 5, not built
yet).
