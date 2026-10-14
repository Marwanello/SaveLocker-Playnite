---
description: Lint the plugin and test project (compiler warnings)
---

$ARGUMENTS

There is no separate linter here — `dotnet build` warnings are the lint signal.

Build `src\SaveLocker.Playnite.csproj -c Release` and `tests\SaveLocker.Playnite.Tests\SaveLocker.Playnite.Tests.csproj -c Release`; report warnings (if any) as lint findings, grouped by project. Do not commit anything. Both need a real Playnite install on the machine (`Playnite.SDK.dll` comes from the `%LocalAppData%\Playnite` HintPath, not NuGet — `AGENTS.md`).
