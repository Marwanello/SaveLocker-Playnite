---
description: Walk through the SaveLocker-Playnite release rollout
---

$ARGUMENTS

Follow the rollout in order, confirming the version number first. Steps:
1. Confirm the tag (`vX.Y.Z`) with the user before pushing it — pushing the tag is what runs CI (a real GitHub Release, real Actions minutes; see `AGENTS.md`).
2. Push `vX.Y.Z`. `.github/workflows/release.yml` stamps `extension.yaml`'s `Version:` from the tag, builds, and publishes `SaveLocker.zip` + `SaveLocker.pext` + `SHA256SUMS.txt`. Confirm the run succeeded and the release assets verify against the checksums.
3. Bump root `installer.yaml` to the new release (`Version`, `ReleaseDate`, `PackageUrl`) — it is hand-maintained and is what the add-on database serves. Update `docs/addon-submission/` too if the listing is in flight.
4. A published release whose manifest has the wrong version cannot be fixed in place without deleting and re-pushing the tag — prefer cutting the next version instead.
Report each step's result before proceeding to the next.
