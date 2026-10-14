---
description: Draft a pull request description against origin
---

Target remote (ALWAYS origin, never an upstream):
!`git remote get-url origin`

Commits ahead of origin/main:
!`git log origin/main..HEAD --oneline`

Changed files:
!`git diff --stat origin/main`

$ARGUMENTS

Write a PR description with: summary, key changes, verification evidence (`dotnet build` + `dotnet test`, plus hardware status via `docs/Build and Run.md` when relevant), related issues. Follow conventional commits tone. Output markdown only. If the current branch is `main` itself, say so and stop instead of inventing a diff.
When asked to open the PR with gh: derive OWNER/REPO from the origin URL above and run `gh pr create --repo OWNER/REPO --base main --head <branch>`. Never use an `upstream` remote as base or repo if one exists — origin (Marwanello/SaveLocker-Playnite) is the only writable remote for this repo.
