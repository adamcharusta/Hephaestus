# Failures, retries, and the Task's time

Story 3.16, AD-11, NFR-10, NFR-11, NFR-5, FR-20, AR-55, [ADR-0031](adr/0031-classify-every-failure-and-bound-every-retry.md).

Every step run that fails records exactly one class, and what a class allows is fixed: a step is attempted again only in the classes that are about the run and not about the code, and nothing is attempted without end.

## The classes

| Class | Decided by | Attempted again | When the bound is spent |
| --- | --- | --- | --- |
| `timeout` | The runtime: the deadline passed and it stopped the container. | Yes, unless the deadline was the end of the Task's time (below), as an Agent's step's always is. | The Task fails. |
| `infrastructure-error` | The runtime: the container was lost or killed for its memory, Docker failed. Also the loop: the mirror or the branch could not be read, the checkout or the checkpoint failed. | Yes. | The Task fails. |
| `agent-error` | The adapter: the CLI reported an error. Or Core: the result is missing or is not whole. | Yes. | The Task fails. |
| `unknown` | The adapter, for a CLI that failed and whose stream it cannot read. Anything no one positively identified. | Yes. | The Task fails. |
| `validation-failure` | The runtime: a Validation command exited with a status that is not 0. | No. | The Task fails at once; this release has no Correction Loop. |
| `usage-limit` | The adapter, only where the stream positively says so. | No. | The Task fails at once; Handoff and waiting for capacity are Epic 7. |
| `cancelled` | The runtime, for a step stopped by a cancel. | No. | Epic 9. |

The class is decided in one order (AD-11): the runtime's cause first, then the adapter's reading of a CLI that ended by itself, then Core. A real usage limit is recorded as `unknown` and attempted again within the bound until an adapter can positively identify it (FR-27, AR-64); it is never treated as one.

## Attempting a step again

A step that fails in `timeout`, `agent-error`, `infrastructure-error`, or `unknown` is attempted again as a new step run of the same step, with the next attempt number, the same Agent, Model, and Effort, and a fresh checkout of the last pushed tip. Before it, the work an Agent's step left in the Task volume is
kept as one commit and pushed to the Task branch through the gateway's checks, so the branch holds all work done and the next attempt starts from it (FR-20); a step that changed nothing has nothing to push, and a push the checks refuse holds the Task for you as any refused push does. A step is tried again up to `stepRetries`
times, so it has at most that many attempts and one: the default is 2 and `docker exec hephaestus hephaestus setting set stepRetries 3` changes it (0 to 10; 0 fails the Task at the first failure). Each step has its own count: the Implementation, and each command of Validation. A command is attempted again on the same pushed tip, and the commands before it
are not run again.

## The Task's time

A Task has a duration limit: the `limits.taskDuration` of its Project's file, held to the installation's `limit set --task-duration`, else the installation's, else two hours. It bounds the time the Task spends in `running`, which Hephaestus adds up whenever the Task leaves the state, so a Task that waited for you and was answered
has spent only the time it ran. Every step is stopped by the runtime no later than the moment that time reaches the limit, whatever its own limit: an Agent's step runs for the time the Task has left, and a Validation command for the shorter of its 30 minutes and what the Task has left.

When the runtime stops a step because the Task's time is spent, the step run is recorded as `timeout`, the work it left is pushed, no step is attempted again, and the Task fails with a comment that names the limit. A Task that failed is given its time anew when you opt it in again, because otherwise one that failed at its limit could never go on.

## When the Task fails

The Task moves from `running` to `failed`, the issue shows `hephaestus:failed`, and one comment is posted, in the language of the installation:

```text
**Failed:** `validation-failure` in `validation`.

Command: `dotnet test` exited with 1.
Attempts: 1. A failure of this class is not tried again.

Next: nothing until you apply the `hephaestus` label to this issue again. Work so far is on `hephaestus/7-add-a-login-page`.
```

For a step whose bound is spent the second line says how many attempts were made and what the bound was (`Attempts: 3. The bound of retries for a step is 2, and it is spent.`), and for a Task whose time is spent it names the limit (`The Task reached its duration limit of 2h in `running`, so no step is tried again.`). The class and the step are never translated.

## Trying again

A Trusted User who applies the `hephaestus` label to the issue of a `failed` Task returns the same Task to `queued` (AD-2): no second Task is made, its snapshot is kept, and it continues on the same branch from the tip last pushed, as a new Implementation with a fresh count of attempts. A label applied by anyone else changes nothing. Until Story 3.15, the Task
is dispatched when its Project has a window open, so start the Project again.
