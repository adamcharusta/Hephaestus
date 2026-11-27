---
title: 'Story 3.5: Give every step a fresh checkout'
type: 'feature'
created: '2026-11-27'
status: 'done'
baseline_commit: '54238e52f5557a3fb96cf1ef5feda1daedbecb60'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A Task volume keeps whatever a step left in it, so a git hook, a git configuration entry, or an ignored fake test runner planted by one step runs beside the next step's credential or decides Validation. Nothing keeps a bare mirror of a Project, nothing turns a commit into a bundle, nothing re-creates `/workspace` from one, and no `hephaestus-tools` image exists (AD-6, AD-9, FR-17, FR-18, AR-49, AR-3, AR-38).

**Approach:** The gateway keeps one bare mirror per Project on the `hephaestus-mirrors` volume, fetched with the installation token passed through the environment of one git process and never written down, and writes a bundle of one commit to the data volume. A checkout container of the `hephaestus-tools` image (git only), with no network and no credential, receives that one file, empties `/workspace`, and makes a fresh repository in it as user `10001:10001`. The `hephaestus` image gains git 2.48 or later, which means a newer base image.

## Boundaries & Constraints

**Always:**
- The checkout container mounts the Task volume with `volume-nocopy` and nothing else, has `network none`, every capability dropped, `no-new-privileges`, limits, and the three labels, runs as `10001:10001`, and receives the bundle as one archive entry; it is removed when it ends, and so is the bundle file.
- Git runs in the gateway only against a mirror and in the checkout container only; the `git` process of the gateway gets no hooks (`core.hooksPath` off, empty template), no system or user configuration, no prompt, and its token only in its environment.
- The repository the checkout makes has default configuration, no hook, no remote, no untracked or ignored file, and the Task's branch checked out at exactly the bundle's tip; the checkout refuses a bundle with more than one head or another tip than the one it was told.
- A retry after a crash replaces the container by name and makes the same workspace, because the checkout empties `/workspace` first.

**Never:**
- Bind-mount a host path, mount the mirrors or data volume into any container, run git in a Task volume from the orchestrator, or put the token in an argument, a file, a remote URL, or the mirror's configuration.
- Run a step, checkpoint (3.6), import or push a bundle (3.7), or call any of this from the loop (3.14).
- Edit the publishing workflow (it does not exist; Story 1.13 builds it) or the epics.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| First step | Task volume empty, bundle of the start commit | `/workspace` holds the commit on the Task branch, owned by `10001` | N/A |
| Next step | A step planted a hook, a config entry, an ignored runner, a read-only directory | None of them exists afterwards | N/A |
| Mirror | Project fetched | Bare, no hooks, no working tree, no URL or token in its configuration | Not installed, unreachable, git failed: a result, not an exception |
| Bundle | Commit in the mirror, or fetched on demand | One regular file under the bundles directory with its SHA-256, tip, and one head | Commit missing after a fetch, or file over the bound |
| Bad bundle | Wrong checksum, path outside the directory, two heads, another tip | Refused before a container is made, or the container exits non-zero | Message id in `en` and `pl` |
| Tools image absent | Not on the machine | Pulled; a failed pull names the image | Message id in `en` and `pl` |
| Checkout hangs | Past its limit | Container stopped and removed, timed out | Message id in `en` and `pl` |

</frozen-after-approval>

## Code Map

- `src/Hephaestus.Core/Workspaces/BundleReference.cs`, `CheckoutRequest.cs`, `Ports/IWorkspaceRuntime.cs` (`CheckoutAsync`), `Ports/IRepositoryMirrors.cs` -- the bundle reference (path, checksum, tip), the request, and the two ports.
- `src/Hephaestus.Workspaces/DockerWorkspaceRuntime.Checkout.cs`, `CheckoutScript.cs`, `WorkspaceLabels.cs`, `WorkspaceRuntimeOptions.cs`, `TarFiles.cs` -- the container, its script, the role, the options (`ToolsImage`, `BundlesDirectory`, limits), the archive.
- `src/Hephaestus.GitHub/GitHubMirrors.cs`, `GitProcess.cs`, `MirrorTokens.cs` -- mirror, bundle, the one place a process is started, the installation token.
- `src/Hephaestus.Host/HostSettings.cs`, `Hosting/` -- `ToolsImage`, `MirrorsDirectory`, composition; `deploy/Dockerfile`, `deploy/tools.Dockerfile`, `deploy/compose.yaml`, `.github/workflows/ci.yml`.
- `docs/adr/0022-*.md`, `docs/step-containers.md`, `docs/security/threat-model.md`, `README.md`, `deploy/README.md`, `_bmad-output/implementation-artifacts/deferred-work.md`.
- Tests beside each; `tests/Hephaestus.Workspaces.Tests/CheckoutEngineTests.cs` runs the real tools image in CI.

## Tasks & Acceptance

