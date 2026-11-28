---
title: 'Story 3.16: Classify every failure and bound every retry'
type: 'feature'
created: '2026-11-28'
status: 'done'
baseline_commit: 'dd715f57502bd1359d856c95cdccb7ba002bd0f0'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A step that does not end well ends the Task at once (Story 3.14): a hiccup of the network, Docker, or an Agent costs the Owner an opt-in, nothing says on the issue what was attempted, a Task's steps can together outlast its duration limit, and a `Failed` Task cannot be opted in again (AD-11, NFR-10, NFR-11, NFR-5, AR-55, FR-20).

**Approach:** Every failed step run records one class, decided in one order (the runtime's cause, the adapter's reading, Core). `timeout`, `agent-error`, `infrastructure-error`, and `unknown` are attempted again as a new step run on a fresh checkout of the last pushed tip, up to the installation's `stepRetries` (two); the work of an Agent step that ran is checkpointed and pushed first. A step's deadline never passes the moment the Task's time in `Running` reaches its limit. When a bound is exhausted, the Task fails with a comment, and a Trusted User's Marker Label returns it to `Queued`.

## Boundaries & Constraints

**Always:**
- Only the loop decides, through `IUnitOfWork`, from recorded state; a retry is planned in the transaction that concludes the failed step run, and a tick can stop anywhere.
- A failed step spends a retry only for `timeout`, `agent-error`, `infrastructure-error`, `unknown`; `validation-failure` ends the Task while this epic has no Correction Loop; a class the adapter names as `usage-limit` is never retried or handed off here.
- The Task branch holds all work done: the work of a failed Agent step is pushed before the step is retried or the Task fails.
- Time in `Running` is a fact of the Task, kept by the database where the Task changes state, not inferred.

**Never:**
- The refuse-to-start comments, `needs-decision`, the no-change rule's own comment, Agent and model per step (Story 3.15); recovery after a restart (3.17); Handoff, capacity, Correction Loop, Review (Epics 4, 7); cancel (Epic 9); a change to the epics, the architecture, or the workflow.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Retry | Step ends `timeout`, `agent-error`, `infrastructure-error`, or `unknown`, attempts within the bound | Work pushed, new step run of the same step, attempt + 1, fresh checkout of the last pushed tip; Task stays `Running` | N/A |
| Bound exhausted | The failed attempt is attempt `stepRetries` + 1 | Task `Failed`, label, one comment (attempts, class, step or command, branch) | N/A |
| Validation fails | A command exits non-zero | Task `Failed` with its comment; no retry | N/A |
| Usage limit unidentified | Adapter cannot name it | Recorded `unknown`, retried within the bound | N/A |
| Task duration | Step stopped when Task time in `Running` reaches its limit | Step `timeout`, work checkpointed, no retry, Task `Failed`, comment names the duration limit | N/A |
| Deadline | Any step planned | Deadline no later than the moment Task time in `Running` reaches the limit | N/A |
| Opt in again | Trusted User applies the Marker Label to a `Failed` Task | Same Task `Queued`, label shown, continues on its branch | Not trusted: nothing changes |

</frozen-after-approval>

## Code Map

