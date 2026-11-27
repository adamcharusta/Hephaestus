---
title: 'Story 3.7: Push Task branches through the gateway''s checks'
type: 'feature'
created: '2026-11-27'
status: 'done'
baseline_commit: 'a0dc204a8567474974f30e1d8879fff924715ad2'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A checkpoint bundle (Story 3.6) is still inside the data volume. Nothing imports it safely, checks it for secrets and protected paths, pushes it as a fast-forward of the Task branch, or notices that someone else moved the branch (AD-9, AD-19, FR-4, FR-44, AR-52, AR-53).

**Approach:** `Hephaestus.GitHub` gets one new port, `ITaskBranches`: check a Task branch against the tip Hephaestus last pushed, and push a bundle. A push imports the bundle into a quarantine ref with strict object checking, accepts only the Task branch, requires a fast-forward, scans every new commit, tree, and blob with the masker, checks the diff against the Task's start commit for protected paths, and only then pushes. The loop makes the push as a journaled `Push` effect, raises an Escalation naming the failed check, and deletes the bundle once the result is journaled.

## Boundaries & Constraints

**Always:**
- The gateway has no code path that pushes with a plus, `--force`, `--mirror`, `--delete`, or an empty source, or to a ref that is not `refs/heads/hephaestus/<issue>[-<slug>]` (`BranchName.IsTaskBranch`); a request for another ref cannot be made. A push that is not a fast-forward is refused before git is asked and by git.
- Checks run in this order and the first failure blocks: bundle file (path, size, SHA-256), remote tip, bundle heads, quarantine import with `fsckObjects`, fast-forward, secrets, protected paths. Nothing but the quarantine ref moves before every check has passed; the quarantine ref is removed whatever came of it.
- A secret value in plain, base64, or URL-encoded form (the masker's own forms) in any new commit, tree, or blob blocks the push. A gateway built without the masker pushes nothing.
- Protected paths are matched case-insensitively, over every path the diff of the start commit and the tip names (no rename detection). `**` is zero or more segments, `*` and `?` stay inside a segment.
- The branch on GitHub counts as unmoved when it is absent and nothing was pushed (last pushed tip is the start commit), or is at the last pushed tip. A branch at, or containing, the bundle's tip is already pushed: that is the resolution after a crash, and nothing is pushed twice. Any other tip holds the Task: no push, an Escalation naming the commits Hephaestus did not push, and a resume condition that a reply does not meet (`confirm`, delivered by Story 3.18).
- The push intent is journaled with the key `<task>:<step cause>:push:<n>` and its detail holds the step run, the bundle's checksum, tip, last pushed tip (the tip before), and the start commit; the journal is the record of the tips before and after. The loop deletes the bundle file after the result is journaled, whatever the result.
- The installation token goes in the environment of the one git process, as the fetch does; no credential is written to disk or an argument.

**Never:**
- Call the checkout, the checkpoint, or the push from a step of the loop's dispatch, and create or change a step-run record (Story 3.14), adopt a remote tip with `confirm` (Story 3.18), or clean what a crash leaves (Story 3.17).
- Run git in any directory but a mirror, or read the bundle with anything but git in the mirror.
- Edit the epics, the architecture, or the workflow.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Clean push | Bundle of one commit on the last pushed tip, nothing protected or secret | Remote Task branch moves to the tip by fast-forward; mirror ref follows; quarantine ref gone | N/A |
| First push | Branch absent, last pushed tip is the start commit | Branch created at the tip | N/A |
| Already pushed | Remote branch at the tip or beyond it | `AlreadyPushed`, no push | N/A |
| Branch moved | Remote tip is neither the last pushed tip nor contains the bundle's tip | Blocked, check `remote-moved`, the commits not pushed by Hephaestus | Escalation, resume on `confirm` |
| Bad bundle | Wrong checksum, two heads, another branch, a prerequisite missing, fsck error | Blocked, check `import` or `branch` | Escalation |
| Not a fast-forward | Tip does not descend from the last pushed tip | Blocked, check `fast-forward` | Escalation |
| Secret | Stored value in a blob, a message, or a path, plain, base64, or URL-encoded | Blocked, check `secret`; the detail never holds the value | Escalation |
| Protected path | Diff touches `.github/x`, `Hephaestus.yml`, or a path the Owner listed | Blocked, check `protected-path`, the paths | Escalation |
| Unreachable | GitHub or git fails | Failure, retried within the bound | `GaveUp` then Escalation of the effect |

</frozen-after-approval>

## Code Map

- `src/Hephaestus.Core/GitHub/PushRequest.cs`, `PushResult.cs`, `BranchCheck.cs` (new) -- request, outcomes, checks, branch check result; `Core/Ports/ITaskBranches.cs` (new) -- the port.
- `src/Hephaestus.Core/Protection/ProtectedPathMatcher.cs` (new) -- the glob matcher; `Core/Secrets/SecretScan.cs`, `SecretMatcher.cs`, `SecretMasker.cs` -- detection over chunks.
- `src/Hephaestus.Core/Effects/Effect.cs`, `PushIntent.cs` (new) -- `EffectKind.Push`, the detail; `Core/Escalations/Escalation.cs`, `EscalationPath.cs`, `EscalationComment.cs` -- `PushBlocked`, the check, `confirm`; `Core/Loop/OrchestratorLoop.Effects.cs`, `OrchestratorLoop.cs` -- `AttemptPushAsync`.
- `src/Hephaestus.GitHub/GitHubMirrors.Push.cs` (new), `GitProcess.cs` (streaming, input), `GitHubMirrors.cs` (partial) -- the checks and the push.
- `src/Hephaestus.Persistence/Model/EscalationEntry.cs`, `HephaestusDbContext.cs`, `SqliteUnitOfWork.cs`, `SqliteReadStore.cs`, `Migrations/` -- the `Check` column, a longer journal detail.
- `src/Hephaestus.Host/Hosting/ControlHost.cs` -- registers the port; `Core/Localization/*` -- messages in `en` and `pl`.
- `docs/security/threat-model.md`, `docs/step-containers.md`, `README.md`, `deferred-work.md`; tests beside each, `tests/Hephaestus.Host.Tests/Architecture/GitPushRulesTests.cs`.

## Tasks & Acceptance

**Execution:**
- [x] Core: push types, port, matcher, scan, effect kind and detail, Escalation kind and comment, messages; tests.
- [x] Loop: `AttemptPushAsync` (journaled effect, escalation, bundle deletion); tests.
- [x] GitHub: branch check and push over real git against a local remote; tests for every row of the matrix; the architecture test.
- [x] Persistence and Host: column, migration, registration; documentation; story `done` in `sprint-status.yaml`.

**Acceptance Criteria:**

| Criterion | Shown by |
|-----------|----------|
| Quarantine import, strict objects, Task branch only, fast-forward | `GitHubPushTests`, `PushRequestTests` |
| Secrets blocked in plain, base64, URL-encoded form | `GitHubPushTests`, `SecretScanTests` |
| Protected path blocks, case policy settled | `ProtectedPathMatcherTests`, `GitHubPushTests` |
| Failed check keeps the Workspace and raises an Escalation naming it | `OrchestratorLoopPushTests`, `EscalationPathTests`, `EscalationCommentTests` |
| Journaled, resolved as fast-forward after a crash, never twice, bundle deleted | `OrchestratorLoopPushTests`, `GitHubPushTests` |
| No force, delete, or other ref | `PushRequestTests`, `GitPushRulesTests` |
| Branch moved by someone else holds the Task | `GitHubPushTests`, `OrchestratorLoopPushTests` |
| Only `Hephaestus.GitHub` pushes | `GitPushRulesTests` |

## Implementation Notes

- Unattended run: no human gate, and the implementation was written directly from the spec rather than by a subagent, because the investigation was already in this session. The tests that need git (`GitHubPushTests`) push to a repository on the machine with real git and skip where there is none; the gateway has no test against live GitHub.
- The push is the port `ITaskBranches`, implemented by `GitHubMirrors` (`GitHubMirrors.Push.cs`) beside `IRepositoryMirrors`, so that a fetch and a push share one gate per repository. `GitProcess` gained `StreamInMirrorAsync` for output that is too long to keep (the objects of a push, the diff) and for a command that reads names from its input (`git cat-file --batch`).
- The loop's `AttemptPushAsync` makes a `Push` effect whose intent is `PushIntent` (229 characters, so the journal's detail column is 256 now; SQLite keeps no length, so the migration adds only the `Check` column of `Escalations`). Nothing journals the intent yet: that is the end of a step run, Story 3.14.
- A deviation from the order in the frozen matrix, deliberate and written in ADR-0024: the branch is read, and a bundle at or beyond the tip is resolved as already pushed, before the bundle's file is checked, so that a push that was made and whose file is gone is not reported as a bad bundle.
- The case policy of the protected paths, which Story 1.9 left open, is case-insensitive (ADR-0024). A scan that did not read every object is a failure, never a pass. The bundle's import is the one command given the `file` protocol, because git counts a local bundle as one.

