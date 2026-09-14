<#
.SYNOPSIS
    Build SaveLocker.Playnite and install it straight into a Playnite install root, skipping the
    .pext pack / double-click-install round trip.

.DESCRIPTION
    Points at any Playnite install root -- a portable extraction (recommended for testing) or a
    real installed one -- builds the plugin, and copies extension.yaml + the built DLL straight
    into "<PlaynitePath>\Extensions\SaveLocker". Playnite loads unpacked extension folders exactly
    the same as a .pext-installed one; packing is only needed for distribution.

.PARAMETER PlaynitePath
    Root of a Playnite install -- the folder containing Playnite.DesktopApp.exe. For a portable
    test instance this is wherever the release .7z was extracted, e.g. C:\SaveLockerTest\Playnite.

.PARAMETER Configuration
    Build configuration to install. Defaults to Release.

.PARAMETER SkipBuild
    Reuse the last build output under src\bin\<Configuration>\net462 instead of running
    dotnet build again.

.EXAMPLE
    .\scripts\Install-ToPortable.ps1 -PlaynitePath C:\SaveLockerTest\Playnite

.EXAMPLE
    .\scripts\Install-ToPortable.ps1 -PlaynitePath C:\SaveLockerTest\Playnite -SkipBuild
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PlaynitePath,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repoRoot "src\SaveLocker.Playnite.csproj"

if (-not (Test-Path $PlaynitePath)) {
    throw "Playnite path not found: $PlaynitePath"
}
if (-not (Test-Path (Join-Path $PlaynitePath "Playnite.DesktopApp.exe"))) {
    Write-Warning "No Playnite.DesktopApp.exe directly under '$PlaynitePath' -- confirm this is a Playnite install root, not e.g. its Extensions folder."
}

if (-not $SkipBuild) {
    Write-Host "Building SaveLocker.Playnite ($Configuration)..." -ForegroundColor Cyan
    dotnet build $csproj -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed (exit $LASTEXITCODE)."
    }
}

$buildOut = Join-Path $repoRoot "src\bin\$Configuration\net462"
$dll = Join-Path $buildOut "SaveLocker.Playnite.dll"
$manifest = Join-Path $buildOut "extension.yaml"
foreach ($f in @($dll, $manifest)) {
    if (-not (Test-Path $f)) {
        throw "Expected build output missing: $f (did the build actually run? try without -SkipBuild)"
    }
}

$ext = Join-Path $PlaynitePath "Extensions\SaveLocker"
New-Item -ItemType Directory -Force $ext | Out-Null

try {
    Copy-Item $manifest, $dll -Destination $ext -Force
} catch {
    throw "Could not copy into '$ext' -- if Playnite is running from this path, close it first (it locks the DLL while loaded). Original error: $($_.Exception.Message)"
}

Write-Host "Installed to $ext" -ForegroundColor Green
Write-Host "(Re)start Playnite at '$PlaynitePath\Playnite.DesktopApp.exe' to load this build."
