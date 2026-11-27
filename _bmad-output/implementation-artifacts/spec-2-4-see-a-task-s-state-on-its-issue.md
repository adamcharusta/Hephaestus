---
title: 'Story 2.4: See a Task''s state on its issue'
type: 'feature'
created: '2026-11-27'
status: 'done'
baseline_commit: '12b4538b3a2b21879aa7184b5b0ddcec807327c2'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A Task's state is only in the database, so the Owner cannot follow or filter Tasks on GitHub, and nothing yet makes an effect on GitHub safe to repeat, retry, or recover after a crash (FR-11, AD-2, AD-5, NFR-9, NFR-12).

**Approach:** The loop journals, in the transaction that creates a Task or sees a state label changed by hand, the intent to show the Task's state as one `hephaestus:<state>` label. After the commit it reads the issue's labels, changes only what is wrong, and records the result. An unreachable GitHub is retried at recorded times within a configured bound, then the Task moves to `WaitingForOwner`; a refusal for good ends the effect at once with the same move.

## Boundaries & Constraints

**Always:**
- Only the loop writes the journal, through `IUnitOfWork`; it is append-only (enforced by the database) and every line names the Task and its cause. The key is `<scope>:<cause>:<effect-kind>:<ordinal>` and is passed to the port method as a parameter.
- The label is a projection: it is put back whoever changed it, decided by reading the labels as they are, never as intent (AD-2). Hephaestus's own label events are not answered. Only state labels are touched.
- An effect starts by reading the far side, so a change made and not recorded is found and recorded, not made again.
- Every wait and retry is bounded: setting `effectRetries` (0-20, default 5), waits doubling from 30 s to 10 min; a rate limit is a wait and not a failure.
- Documentation says that in v0.1 a Task whose issue can no longer be written to is visible only in the log.

**Never:**
- Move a Task that has ended, or is already waiting for the Owner, to `WaitingForOwner`; move any Task to `Failed` for an effect.
- Add comments, Escalation, or any other effect kind (Stories 2.5 and 2.9).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Opt in | Marker Label by a Trusted User | Task `Queued`; intent journaled in the same transaction; issue gets `hephaestus:queued`; result `applied` | N/A |
| By hand | state label removed or replaced by anyone but the App | intent with the Signal as cause; labels read; right label written back | N/A |
| Crash | intent journaled, no result | first tick reads labels; `already-in-place` or applied once | N/A |
| Outage | GitHub unreachable | retry entry with next-attempt time; Task keeps state; at the bound `gave-up` and `WaitingForOwner` | error logged with Task id |
| Refusal | issue deleted, App removed, permission withdrawn | no retry; `refused` with reason; `WaitingForOwner` | error logged with Task id |

</frozen-after-approval>

## Code Map

- `src/Hephaestus.Core/Effects/Effect.cs` -- journal vocabulary: kinds, results, key, retry waits.
- `src/Hephaestus.Core/Loop/OrchestratorLoop.cs`, `.Effects.cs` -- Signals decide intents; the second phase makes effects.
- `src/Hephaestus.Core/Ports/` -- `ICodeHost` (read and change labels), `IReadStore` (journal, unfinished effects), `IUnitOfWork` (`RecordEffectAsync`, intents in `ApplyAsync`).
- `src/Hephaestus.GitHub/GitHubGateway.Issues.cs` -- labels through the installation that holds the repository; outage, wait, and refusal told apart.
- `src/Hephaestus.Persistence/` -- `EffectJournal` table (migration with append-only triggers), unit of work, read store.
- `docs/state-labels.md`, `docs/intake.md`, `README.md` -- what the Owner sees and when GitHub fails.

## Tasks & Acceptance

**Execution:**
- [x] Core: label naming, effect vocabulary, ports, loop effect phase, `effectRetries` setting.
- [x] GitHub: read and change labels as a set; classify failures.
- [x] Persistence: journal table, intents with the state change, results, move to `WaitingForOwner`.
- [x] Host: log each effect that could not be made as an error with the Task id.
- [x] Tests for every row of the matrix; documentation and README.

**Acceptance Criteria:**
- Given each acceptance criterion of Story 2.4 in `epics.md`, then a test named for it passes.

## Implementation Notes

- Cause of an effect that follows a failed one is the failed one's cause, so keys keep the documented `g<signal-sequence>` form and differ in ordinal.
- A Task moves to `WaitingForOwner` from `Queued`, `Running`, `WaitingForCapacity`, and `PullRequestOpen`; the lifecycle diagram lists only `Running`, but the story's criteria name this move for a Task that has not started.
- Hand-written migration `AddEffectJournal`; `HasPendingModelChanges` test passes.

## Spec Change Log

## Review Triage Log

One row per finding of the three review layers (blind, edge-case, verification-gap), with the verdict and what settled it.

| Finding | Verdict | Evidence and route |
|---------|---------|--------------------|
| Task record read once per tick goes stale after an earlier effect moves the Task | medium | A second effect of the same Task decided from `Queued`. Patched: the loop remembers the moves of the tick and decides from them; a test covers two effects due together. |
| Problems found before a failing recording are lost to the tick report | medium | `RunEffectsAsync` returned its run only on success. Patched: the run is created by the tick and survives the exception; a test covers it. |
| Effect phase skipped while a Signal fails to apply | medium | It ran only when `failure is null`. Patched: it runs in its own guard after the Signals; a test covers it. |
| Hand-made label changes journal and ask GitHub once each | medium | Anyone who can label an issue could cause a read per change. Patched: a change is not journaled while an effect that shows the Task's state is waiting; tests cover a flood and other Tasks. |
| Cancellation while an `ApiException` surfaces records a refusal | low | Real but narrow, and a direct correction. Patched in the gateway with `ThrowIfCancellationRequested`; a test covers it. |
| Tasks made by Story 2.2 get no label because they have no intent | medium | Found by the blind reviewer's upgrade question. Patched: the migration journals an intent for each existing Task; a test migrates a database that holds Tasks. |
| Rate-limit waits have no outer bound | low | Waiting out rate limits is by design; each wait is bounded by two hours. Deferred. |
| Gateway exceptions are taken for an outage | low | The exception is reported with the tick and the attempt is bounded; a separate reason adds a path for a fault that should not happen. Rejected. |
| 401, 403, 404, suspended installation are refused for good | false | A 403 that is a rate limit is an outage in `IsOutage`; the other cases are what the criteria call refusals, and `ReadFile` and the other reads classify them the same way. |
| No foreign key from the journal to Tasks, so a missing Task poisons the loop | false | The intent is written in the transaction that writes the Task and no Task is ever deleted; the state cannot arise. |
| One effect costs about eight GitHub requests | low | Efficiency, not correctness. Deferred. |
| Log lines are English literals and the retry warning has no Task id | false | The log is machine surface (AD-21), like the rejection lines before it; each attempt is in the journal with its key. |
| `Queued` to `WaitingForOwner` is not in the lifecycle diagram | medium | The criteria of Story 2.4 name this move and only `Queued` Tasks exist. Deferred: the diagram is changed through `bmad-architecture`. |
| `not-connected` and `key-unreadable` spend the retries quickly | low | Documented in `docs/state-labels.md`. |
| Own label events are answered when the App's slug is unknown | low | Costs a read and one `already-in-place` line, and ends there. Rejected. |
| Journal retention | low | Not in any criterion. Deferred. |

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: no warnings
- `dotnet test --solution Hephaestus.slnx` -- expected: all pass
- `python3 eng/check-coverage.py TestResults` -- expected: line coverage at or above 80%
