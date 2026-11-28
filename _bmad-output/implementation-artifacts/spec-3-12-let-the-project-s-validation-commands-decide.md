---
title: 'Story 3.12: Let the Project''s Validation commands decide'
type: 'feature'
created: '2026-11-28'
status: 'done'
baseline_commit: 'da2cf0137bec228ec3823abe793c394749a34c08'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The runtime starts only Agent steps, nothing runs the Project's Validation commands, and nothing decides or records what they came to, so an Agent saying it is finished is all there is (FR-31, FR-19, AD-6, AD-13).

**Approach:** One command is one container run, started through the step path after a fresh checkout of the pushed tip, in the Task's image, with no credential and the Project's egress list alone. A pure `ValidationRun` in Core is decided only by each container's exit status and cause, and writes `validation.json`.

## Boundaries & Constraints

**Always:**
- Each command is its own container run with its own step run and output file, started through `StartStepAsync`, so the hardening, proxy, deadline, masked output, and one step-exited Signal are the step's.
- A Validation container mounts the Task volume and nothing else of a step; a request with context, Methodology, or a secret is refused. Egress is `EgressPolicy.AllowedHosts` with no invocation hosts.
- Validation passes only if every command exited with status 0; the first command that does not pass ends it and the rest are `not-run`. A non-zero status is `validation-failure`, a stop at the deadline is `timeout`, a memory kill or a lost container is `infrastructure-error` (AD-11).
- Nothing an Agent reported is an input of the decision. Nothing a Validation run leaves in the tree is kept.

**Never:**
- Add a step-run table, wire Validation into the loop, add a `StepKind` for Validation, change the epics, the architecture, or the workflow, or add a message id.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| All pass | every command exits 0 | outcome `passed`, no failure class | N/A |
| Non-zero exit | command 2 of 3 exits 1 | `validation-failure`; command 3 `not-run` with no exit status, duration, or output | N/A |
| Deadline | command stopped by the runtime | `timeout`; status of the stop is ignored; output kept by the watcher | N/A |
| Memory kill or lost | cause `out-of-memory` or `lost` | `infrastructure-error`, never a pass | N/A |
| Tree changed | command writes files | next command is checked out afresh; no checkpoint can be made | N/A |
| Later step | results recorded | `validation.json` in the context | Undecided run refuses `ToJson` |
| Restart | container of the command exists | tree not made again; bundle discarded unread | N/A |

</frozen-after-approval>

## Code Map

- `src/Hephaestus.Core/Validation/` (new) -- `ValidationRun`, `ValidationCommandResult`, `ValidationOutcome`, `ValidationCommandRequest`, `ValidationJson`.
- `src/Hephaestus.Core/Workspaces/StepContainerRequest.cs` -- `IsValidation`; `src/Hephaestus.Core/Ports/IWorkspaceRuntime.cs` -- `StartValidationAsync`.
- `src/Hephaestus.Workspaces/DockerWorkspaceRuntime.Validation.cs` (new), `.Container.cs`, `.cs` -- the mounts and the skipped context volumes.
- `docs/step-containers.md`, `docs/adr/0028-*.md`, `README.md`.

## Tasks & Acceptance

**Execution:**
- [x] Core types, the port method, the runtime, tests (in memory and against an Engine).
- [x] Documentation, ADR, story `done` in `sprint-status.yaml`.

**Acceptance Criteria:**

| Criterion | Shown by |
|-----------|----------|
| Own container, fresh checkout, Task image at its digest, no credential, Project's egress list | `ValidationCommandRequestTests`, `ValidationContainerTests`, `ValidationEngineTests` |
| Passes only if every command exited 0; no Agent input | `ValidationRunTests` |
| Non-zero: `validation-failure`, rest `not-run`, status, duration, output recorded | `ValidationRunTests` |
| Deadline: `timeout`, output kept | `ValidationRunTests`; the watcher keeps output (`StepWatcherTests`) |
| Tree changes discarded, never checkpointed | `ValidationEngineTests`, `ValidationContainerTests` |
| Available as `validation.json` | `ValidationRunTests` |

## Implementation Notes

- Unattended run: no open question was left; choices are recorded here and in ADR-0028.
- Implemented directly from this spec, because the investigation was already in this session.
- No step-run table exists (Story 3.14), so "recorded" is `ValidationCommandResult` and `validation.json`; the output file is keyed by the command's step run id.
- Out-of-memory and lost are `infrastructure-error` as AD-11 says, not `validation-failure`.
- The command deadline (Task duration, at most 30 minutes) is a decision of this story; the epics name none.
- `validation.json` carries no command output; which part a Correction step is shown is for the story that dispatches Corrections.
- The engine tests need Docker and git and are skipped here; CI runs them.
