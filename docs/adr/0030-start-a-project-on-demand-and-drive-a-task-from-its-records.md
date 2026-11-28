# ADR-0030: Start a Project on demand with a window, and drive a Task from its records, one step at a time

- **Status:** Accepted
- **Date:** 2026-11-28
- **Spine:** AD-1, AD-5, AD-6, AD-9, AD-13, AD-16, AD-17, AD-18

## Context

The parts of a run exist (Signals, the runtime, the gateway's push, the adapters, context, Validation, publication) and nothing joins them. The Owner needs a command that says a Project may be worked, a rule for which Task starts, a snapshot of the configuration the Task will use, and the steps from the labelled issue to the
pull request, without the loop waiting for a container (AD-18) and without a restart losing or repeating anything that was recorded (AD-1, AD-5).

## Decision

**A start is a Signal.** `project start` and `project stop` call the control API, which appends a Signal of the kinds `start-requested` and `stop-requested` from the source `control` and answers `202` with the Signal's sequence as its id; `signal show` reads its status. The
Signal is the Owner's own, so it has no actor to admit. The loop applies it to a **window** record in the transaction that marks it: a start with a window open is applied with the note `window-already-open` and opens none, and the database refuses a second open window of a Project
(a filtered unique index), so two starts in one batch cannot open two. A window ends when its Project has no `queued` Task (`drained`) or when it is stopped (`stopped`).

**One dispatch rule, in one place.** `DispatchPolicy` is the whole rule: only a `queued` Task of a Project with an open window, none while a Task runs, the lowest issue number first. `IUnitOfWork.DispatchAsync` makes the move to `running` only while the Task is `queued` and no Task is `running`, and
a filtered unique index on the Task's state makes the database refuse a second `running` Task, so the invariant does not rest on the loop alone.

**The snapshot is read once, validated, and stored whole.** The loop reads `hephaestus.yml` from the Base Branch through the gateway (`ProjectConfigurationReader`, which `project check` shares), resolves the image through the runtime, and stores one `TaskSnapshot` with the commit, the configuration, and the
image's reference, digest, architecture, and C library, as one document with the commit and digest as columns of their own. A Task that starts again keeps it. Every later step reads it, never the file.

**Steps are rows, and the next thing is decided from them.** A `StepRun` is planned with its deadline in the transaction that causes it (`PlanStepRunAsync`, or the dispatch for the first), its container is then asked for and left, and its status goes `planned`, `started`, `ended` (by the runtime's step-exited Signal, in the transaction that marks that
Signal), `checkpointed` (with the push intent), and, for the last Validation command, `concluded` (with the publication intent). A change is made only from the statuses it may be made from, so a change recorded twice is made once, and a Task's move is made only while the Task is where the change found it. Each
tick, a `running` Task is advanced from its last step run, the journal, and the snapshot: nothing is held in memory, so a loop that starts again carries on. The loop calls only what returns: `StartStepAsync` and `StartValidationAsync` return when the container has started, and checkout and checkpoint are helper containers that git
runs in for seconds; it never calls a probe, and the architecture test says so.

**What an Agent's step says is read where it ended.** Applying the step-exited Signal, the loop gives the stored stream to the adapter, classifies how it ended in the order of AD-11 (the runtime's cause, then the adapter's reading, then Core's), and validates the result by strict deserialization. A result that needs a decision
is read as the Escalation it will raise, so one that cannot be asked of the Owner is an error of the step and not of the loop.

**A failure ends the Task, for now.** A step that does not end well moves the Task from `running` to `failed` with the intent of its label, in the same transaction as the step's end, so the slot is not held for ever by a Task nothing can carry on. The class is recorded as far as the runtime and the adapter name it. The bounds and retries, the comment on the issue, and the Correction Loop are
Stories 3.15, 3.16, and Epic 4, which change this one rule.

**The runtime is the loop's, found when first needed.** `OrchestratorLoop` takes the runtime, the mirrors, the Methodology catalog, the output store, and the adapters. The host gives it the runtime through `LazyWorkspaceRuntime`, which finds the registered one on the first call, so a host that starts no Task needs neither the lock of the Agent
binaries nor the Docker client.

## Alternatives considered

- **A queue in memory, or the window as a flag on the Project.** A window that is a record survives a restart and is the audit of when work was allowed; a flag cannot say that two starts were one.
- **A state machine in a class of its own that holds a Task's position.** The records already say where a Task is, and a second place that must agree with them is where a restart goes wrong.
- **Waiting for the step in the tick.** AD-18 forbids it: cancels, labels, and answers would wait for a container.
- **Failing the Task only after bounded retries.** That is Story 3.16; a Task that could not make a checkout must not hold the slot while it is built.
- **Stopping a start with an empty Queue from closing its window at once.** FR-16 says a start on demand ends when the Queue is empty.

## Consequences

- A Task with a window open is started within a tick of the Owner's start; a Task that cannot be started is said so in the log and tried again after a minute, until Story 3.15 says it on the issue.
- A step lost with the host (a `started` step run whose container is gone) is not recovered: Story 3.17 removes its container, takes a checkpoint, and attempts it again as a new step run. A `planned` one is carried out by the next tick.
- A step run's usage, and the other Agent, the Reviewer, and the Findings, join the record with Stories 3.15 and 4.x.