**Execution:**
- [x] Core: bundle reference, checkout request, ports, messages; tests.
- [x] GitHub: mirror, bundle, process runner, token; tests on real git and a local remote.
- [x] Workspaces: checkout container and script; tests on the Docker double and on the script itself.
- [x] Host, images, Compose, CI: settings, composition, git in the `hephaestus` image, the tools image, the engine tests.
- [x] Documentation, decision record, deferred publishing note, story `done` in `sprint-status.yaml`.

**Acceptance Criteria:**

| Criterion | Shown by |
|-----------|----------|
| One named volume per Task, journaled, no host path | `TaskVolumeTests`, `CheckoutContainerTests` (no volume is made, no bind) |
| Bare mirror with no hooks or working tree; no git outside a mirror | `GitHubMirrorsTests`, `GitMirrorRulesTests` |
| Fresh clone, default config, no untracked or ignored files, tools image, no credential | `CheckoutScriptTests`, `CheckoutContainerTests`, `CheckoutEngineTests` |
| Planted hook, config entry, and ignored runner are gone | `CheckoutScriptTests`, `CheckoutEngineTests` |
| No credential in the volume, the environment, or a remote URL | `CheckoutContainerTests`, `GitHubMirrorsTests`, `CheckoutEngineTests` |
| The tools image is git only, 2.48 or later; the `hephaestus` image has git 2.48 or later | `ComposeFileTests`, `CheckoutEngineTests`, CI |
| The step user can write to every path the checkout created | `CheckoutEngineTests` |

## Implementation Notes

