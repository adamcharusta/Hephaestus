---
title: 'Story 3.6: Checkpoint a step''s work as one commit'
type: 'feature'
created: '2026-11-27'
status: 'done'
baseline_commit: '90b0c5f1e6367f9b7c6e9744c989058f9a2a94dd'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Whatever an Agent changed in `/workspace` is lost when a step times out, crashes, or is cancelled, and nothing turns a changed tree into a commit that the gateway can later check and push. The Agent's own `.git` cannot be trusted: it may hold hostile configuration and hooks (AD-9, FR-20, AR-50).

**Approach:** After a checkpointed step (`Implementation`, `Correction`, `Rework`) the runtime runs a credential-free checkpoint container of the `hephaestus-tools` image. It builds its own git directory from the last pushed tip outside the Task volume, takes `/workspace` as work tree only, and writes one commit whose parent is that tip, with the message and identity Core composed, and one bundle file. The runtime reads that one file from the stopped container and hands it on as a bundle reference with path, checksum, tip, and prerequisite. An unchanged tree makes neither.

## Boundaries & Constraints

**Always:**
- The checkpoint container is of the tools image, runs as `10001:10001`, has `network none`, every capability dropped, `no-new-privileges`, limits, the three labels, and no credential. Its only mount is the Task volume, read-only and `volume-nocopy`; the base bundle is placed in its own file system. It is removed when it ends, and so is the base bundle's file.
- The script uses `GIT_DIR` of its own and `GIT_WORK_TREE=/workspace`, no system or user configuration, no template; `/workspace/.git` is never read as a repository and nothing in it runs.
- One regular file of bounded size, under a name the runtime chose, is read from the stopped container. A link entry, a parent path, an extra entry, another name, or a size over the bound is an infrastructure error (`WorkspaceRuntimeException`) and nothing from the archive is used. The bundle has exactly one head, `refs/heads/<Task branch>`, and exactly one prerequisite, the last pushed tip.
- Core composes the whole message (a subject as a message id, then the trailers `Hephaestus-Task`, `Hephaestus-Step`, `Hephaestus-Agent`, `Hephaestus-Model`, `Hephaestus-Effort`) and the identity of the Hephaestus GitHub App. The orchestrator runs no git and parses the bundle's header only.
- A step kind that is not checkpointed cannot be asked for a checkpoint; what it left in the tree is discarded by the next checkout.

**Never:**
- Push, import, or scan the bundle (3.7), delete the result bundle (the loop does, once the push is journaled), or call any of this from the loop (3.14).
- Use `/workspace/.git`, mount the data or mirrors volume, bind a host path, or give the container a credential.
- Edit the epics, the publishing workflow, or the architecture.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Changed tree | Files edited, added, deleted; Agent made commits | One commit, parent the last pushed tip, tree equal to the work tree, Core's message and identity; one bundle with one head and one prerequisite | N/A |
| Unchanged tree | Tree equals the tip's, even after Agent commits | No commit, no bundle, no file; a result that says unchanged | N/A |
| Hostile `.git` | `/workspace/.git` holds a config with `core.fsmonitor`, a hook, a `.gitignore` of everything | None of it runs or is read; the commit is made | N/A |
| Interrupted step | Step container timed out or crashed | The checkpoint is made as for any end | N/A |
| Bad archive | Link entry, parent path, extra entry, other name, over the bound, header of two heads or another prerequisite | Refused, nothing written to the bundles directory | `infrastructure-error` message id in `en` and `pl` |
| Bad base bundle | Wrong checksum, outside the directory, two heads, another tip | Refused before a container, or the container exits non-zero | Message id in `en` and `pl` |
| Checkpoint hangs | Past its limit | Container removed by force, timed out | Message id in `en` and `pl` |
| Not checkpointed | `Review`, `Planning`, `Readiness` | A request cannot be made for it | `ArgumentException` |

