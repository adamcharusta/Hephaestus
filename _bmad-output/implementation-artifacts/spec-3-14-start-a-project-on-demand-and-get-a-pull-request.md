---
title: 'Story 3.14: Start a Project on demand and get a pull request'
type: 'feature'
created: '2026-11-28'
status: 'in-progress'
baseline_commit: '83d06141a8d3dca4fd7273bc9ca51aee9d6693dc'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Everything the first run needs exists as a part (Signals, the runtime, the adapters, Validation, publication), but nothing joins them: no command starts work on a Project, no window records that it may be worked, and nothing dispatches a `Queued` Task, snapshots its configuration, runs its steps, or publishes its pull request (FR-14, FR-16, FR-2, AD-1, AD-17).

**Approach:** `project start` and `project stop` append Signals through the control API (202 with the Signal id; `signal show` reads its status). The loop applies them to window records, and each tick dispatches the one eligible Task (issue number order) of a Project with an open window, snapshots its configuration and image digest, and drives its step runs from recorded state: Implementation, checkpoint and push, Validation command by command, publication.

## Boundaries & Constraints

**Always:**
- Only the loop writes windows, step runs, snapshots, and the Task's move to `Running`, through `IUnitOfWork`; `Running` is entered only from `Queued`, in one transaction with the snapshot, the first step run, and the intent of the state label; the database refuses a second `Running` Task and a second open window of a Project.
- The Project Configuration is read from the Base Branch once, validated, and snapshotted with its commit id and the resolved image digest; every later step uses the snapshot, never the file.
- A step run is recorded with its deadline before its container starts; the loop never awaits a step: the step-exited Signal ends it, and what follows is decided from recorded state, so a tick can stop anywhere.
- The Task holds the single slot until publication moves it out of `Running`; a window closes when its Project has no eligible Task or the Owner stops it, and never stops a `Running` Task.

**Never:**
- Refuse-to-start comments, the Escalation of `needs-decision`, the Agent's no-change rule's comment, failure classes beyond the one each end maps to, retries, the Correction Loop, Review, Handoff, capacity, schedules, priority labels, dependencies, recovery of a step lost with the host, cancel (Stories 3.15 to 3.17, 4.x, 6.x); a second dispatch rule beside the issue number; a change to the epics, the architecture, or the workflow.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Start | Registered Project, no window | 202 with a Signal id; applied; one window opened | Not registered: rejected `project-not-registered` |
| Start again | Window open | Applied with the note `window-already-open`; no second window; the command says so | N/A |
| Stop | Window open / none | Window closed (`stopped`) / applied with `window-not-open` | N/A |
| Dispatch | Window open, `Queued` Tasks, none `Running` | Lowest issue number moves to `Running` with snapshot and step run | Config missing, invalid, or unusable: not started, reported in the log, tried again after a minute |
| Slot taken | A Task is `Running` | Nothing is dispatched | N/A |
| Happy path | Implementation completes, tree changed, push and Validation pass | Pull request published; Task `PullRequestOpen`; window closes when no Task is eligible | N/A |
| Step fails | Non-zero exit, timeout, invalid result, Validation fails, tree unchanged | Step run records its end and failure; Task moves to `Failed` with its label | N/A |

</frozen-after-approval>

## Code Map

- `src/Hephaestus.Core/Signals/`, `Windows/` (new), `Runs/` (new: `StepRun`, `TaskSnapshot`, changes) -- the model; `Tasks/DispatchPolicy.cs` -- the order.
- `src/Hephaestus.Core/Loop/OrchestratorLoop.Dispatch.cs`, `.Steps.cs` (new) -- dispatch, window closing, driving a Task, the step-exited Signal.
- `src/Hephaestus.Core/Ports/IReadStore.cs`, `IUnitOfWork.cs`, `ISignalInbox.cs`; `Projects/ProjectConfigurationService.cs` -- gains a read by Project.
- `src/Hephaestus.Persistence` -- `Windows`, `StepRuns`, `TaskSnapshots`, migration, the unit of work and the read store.
- `src/Hephaestus.Host` -- `SignalApi`, `project start|stop`, `signal show`; `ControlHost` wiring.
- `tests/Hephaestus.EndToEnd.Tests` -- the scripted Agent drives a Task from a labelled issue to a pull request on real SQLite and the real loop.
- `docs/dispatch.md`, `docs/adr/0030-*.md`, `README.md`, `EXPERIENCE.md` -- documentation.

## Tasks & Acceptance

**Execution:**
- [ ] Core model, ports, dispatch policy, loop phases, messages (en, pl).
- [ ] Persistence: tables, migration, unit of work, read store, inbox append with its id.
- [ ] Host: Signal API, commands, wiring, log lines.
- [ ] Tests at every layer, the end-to-end test, documentation, ADR, README, story `done` in `sprint-status.yaml`.

**Acceptance Criteria:**

| Criterion | Shown by |
|-----------|----------|
| 202 with a Signal id; window opened; status readable | `SignalEndpointsTests`, `OrchestratorLoopWindowTests`, `SignalCommandTests` |
| Second start opens no second window and says so | `OrchestratorLoopWindowTests`, `WindowJournalTests` |
| Dispatch order, `Queued` to `Running` only, never two `Running` | `DispatchPolicyTests`, `OrchestratorLoopDispatchTests`, `DispatchPersistenceTests` |
| Snapshot of configuration, commit id, digest; later steps use it | `OrchestratorLoopDispatchTests`, `DispatchPersistenceTests` |
| Implementation, checkpoint, push, Validation, publication; slot held | `OrchestratorLoopStepsTests`, `RunToPullRequestTests` |
| Window closes when no Task is eligible or the Owner stops it | `OrchestratorLoopWindowTests` |
| Scripted Agent from labelled issue to pull request | `RunToPullRequestTests` |

## Implementation Notes

- Unattended run: no Open Questions could be put to the Owner. Choices that the Owner would not notice were made and are recorded in ADR-0030.