- Unattended run: no human gate, and no Docker daemon answers where this was built, so `CheckoutEngineTests` (the real tools image) is written for CI, where `build-and-test` builds `hephaestus-tools:ci` from `deploy/tools.Dockerfile` and names it in `HEPHAESTUS_TEST_TOOLS_IMAGE`; without that variable it skips, and on GitHub Actions it fails instead. Every row of the matrix has a test that runs here: `CheckoutScriptTests` runs the script itself with the git and shell of the machine against a real bundle, `CheckoutContainerTests` the container definition and its outcomes against the Docker double, `GitHubMirrorsTests` the mirror and the bundle with real git and a local repository standing in for GitHub, `MirrorTokensTests` the token.
- The orchestrator image needs git 2.48 or later (AR-3), and `aspnet:10.0` is Ubuntu 24.04 (git 2.43; checked against MCR's manifests, where `10.0` and `10.0-noble` are the same digest). The runtime stage is therefore `aspnet:10.0-resolute` (Ubuntu 26.04, git 2.53) with a build-time check; ADR-0022 records it. The tools image is `alpine:3.23` and `git`.
- The checkout is `IWorkspaceRuntime.CheckoutAsync`, awaited by the caller for the seconds it takes, and not part of `StartStepAsync`: the loop (3.14) sequences the mirror fetch, the bundle, the checkout, and the step. The runtime deletes the bundle it consumed, and does not stop the container itself at its limit (only the step watcher stops containers, `StepEndRulesTests`): a forced removal kills it.
- The mirror is fetched with `+refs/heads/*:refs/heads/*`; the commit of a bundle is given a temporary name `refs/hephaestus/bundle/<step-run>` in the mirror for as long as `git bundle create` takes. The checkout does not use strict object checking, because the objects come from the mirror the gateway fetched; strict checking is of what comes back (3.7).
- `hephaestus-tools` is built by a `tools` Compose service in a profile `up` does not start. The criterion on the publishing workflow of Story 1.13 is not met here, because that workflow does not exist; `deferred-work.md` records it, with the loop wiring and cleanup of leftovers.

## Spec Change Log

## Review Triage Log

| Finding | Verdict | Route | Evidence |
|---------|---------|-------|----------|
| Acceptance table cites `GitProcessTests`, `ProcessBoundaryTests`, `ToolsImageTests`, which do not exist | low | patch | Table names corrected to `GitHubMirrorsTests`, `GitMirrorRulesTests`, `ComposeFileTests`. |
| Story status in the spec, the tracking file, and the task list disagree | false | none | The spec is `in-review` during review and the tracking file is set to `done` at the end, as the run's instructions say. |
| English reason fragments passed as arguments of a localized message | medium | patch | Each refusal has its own message id in `en` and `pl` (`workspace.checkout.bundle-*`, `workspace.tools-image.missing`). |
| A failed checkout loses its diagnosis; exit codes 21, 22, 23 are not mapped | medium | patch | The three codes have messages of their own; any other ends with the exit code and the last 500 characters of the container's output (`CheckoutContainerTests`). |
| The checked bundle is not the bundle that is sent (two reads) | medium | patch | The SHA-256 is taken while the file is copied into the archive (`TarFiles.OwnedFile`); a mismatch is refused before extraction. |
| A refused bundle is not deleted, against the port's contract | medium | patch | The delete is in the outer `finally`, for any path inside the bundles directory, and deletes a link and not its target (`CheckoutContainerTests`). |
| A rate limit (403 with a rate-limit text, 429) is classified as a refusal and discards a good token | medium | patch | Both are `Unreachable` and the token is kept (`A_rate_limit_is_a_wait_and_the_token_is_kept`). |
| The size bound is enforced after `git bundle create` has written the file | medium | patch | `rev-list --objects --disk-usage` is checked first (`A_history_larger_than_the_bound_is_refused_before_anything_is_written`); the check after writing stays. |
| The bundle bound is defined twice | low | patch | One constant, `BundleReference.DefaultMaximumBytes`, feeds both options. |
| The git environment loses the proxy and certificate variables | medium | patch | An allowlist of nine variables passes through (`Only_the_proxy_and_certificate_variables_of_the_host_reach_git`). |
| `git gc --auto` may run detached beside the next command | medium | patch | `gc.autoDetach=false` and `maintenance.autoDetach=false` on every run. |
| A mirror left half made is never repaired | medium | patch | A mirror that is not a whole bare repository is removed and made again (`A_mirror_a_crash_left_half_made_is_removed_and_made_again`). |
| Mirrors have no growth policy | low | defer | Recorded in `deferred-work.md`; the volume is a cache. |
| `BundleRequest` validates nothing and lives in `MirrorResult.cs` | low | patch | Own file, validating constructor. |
| The Task volume is not checked to exist | medium | patch | `RequireTaskVolumeAsync` refuses with `workspace.task-volume.missing`; nothing is created (`A_task_whose_volume_does_not_exist_gets_no_checkout_and_no_volume_is_made_for_it`). |
| The checkout container is less hardened than the proxy (no read-only root) | false | none | Docker refuses an archive copy into a read-only root file system, and the bundle is placed that way; the container has no network, no capability, and one volume. |
| SHA-256 object format is half supported | low | patch | `BundleReference` and `BundleRequest` accept 40-digit commits only, which is what GitHub serves. |
| The task-branch pattern refuses a name `BranchName.For` makes for a ten-digit issue number | low | patch | `{1,10}` digits; `A_name_made_for_the_largest_issue_number_is_a_task_branch`. |
| The 2.48 rationale contradicts the checkout not checking objects | low | patch | Dockerfile, ADR, and CI wording say the version is the architecture's requirement for both images and what the import relies on. |
| README says Hephaestus runs git only against a mirror | low | patch | Reworded: the `hephaestus` container runs git only against a mirror; the tools container runs it in the workspace. |
| Tests that grep source text | low | patch | The process rule now catches target-typed creation (`The_rule_catches_the_ways_a_process_could_be_started`); the behaviours are shown by the recording-git tests. |
| The CI hook check re-implements the runner's command | low | patch | The step is renamed to say what it shows; `GitHubMirrorsTests` shows the runner's. |
| No test of cancellation, and an undisposed stream and a stale test name | low | patch | `A_fetch_that_is_cancelled_kills_its_git_process...`; the stream is disposed; the compose test renamed. |
| `TestGit` and `LocalGit` are near duplicates in two test projects | low | none | Two test projects cannot share a helper without a third project; the duplicates are small. |
| `MirrorTokens` keeps its own installation cache | low | none | The cost is one extra read of the installations an hour; sharing would couple the poller to the gateway. |
| Submodules and Git LFS are not handled | medium | defer | Recorded in `deferred-work.md` and the ADR. |
| Mirrors directory with a trailing separator makes every git call throw | medium | patch | `TrimEndingDirectorySeparator` for the mirrors and the bundles directory (tests for both). |
| An unwritable mirrors directory, or a bad address, throws instead of a result | medium | patch | Both are `GitFailed` (`A_mirrors_directory_that_cannot_be_made...`, `An_address_that_is_not_an_absolute_address...`). |
| A removed Project gets a bundle when the mirror holds the commit | medium | patch | The Project is looked up first (`A_removed_project_gets_no_bundle...`). |
| A container that is created, or dead, counts as a finished checkout | high | patch | Only `exited` with code 0 is success; `created` is waited for and `dead` fails (`CheckoutContainerTests`). |
| `chmod -R` may follow a top-level symlink | low | patch | `! -type l` on the chmod. |
| `GitHubMirrors` to `MirrorTokens.Forget` is not asserted | medium | patch | `A_token_git_says_GitHub_refused_is_forgotten_so_that_the_next_fetch_asks_for_another`. |
| Gateway-made bundle and runtime options are verified only separately | low | patch | Both read `HostSettings.BundlesDirectory`; a gateway-style bundle, whose head is `refs/hephaestus/bundle/<id>`, is checked out in `CheckoutScriptTests`. |
| The tmpfs is a second mount of the checkout container | false | none | It is memory, not a mount of any state; the spec's rule is about volumes and host paths. |

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: no warnings
- `dotnet test --solution Hephaestus.slnx` -- expected: all pass; real-Engine tests skipped without Docker
- `python3 eng/check-coverage.py TestResults` -- expected: line coverage at or above 80%