- `src/Hephaestus.Core/Failures/` -- `FailureClass`; gains `FailureClassifier` (the one order), `StepRetries` (the decision), `TaskFailureNotice` (the comment's facts and text).
- `src/Hephaestus.Core/Loop/OrchestratorLoop.Steps.cs` -- `AdvanceOnceAsync`, `FailStepAsync`, `DecideStepExitedAsync`; retry, preservation, and the end of a Task. `OrchestratorLoop.Dispatch.cs` -- first deadline. `OrchestratorLoop.cs` -- `Decide` for a marker on a `Failed` Task. `OrchestratorLoop.Effects.cs` -- `CommentTextAsync`.
- `src/Hephaestus.Core/Runs/` -- `StepRun.Attempt`, `StepRunChange.Retry`, `StepDeadlines`; `Tasks/TaskRecord.cs`, `TaskState.cs` -- time in `Running`, the lifecycle row; `Settings/InstallationSettings.cs` -- `stepRetries`.
- `src/Hephaestus.Persistence` -- `StepRuns.Attempt`, `Tasks.RunningMillis` and `RunningSince` with a trigger, one migration; `SqliteUnitOfWork`, `SqliteReadStore`.
- `tests/Hephaestus.Core.Tests/Loop/LoopStores.cs` -- the in-memory unit of work follows the real one. `docs/dispatch.md`, ADR-0031, `README.md`.

## Tasks & Acceptance

**Execution:**
- [x] Core: classifier, retry decision, failure notice and messages (en, pl), deadlines, setting, lifecycle row, loop changes.
- [x] Persistence: migration, entries, unit of work, read store.
- [x] Tests at every layer, including the bounds named by AR-55; documentation, ADR, README; story `done` in `sprint-status.yaml`.

**Acceptance Criteria:**

| Criterion | Shown by |
|-----------|----------|
| One class, decided in one order | `FailureClassifierTests`, `OrchestratorLoopFailureTests` |
| Retry as a new step run on the last pushed tip, within `stepRetries` | `OrchestratorLoopRetryTests`, `RetryPersistenceTests` |
| Exhausted bound or failed Validation: `Failed`, label, comment, branch holds the work | `OrchestratorLoopRetryTests`, `TaskFailureNoticeTests` |
| A Trusted User's Marker Label returns a `Failed` Task to `Queued` | `OrchestratorLoopOptInTests` |
| Unidentified usage limit is `unknown` and retried | `OrchestratorLoopRetryTests` |
| Deadlines on Agent runs and Validation, the retry bound, the Task duration limit | `OrchestratorLoopRetryTests`, `TaskDurationTests`, `RunningTimePersistenceTests` |

## Implementation Notes

- Unattended run: no Open Questions could be put to the Owner. The choices the Owner would not notice are recorded in ADR-0031.
- This closes three entries of `deferred-work.md` that Story 3.14 left for this story: the bounded retry of a step whose checkout or bundle could not be made, the Task duration bound across all steps, and the comment on the issue where a step ended the Task, and where an Agent made no change. They are left in the file, which is append-only.
- Story 3.14's tests that expected the first failure to end the Task now set `stepRetries` to 0 (`RunRig.NoRetries`) and keep what they showed; `LoopStores` follows the unit of work's new rules.

## Review Triage Log

| Finding | Verdict | Route and evidence |
| --- | --- | --- |
| A Task that ended because the Agent changed nothing is told "a failure of this class is not tried again", which contradicts `agent-error` being retried | medium | patch: its own end (`no-change`) and sentence, and a test of the comment and the label |
| A checkpoint that fails once, for a step that ended well, loses the Agent's work: the step is failed and attempted again from the last pushed tip | medium | patch: a failed checkpoint is tried again by each tick, up to the start-attempts bound, before the step fails |
| The tick counter of a step is not cleared when its start succeeds, so a later failure to keep its work gives up at once | low | patch: cleared when the step starts |
| The keep-work path is silent when GitHub is not connected | low | patch: a line in the log |
| "tried again up to 1 times" is wrong in English and in Polish | low | patch: the sentence names the bound and has no counted noun |
| `docs/failures.md` says a `timeout` is always attempted again, while an Agent's step's never is | medium | patch: the table says when |
| A command of Validation over several lines breaks out of its code span in the comment | low | patch: line ends become spaces, with a test |
| `using` out of order; ADR bullet garbled; the trigger's dependence on `UpdatedAt` is unwritten | low | patch |
| "Next:" says to apply the label, and a failed Task is dispatched only while its Project has a window | low | not changed: the fixed words are the UX template's, a Project's Queue needs `project start` for any Task, and `docs/failures.md` says so; Story 3.15 lifts it |
| The comment gives the class and not the cause (the exit status of the CLI, "mirror unreachable") | low | not changed: the acceptance criterion asks for the class and the command or step; the cause is in the log with the Task id |
| An unreadable stored result at the end of Validation retries the last command, which passed | low | not changed: it needs a damaged store, and the attempts are bounded |
| Time is zeroed on every opt-in, so a Trusted User can grant a Task time again | false | by design and written in ADR-0031: a Task that failed at its limit could not otherwise go on, and the Trusted User is who decides |
| The retry is logged though the Task may have left `running` before it was planned | false | one writer: the loop reads the Task, writes, and only its own effects phase, which runs later, moves a Task |
| A push that ends with no Escalation holds the Task in `running` | false | a push that is refused or given up raises an Escalation (ADR-0024); the wait is the one `AfterCheckpointAsync` already had |
| `MoveTaskTo` has no state guard | false | pre-existing, in the same transaction as the Signal that was read in the same tick by the one writer |
| A deterministic out-of-memory step is attempted again, and one second of slack is arbitrary | false | AD-11 and ADR-0028 class a memory kill as `infrastructure-error` that spends the bound; the slack's reason is written beside it |
| The payload of the comment is not validated field by field | false | only Core writes the journal, and the payload is not read from a repository |
