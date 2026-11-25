# ADR-0020: Branches, the quality gate, and image channels for Hephaestus's own repository

- **Status:** Accepted (owner decision)
- **Date:** 2026-11-25
- **Spine:** Consistency conventions (Branches and checks, Analysis credential, Releases and channels); NFR-18

## Context

The owner wants Hephaestus's own code held to a stated standard: two long-lived branches, changes only through pull requests, SonarQube, at least 80% test coverage over all code and over new code, a preview image from `develop`, and a production version from `main`. The repository is public. From v0.1 on, some pull requests are written by an Agent, and Hephaestus will work on its own repository.

## Decision

**Branches.** `develop` is the default branch and the target of feature pull requests. `main` receives pull requests from `develop`, and from `hotfix/*` when production must be fixed first. Both are protected; neither accepts a direct push.

**Two coverage checks, two mechanisms.**

- *All code:* the build fails below 80% line coverage, in the .NET solution and in the Angular workspace. Exclusions live in one file, each with a reason. This check needs no service, so it can also be a Validation command when Hephaestus works on itself.
- *New code:* SonarQube Cloud's built-in quality gate, on every same-repository pull request and on both branches.

**No stored analysis token.** The workflow uses SonarQube Cloud's secretless analysis: it exchanges a GitHub-issued OpenID Connect token for a credential that lasts thirty minutes and can only submit an analysis for this project.

**Image channels.** A merge to `develop` publishes `develop` and `develop-<short commit id>`: a preview channel, not a release, outside the versioned public surface. A merge to `main` publishes the version the repository states and creates its git tag. The pull request to `main` cannot merge unless that version is a valid three-part Semantic Version, higher than the last one published, and present in the changelog.

## Alternatives considered

- **Self-hosted SonarQube Community Build.** Free and open-source, but it analyses one branch only and has no pull request analysis, so it cannot gate pull requests to `develop`.
- **No SonarQube; analyzers and the coverage threshold only.** Keeps everything in the build. The owner chose SonarQube Cloud, where the owner already has an account.
- **A stored SonarQube token.** On the free plan only personal tokens exist, and they carry the whole account's permissions. Handing one to a workflow that runs Agent-written code was the one real risk of this decision; secretless analysis removes it.
- **Analysing only after a merge.** No credential in pull request workflows at all, but a failed gate would be found too late to block.
- **One custom quality gate carrying both coverage conditions.** Depends on a plan feature that may not be available; splitting the two checks depends on nothing.
- **Releasing by a hand-pushed tag.** "Merged to `main`" and "released" can drift apart, and the version and changelog checks run after the merge instead of before it.

## Consequences

- SonarQube Cloud is a hosted, proprietary service. NFR-15 is clarified to govern what an installation needs to run; nobody who runs Hephaestus needs a SonarQube account.
- Pull requests from forks cannot obtain the credential and get no SonarQube analysis. The build's own threshold and tests still apply.
- Code in the same job could use the short-lived credential to submit a forged analysis of this public project. The threat model records this as accepted (T39).
- When Hephaestus works on itself, its Base Branch is `develop`, and its pull requests are held to the same checks as a person's.
- Publishing an image deploys nothing. Hephaestus never updates or redeploys itself.
