---
description: Run the plugin test suite and diagnose failures
---

$ARGUMENTS

Run `dotnet test tests\SaveLocker.Playnite.Tests\SaveLocker.Playnite.Tests.csproj -c Release` (needs a real Playnite install — same SDK HintPath as `src/`).

The suite covers `GameMatcherTests` (pure logic) and `LocalApiClientTests` (an in-process `HttpListener` stub of the agent's local API) — nothing real is touched, and the Steam-AppID matcher tier is deliberately untested. Hardware behavior (the pre-launch gate really firing inside Playnite) is only verifiable manually via the main repo's `tests\testenv.ps1` (`docs/Build and Run.md`).

Report pass/fail, show failing output, and suggest fixes.