</frozen-after-approval>

## Code Map

- `src/Hephaestus.Core/Workspaces/StepKind.cs`, `CheckpointCommit.cs`, `CommitIdentity.cs`, `CheckpointRequest.cs`, `CheckpointResult.cs`, `BundleHeader.cs`, `BundleReference.cs` (gains `Prerequisite`), `Ports/IWorkspaceRuntime.cs` (`CheckpointAsync`) -- the kinds, the message, the identity, the request, the result, the header parser.
- `src/Hephaestus.Workspaces/CheckpointScript.cs`, `DockerWorkspaceRuntime.Checkpoint.cs`, `TarFiles.cs` (reading one file back), `WorkspaceLabels.cs`, `WorkspaceRuntimeOptions.cs`, `CheckoutScript.cs` (shared bundle check), `DockerWorkspaceRuntime.Checkout.cs` (shared hardening and wait).
- `src/Hephaestus.Core/Localization/MessageIds.cs`, `Messages.en.resx`, `Messages.pl.resx` -- subject and failures.
- `docs/step-containers.md`, `docs/security/threat-model.md`, `README.md`, `deferred-work.md`; tests beside each, `CheckpointEngineTests` for CI.

## Tasks & Acceptance

**Execution:**
- [x] Core: step kinds, commit, identity, request, result, header parser, reference, port, messages; tests.
- [x] Workspaces: checkpoint script and container, reading the result, labels, options; tests on the script with real git, on the container against the Docker double, on the archive reader.
- [x] Engine test for CI, documentation, story `done` in `sprint-status.yaml`.

**Acceptance Criteria:**

| Criterion | Shown by |
|-----------|----------|
| One commit, parent the last pushed tip, own git directory, flattened | `CheckpointScriptTests`, `CheckpointEngineTests` |
| Hostile `.git` never used | `CheckpointScriptTests`, `CheckpointEngineTests` |
| Unchanged tree, no commit and no bundle | `CheckpointScriptTests`, `CheckpointContainerTests` |
| Core's message, identity, trailers | `CheckpointCommitTests`, `CheckpointScriptTests` |
| One regular bounded file, bundle reference; any other archive is an infrastructure error | `CheckpointContainerTests`, `BundleHeaderTests` |
| Not checkpointed, discarded | `StepKindTests`, `CheckpointRequestTests`, `CheckoutScriptTests` |

## Implementation Notes

- Unattended run: no human gate, no Docker daemon where this was built, and the implementation was written directly from the spec rather than by a subagent, because the investigation was already in this session. `CheckpointEngineTests` (the real tools image) is written for CI and skips without `HEPHAESTUS_TEST_TOOLS_IMAGE`, as `CheckoutEngineTests` does; every row of the matrix has a test that runs here: `CheckpointScriptTests` runs the script itself with the git and shell of the machine against a real workspace and bundle, `CheckpointContainerTests` the container definition and what comes back against the Docker double.
- The checkpoint is `IWorkspaceRuntime.CheckpointAsync`, awaited by the caller after the step-exited Signal; it shares `RunHelperAsync` with the checkout (verify the file, volume, image, replace by name, place the bundle, start, wait, collect, remove, delete), so the two helpers cannot drift. The shared shell for the three bundle checks is `CheckoutScript.VerifyBundle`.
- Unchanged is exit code 30, not the absence of a file: a container that ended with 0 and left nothing is `workspace.checkpoint.result-missing`. The bundle's tip and prerequisite are read from its header as text (`BundleHeader`), so the orchestrator runs no git on what a container returned.
- The mount is read-only, which the spec asks for and the architecture does not require; the index lives in the checkpoint's own git directory. A nested repository is dropped from the index (`find -exec`, one entry at a time, for the busybox of the tools image) and recorded in `deferred-work.md`, as are the loop wiring and the bot user's id in the commit address.
- `StepKind` did not exist; it is added to Core with the tags, and `IsCheckpointed` is the one place that says which kinds are kept.

