# Branches, pull requests, and checks

Hephaestus's own repository has two long-lived branches and one rule: every change arrives by pull request. This page
says how a feature, a release, and a hotfix reach each branch (AR-76, ADR-0020).

| Branch | Role | Receives |
| --- | --- | --- |
| `develop` | the default branch; the next release being assembled | pull requests from feature, fix, chore, and docs branches |
| `main` | what has been released | a pull request from `develop`, or from a `hotfix/*` branch |

Neither branch accepts a direct push. A branch name says what it is: `feature/<story-or-issue>-<slug>`,
`fix/<slug>`, `chore/<slug>`, `docs/<slug>`, `hotfix/<slug>`.

## A feature

1. Branch from `develop`.
2. Build one story, with its tests and, when it changes what a reader of `README.md` needs, its README change.
3. Open a pull request into `develop`. The checks below must pass; fix what they report.
4. The Owner merges it.

## A release

1. When `develop` holds what the next version should contain, open a pull request from `develop` into `main`.
2. The version and changelog checks of Story 1.13 apply to it, in addition to the checks below.
3. The Owner merges it. Publishing the version and its tag is Story 1.13's work.

## A hotfix

Production must be fixed before `develop` is ready to release:

1. Branch `hotfix/<slug>` from `main`, make the smallest fix, and add a test for it.
2. Open a pull request from the hotfix branch into `main`; it is held to the same checks.
3. After it merges, open a pull request from `main` into `develop` (or merge `main` into a branch for it), so the fix is
   not lost in the next release.

## Checks

GitHub Actions runs `.github/workflows/ci.yml` on every pull request into `develop` or `main`:

| Check (job name) | Fails when |
| --- | --- |
| `Build and test` | the solution does not build, because a compiler warning is an error and nullable reference types are on; a test fails; line coverage over all code is below 80% (`eng/check-coverage.py`); or a package, direct or transitive, carries a license that is not on `eng/allowed-licenses.txt` (`eng/check-licenses.py`) |
| `Image (linux/amd64)` and `Image (linux/arm64)` | the image does not build on that architecture, the Compose file does not start a healthy `hephaestus` container, the command line cannot reach the API, a port is published, something listens on a network address, or a log line is not a JSON object |
| `Gate scripts` | the tests of the two gate scripts fail |
| `Branch policy` | a pull request into `main` does not come from `develop` or a `hotfix/*` branch of this repository (it is skipped for pull requests into `develop`) |

The same commands run locally; they are in [`AGENTS.md`](../AGENTS.md) under "Running and verifying". The pull request
that changes a gate (`eng/`, `.github/workflows/ci.yml`) is judged by the gate it changes, so the Owner reviews those
changes with that in mind.

Coverage exclusions are kept in one file, `eng/coverage-exclusions.txt`, and each line carries its reason; a line without
one fails the gate. Adding a license to `eng/allowed-licenses.txt` is a decision about what the project accepts and is
reviewed as one.

SonarQube Cloud analyses pull requests with its own quality gate (80% coverage on new code). That analysis and its
secretless setup belong to Story 1.16.

## What the Owner sets in GitHub

Only the Owner administers the repository, so these are not set by a pull request:

- `develop` is the default branch.
- `develop` and `main` are protected: no direct push, no force push, no deletion; a pull request is required.
- The checks `Build and test`, `Image (linux/amd64)`, `Image (linux/arm64)`, `Gate scripts`, and `Branch policy` are required status checks on both branches.
- Branches are up to date before they merge.
