# Group 6, Phase 16 — add-on database submission prep (2026-09-16)

Asked directly: prepare everything for the `JosefNemec/PlayniteAddonDatabase` submission except
actually opening the pull request — verify first, then submit.

## What this session did

Fetched the addon database's own README (`JosefNemec/PlayniteAddonDatabase`) directly rather than
working from memory, and cross-checked the exact manifest shape against a real, already-merged entry
(`AfonsoJeremias/GameManagement`'s `addons/generic/*.yaml` + its own `installer.yaml`) to confirm
formatting conventions before writing anything.

Two files staged under `docs/addon-submission/` — **not yet live, not yet submitted anywhere**:

- `addon-manifest.yaml` — the primary listing (`AddonId`, `Type: Generic`, description, tags, links).
  `AddonId` is `extension.yaml`'s own `Id` (must match, per the database's own rule). This file has no
  hard dependency on a release existing and is genuinely ready to submit as-is.
- `installer-manifest.yaml` — the version/package manifest the primary listing's
  `InstallerManifestUrl` will point at (`https://raw.githubusercontent.com/Marwanello/
  SaveLocker-Playnite/main/installer.yaml`, once real — corrected 2026-09-17 after checking this
  repo's actual default branch is `main`, not `master`; the first draft had copied the wrong branch
  name from the reference example, which happens to default to `master`). `RequiredApiVersion:
  6.17.0` is not a guess
  — downloaded Playnite's own latest release (10.60) and read `Playnite.SDK.dll`'s real
  `FileVersion` directly. Confirmed this is a genuinely different number space from Playnite's own app
  version (10.60 the app; 6.17.0 the SDK) — the addon database's own real example
  (`RequiredApiVersion: 6.13.0` against a plugin whose repo builds against a much newer Playnite) made
  the same point independently.

## What is still genuinely blocking the real PR

**No version tag has ever been pushed to this repo.** Phase 17's release workflow
(`.github/workflows/release.yml`) is confirmed code-complete and was actually run once (build +
Playnite.SDK.dll fetch step both verified working, per `docs/CONTEXT.md`), and — checked again this
session — it already produces the `.pext` copy `installer-manifest.yaml`'s `PackageUrl` needs (the
`Package` step Compress-Archives to `SaveLocker.zip` then copies it to `SaveLocker.pext`; both publish).
So the one missing piece is not more workflow code — it is a real `v0.1.0` tag, which this session did
not push (a visible, public action — a real GitHub Release — outside what was asked for this session).

Once that tag exists and the release actually publishes:
1. Confirm the release's three assets exist and `SHA256SUMS.txt` matches `SaveLocker.zip`.
2. Copy `installer-manifest.yaml`'s content to `installer.yaml` at this repo's root, filling in the
   real `ReleaseDate`.
3. Commit `installer.yaml` for real (its raw URL is what the addon database actually reads).
4. Fork `JosefNemec/PlayniteAddonDatabase`, add `addon-manifest.yaml`'s content as
   `addons/generic/Marwanello_SaveLocker.yaml`, open the PR.

Not done this session — by design, per the direct instruction to prepare and verify before
submitting.