## Spec Change Log

## Review Triage Log

| Finding | Verdict | Route | Evidence |
|---------|---------|-------|----------|
| Unchanged path deletes the result file and throws when the Task's directory does not exist | medium | patch | `DeleteBundleFile` is used (`An_unchanged_tree_needs_no_directory_for_a_result_that_was_never_made`). |
| Nested-repository guard swallows failures and walks `./.git` | medium | patch | `-path ./.git -prune`, and a post-check that stops with exit 31 on any new link to a commit; a nested repository at a path the tip tracks as a submodule is reset to the tip's entry (`A_submodule_the_tip_tracks_is_not_deleted_or_moved...`, `A_nested_repository_whose_git_is_a_file...`). |
| The nested-repository removal is shown only with GNU `find` | medium | patch | `CheckpointEngineTests` plants one and runs in the tools image in CI. |
| An unreadable file fails the whole checkpoint on every retry | medium | patch | `git add --all --ignore-errors`; the rest is kept (`A_file_the_step_user_cannot_read_does_not_stop_the_checkpoint_of_the_rest`). |
| `read-tree` baseline is not pinned: no test has a tracked file that `.gitignore` names | medium | patch | `A_tracked_file_the_gitignore_names_stays_tracked...` and `A_gitignore_of_everything_does_not_stop_the_commit...`. |
| An Agent's `.gitignore` drops new files silently | low | none | It is what `git add -A` does for any developer; Validation runs on the pushed commit and fails when the file mattered. Recorded in ADR-0023. |
| A prerequisite's subject in another encoding, or very long, makes a good bundle unreadable | medium | patch | The header is read as bytes, only the commit ids are decoded, the bound is 64 KiB (`A_prerequisite_whose_subject_is_in_another_encoding_or_very_long_is_still_read`). |
| A commit composed for another Task, step run, or kind can be requested | medium | patch | `CheckpointCommit` carries them and `CheckpointRequest` compares (`A_commit_composed_for_another_task_step_run_or_kind_is_refused...`). |
| A step container still running on the volume is not detected | medium | patch | `RequireStepEndedAsync` refuses a running, restarting, or paused one, and deletes the base bundle (`CheckpointContainerTests`). |
| Disk used inside the container is unbounded and the limits are shared with the checkout | low | defer | Recorded in `deferred-work.md`; the bound is enforced where it can be on every storage driver. |
| The result read ignores cancellation | low | patch | The copy looks at the token between reads (`A_checkpoint_that_is_cancelled_while_the_file_is_read...`). |
| `CommitIdentity` does not require a well-formed address | low | patch | One `@` between a name and a host, no space. |
| Polish wording mixes "punkt kontrolny" and "zapis" | low | patch | One term, `checkpoint`, as the checkout messages use `checkout`. |
| No parity test for the new message ids | false | none | `MessageCatalogTests` checks every declared id in both languages and the same placeholders. |
| Documentation tests pin wording | low | none | The same pattern as `CheckoutDocumentationTests`; the claims rest on the script, container, and engine tests, which the threat model names. |
| Threat model T10 still says "to be verified by test" | low | patch | Reworded to name `CheckpointContainerTests` and `BundleHeaderTests`, and Story 3.7 for the import. |
| Base bundle "outside the directory" has no test | false | none | `BundleReference` refuses a path that could leave the directory, so the refusal cannot be reached with a valid request; the branch is shared with the checkout. |
| Spec status, tasks, and log disagree | false | none | The spec is `in-review` while review runs; the tasks are marked and the tracking file is set to `done` at the end, as the run's instructions say. |

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: no warnings
- `dotnet test --solution Hephaestus.slnx` -- expected: all pass; real-Engine tests skipped without Docker
- `python3 eng/check-coverage.py TestResults` -- expected: line coverage at or above 80%