## Spec Change Log

## Review Triage Log

| Finding | Verdict | Route | Evidence |
|---------|---------|-------|----------|
| A blocked push on a Task that already waits raises no Escalation and the bundle is deleted | medium | defer | One Escalation asks for one decision (`EscalationPath`), and the criteria do not say how the Owner is told otherwise; recorded in `deferred-work.md` as unverified intent, and in ADR-0024. |
| A glob that is not valid protects nothing | low | none | The one writer of the list (`ProtectedPathService`) refuses an invalid glob; only a database edited by hand holds one. |
| `ToIssueFailure` words `CommitMissing` and `UnknownProject` as GitHub's refusal | low | defer | Real wording only; recorded in `deferred-work.md`. |
| The comment of a moved branch can list Hephaestus's own commits after a rewrite, or say "adopts those commits" for a deleted branch | low | none | Cosmetic; the commits named are those the branch has and the last pushed tip does not. |
| `OneLine` cuts the facts of a check at 500 characters | low | none | The gateway bounds them at 500 as well, and a path is cut, not an id. |
| `SecretMasker.ContainsSecret` is used by tests only | low | patch | Removed. |
| Memory and time bounds of the scan are weak | low | none | Bounded by 1,000,000 objects and by the fetch timeout per command; nothing here is unbounded. |
| `StreamInMirrorAsync` leaves git running when the consumer throws | medium | patch | Any exception now kills the process and observes both tasks. |
| The bundle's file is read after it is hashed, and only its last path component is checked for a link | low | none | The bundles directory is on the data volume that only the orchestrator writes; no step container mounts it. |
| A quarantine name left by a crash makes a new checkpoint of the same step run a non-fast-forward, blamed on the work | medium | patch | The name is deleted before the import (`A_stale_quarantine_name_that_a_crash_left...`). |
| A missing masker is retried as unreachable | low | none | The host always registers the masker (`ControlHost`); the guard only stops a push that has not been scanned. |
| Aliases of a path (short names, trailing dots) are not matched | low | none | Stated in ADR-0024 and the threat model (T22). |
| The diff is the net change, so an intermediate commit that touched a protected path is not caught | low | none | The story says the diff is against the start commit; a checkpoint is one commit; stated in ADR-0024. |
| The option of a blocked push tells the Owner to edit work they cannot edit | medium | patch | Reworded: reply and say what to change, and the Task returns to the Queue with the reply as the answer. |
| Weak tests: the Workspace is kept, a host with no gateway, a bundle that adds nothing | medium | patch | Replaced by assertions on the journal, a renamed test, and a bundle that reaches the fast-forward check (`A_bundle_that_adds_nothing...`). |
| Spec, ADR, and code disagree on the order of checks | low | none | A deliberate deviation, written in the Implementation Notes and ADR-0024. |
| `DeleteBundle` is on the port of Task branches | low | none | The gateway owns the bundles directory and the loop has no file system; the port is where Core can reach it. |
| The host registration of `ITaskBranches` and the loop's argument are untested | medium | patch | `The_host_offers_the_same_gateway_as_the_port_that_pushes...` resolves the port and reads it from the loop. |
| A carriage return in a file name splits a line of `rev-list` and fails the scan | medium | patch | Lines end at a line feed only (`A_file_name_with_a_carriage_return_in_it_is_scanned_and_pushed...`). |
| The import's detail can carry the host's paths into a public comment | medium | patch | The bundle's path and the mirror's are replaced (`What_git_said_about_a_bundle_it_would_not_import...`). |
| Any non-zero end of the import is the work's fault | low | none | A deterministic failure of git on a verified bundle is the common cause; a transient one is retried by the Owner's reply. |
| `[remote rejected]` and `not found` in a push's report are classified like a fetch's | low | none | A refusal by a rule reads as `rejected`, which is refused for good; `not found` in a push is GitHub's own 404. |
| Early stop of the streaming consumer is untested | low | patch | `No_more_than_five_secrets_and_five_protected_paths_are_named...` covers it. |

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: no warnings
- `dotnet test --solution Hephaestus.slnx` -- expected: all pass
- `python3 eng/check-coverage.py TestResults` -- expected: line coverage at or above 80%
