# 2026-09-17 — Release version stamping (branch `fix-version`, PR #7)

## The bug

The v0.1.1 release — built for real by the Phase 17 workflow on 2026-09-17 — installs and displays
as **0.1.0**. Playnite reads the displayed version from the `extension.yaml` packed *inside* the
zip, and that file's `Version:` field is hardcoded in the repo (`extension.yaml:4` still says
`0.1.0`; it was never bumped when tagging). `release.yml` packs the built `extension.yaml` verbatim
(`Compress-Archive ... extension.yaml ...`), so the tag only names the GitHub Release — the package
manifest is whatever the checked-in file says.

The mismatch is worse than cosmetic because `installer.yaml` correctly advertises `Version: 0.1.1`:
anything consuming the installer manifest (the add-on database, the agent self-updater's
`CheckAsync` comparison) sees 0.1.1 available while the installed plugin reports 0.1.0 — a permanent
"update available" that installs the same wrong-versioned bytes.

## The fix

New **"Stamp version from tag"** step in `.github/workflows/release.yml`, placed *before* Build:

- Strips the `v` off `GITHUB_REF_NAME` and rewrites the `Version:` line in the repo-root
  `extension.yaml`. Editing the root copy (not the bin copy) is deliberate: the csproj
  (`<None Include="..\extension.yaml">`) copies it into build output during Build, so one edit
  covers the whole pipeline, and nothing in the CI checkout is ever committed.
- Guards: throws if the ref isn't a `v*` tag (workflow only triggers on `v*` anyway — defense in
  depth), and throws if the regex rewrote nothing (protects against a silent no-op if the manifest
  shape ever changes).
- Regex `(?m)^Version: [^\r\n]*` rather than `.*$` so the file's CRLF endings are preserved
  verbatim.

`extension.yaml` in the repo stays `0.1.0` — it's now just the dev placeholder; the tag always wins
in CI.

## Verification status

- Stamp logic dry-run locally against a copy of the real `extension.yaml` (`v0.1.2` →
  `Version: 0.1.2`, byte-clean). **The workflow itself has not been exercised end to end** — same
  manual-verification caveat Phase 17's original workflow carried until the real v0.1.0 push.
- **The already-published v0.1.1 release still contains the wrong 0.1.0 manifest** (it was built by
  the old workflow and a CI change can't rewrite past artifacts). After PR #7 merges: delete +
  re-push `v0.1.1`, or cut `v0.1.2`, to get a correctly stamped release out.

## Commits

- `76db23f` — CI: stamp extension.yaml Version from the release tag
- Docs commit (this file + `CONTEXT.md` update) on the same branch

PR: https://github.com/Marwanello/SaveLocker-Playnite/pull/7
