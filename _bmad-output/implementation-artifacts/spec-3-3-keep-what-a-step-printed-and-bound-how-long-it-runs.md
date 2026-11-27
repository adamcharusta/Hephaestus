---
title: 'Story 3.3: Keep what a step printed and bound how long it runs'
type: 'feature'
created: '2026-11-27'
status: 'done'
baseline_commit: '95aa99a9a4e7339e9856b85e5e96a32074d326a3'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A step container that is started is not watched: what it prints is lost with the container, nothing reports that it ended, nothing stops it, and no test installation exists to show a step end to end (AD-18, FR-4, FR-19, FR-53, AR-37).

**Approach:** `IOutputStore` in Core, implemented by Persistence as one file per container run indexed in the database. The Docker runtime watches each container it starts: it follows the log through the masker into the store, stops the container at its recorded deadline, appends exactly one step-exited Signal, and removes the container and the step run's two volumes. The loop applies the Signal with no change for now. A harness in the end-to-end project starts an installation on real SQLite and Docker and drives a step with a scripted `IAgentAdapter`.

## Boundaries & Constraints

**Always:**
- The runtime alone ends a container early, and only at the deadline the request carries; the loop never awaits a step, and `StartStepAsync` still returns as soon as the container is started.
- Output reaches `IOutputStore` only through the masker, across chunk boundaries; stdout and stderr are one file in arrival order; what was written before a timeout is kept and indexed.
- One step-exited Signal per step run: source `runtime`, source event id the step-run id, so a second append is a no-op. It is appended before the container is removed, so a crash between the two leaves a container to find, never a step nobody heard of.
- Every Docker object the runtime makes for a step run is removed when the step ends: the container and the step run's context and Methodology volumes (not the Task volume).

**Never:**
- Write Task or step-run state from the runtime or the store (only the loop does, 3.14); record refused hosts (3.4); classify a CLI that ended by itself, retry, or recover a step after a restart (3.16, 3.17); start a container for checkout, checkpoint, or Validation (3.5, 3.6, 3.12).
- Give the loop a way to wait on the runtime, or stream output while a step runs.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Step prints and exits 0 | stdout and stderr lines | One file with both, indexed complete with its size; Signal `exited`, code 0; container and volumes removed | N/A |
| Secret in output | Value split across two chunks | The file holds `***` and no part of the value | N/A |
| Deadline passes | Container still running at `Deadline` | Runtime stops it; Signal `timeout`, its exit code; output so far kept | A stop that finds the container already gone is not an error |
| Out of memory | Container killed by the kernel | Signal `out-of-memory` with the exit code | N/A |
| Container gone | Removed from outside while watched | Signal `lost`, no exit code | N/A |
| Signal appended twice | Restart finds the exited container | Inbox keeps one; the output file is replaced by the new complete copy | N/A |
| Another Signal while a step runs | Label event appended mid-step | Applied within one tick | N/A |
| Docker or the database fails while watching | Transient error | Retried at an interval until it works or the host stops | Reported once per failure through the problem callback |

</frozen-after-approval>

## Code Map

- `src/Hephaestus.Core/Ports/IOutputStore.cs`, `src/Hephaestus.Core/Output/` -- the port (`BeginAsync` returns an `IOutputWriter`; `FindAsync`, `OpenReadAsync` for reads) and `OutputRun`, `StoredOutput`.
- `src/Hephaestus.Core/Signals/Signal.cs` -- `SignalKind.StepExited` (`step-exited`), `Signal.RuntimeSource`, optional `StepRunId`, `ExitCode`, `Cause`; `StepExitCause` in `Core/Workspaces`. `OrchestratorLoop.Decide` applies the kind with no change (the step-run write is 3.14).
- `src/Hephaestus.Core/Workspaces/StepContainerRequest.cs` -- gains `Deadline`.
- `src/Hephaestus.Core/Ports/IAgentAdapter.cs` -- the invocation half of the port only (`AgentInvocation`); the rest of AR-19 arrives with 3.8, 3.9, 3.11.
- `src/Hephaestus.Persistence/SqliteOutputStore.cs`, `Model/OutputFileEntry.cs`, a migration, `SqliteSignalInbox.cs`, `Model/SignalEntry.cs` -- the file under `<data>/output/<task>/<step-run>.log`, the `OutputFiles` index, the new Signal columns.
- `src/Hephaestus.Workspaces/StepWatcher.cs`, `MaskedOutputSink.cs`, `DockerWorkspaceRuntime.cs` -- the watcher (log copy, deadline, exit, Signal, removal) and its call from `StartStepAsync`.
- `src/Hephaestus.Host/Hosting/WorkspaceRuntimeSetup.cs`, `ControlHost.cs` -- composition; problems logged through the host's logger.
- `tests/Hephaestus.Workspaces.Tests/`, `tests/Hephaestus.Persistence.Tests/`, `tests/Hephaestus.Core.Tests/`, `tests/Hephaestus.EndToEnd.Tests/Harness/` -- unit tests on the Docker double, store tests on SQLite, the harness and its tests on a real Engine (CI).
- `docs/step-containers.md`, `docs/architecture-c4.md` if it names the store; `README.md` only if the reader's needs change.

## Tasks & Acceptance

**Execution:**
- [x] Core: `IOutputStore`, records, `StepExited` Signal and `StepExitCause`, `Deadline`, `IAgentAdapter` invocation record; loop applies the kind; tests.
- [x] Persistence: `SqliteOutputStore`, index table and Signal columns with a migration; tests.
- [x] Workspaces: `StepWatcher`, masked sink, runtime wiring; tests on the Docker double with a fake clock.
- [x] Host: composition and logging of problems.
- [x] Harness: an installation on real SQLite and Docker, a scripted adapter, tests of each criterion on a real Engine; CI runs them with the rest.
- [x] Documentation; story `done` in `sprint-status.yaml`.

**Acceptance Criteria:**

| Criterion | Shown by |
|-----------|----------|
| Raw stream through the masker to `IOutputStore`, one file per container run, indexed | `StepWatcherTests` (chunk-split secret, both streams, one file), `OutputStoreTests` (file and index), harness test on Docker |
| Exactly one step-exited Signal with step-run id, exit code, cause; container removed | `StepWatcherTests` (Signal fields, once, before removal, volumes), `SignalInboxTests`, harness test |
| Deadline recorded before start; the runtime alone stops the container; cause `timeout`; output kept | `StepWatcherTests` (fake clock), harness test (a step that sleeps past its deadline) |
| Another Signal applied within one tick while a step runs | harness test: a Signal is applied by the loop while the step container still runs |
| Harness with a scripted `IAgentAdapter`, real SQLite and Docker, no vendor call | `tests/Hephaestus.EndToEnd.Tests/Harness`, its tests |

## Implementation Notes

- Unattended run: no human gate. No Docker Engine can run containers where this was built (the daemon starts but cannot unpack an image), so the real-Engine tests are written against the CI Engine and skipped here; `RealEngine.Required` fails them on CI when no Engine answers. Every row of the matrix is covered by a test that runs here: `StepWatcherTests`, `MaskedOutputSinkTests`, `OutputStoreTests`, `SignalInboxTests`, `OrchestratorLoopTests`, and `HarnessTests` for the loop applying a Signal on a real installation.
- `IAgentAdapter` is defined in Core with the invocation half only (`DescribeInvocation`), because the criterion needs a scripted fake of it and the rest of AR-19 belongs to 3.8, 3.9, and 3.11. The records live in `Hephaestus.Core.Invocations` (a namespace `Agents` would collide with the static class `Agents`).
- `StepContainerRequest` gained the required `Deadline`; the Signal gained `StepRunId`, `ExitCode`, `Cause`; the database gained the `OutputFiles` table and three columns of `Signals` (migration `AddOutputFiles`). Refused egress hosts are not on the Signal until 3.4.
- The harness starts the real host (`ControlHost`, made visible to the end-to-end project) over real SQLite; with an `EngineFixture` it uses the real Engine and the fixture's installation id, network, and agents volume. The harness tests that need no Engine run everywhere.
- The context and Methodology volumes of a step run are removed with the container, which closes the item deferred from Story 3.2.

## Spec Change Log

## Review Triage Log

| Finding | Verdict | Route | Evidence |
|---------|---------|-------|----------|
| Deadline waits on the output store (`BeginAsync` retried before the deadline loop) | medium | patch | The output is now recorded beside the look at the container; a store that keeps failing is abandoned after the drain limit and the step still ends (`A_store_that_keeps_failing_does_not_hold_back_the_deadline...`). |
| Completion of the file and the sink not retried; a failed completion leaves no Signal | high | patch | Completion is retried until it works or the host stops; the sink takes what it holds once; the store records before it closes (`A_file_that_cannot_be_completed_is_completed_again...`, `Completing_again_after_a_write_failed...`, `A_completion_that_fails_because_the_database_does...`). |
| `CopyOutputAsync` rethrows what is not transient and aborts the watch | medium | patch | It reports any failure that is not a cancellation and never fails (`Output_that_cannot_be_read_for_a_reason_the_runtime_does_not_know...`). |
| Timeout reported when the stop found the container already stopped | medium | patch | `StopAsync` says whether it ended a running container; only that is a timeout (`A_stop_that_finds_the_container_already_stopped_by_itself...`, `..._gone_...`). |
| Docs overstate the retry | medium | patch | `docs/step-containers.md` names what is retried and what is reported once. |
| The `none` log driver claim has no test behind it | medium | patch | The README and docs now say only what the tested path does: the failure is reported and nothing is stored. |
| Abandoned log read may not honor cancellation; `await copying` hangs | medium | patch | The log stream is closed when the read is abandoned (`A_log_read_that_does_not_honor_cancellation_is_ended_by_closing_the_stream`). |
| Shutdown can write to a closed writer | medium | patch | The watch awaits the recording in a `finally` before the writer is disposed. |
| A failing listener of problems ends the watch | low | patch | `Report` swallows a listener's failure (`A_listener_that_fails_does_not_end_the_watch`). |
| Removal of the container failing skips the volumes | low | patch | Each removal is tried alone (`A_container_that_cannot_be_removed_does_not_keep_the_volumes_from_being_tried`). |
| Row left naming a file that could not be created | low | patch | The file is created before the row (`A_file_that_cannot_be_created_leaves_no_row...`). |
| `Watch` races with disposal of the token source | low | patch | The token is taken once and a disposed source ends the call. |
| Watcher disposal by the host is not verified | medium | patch | `Stopping_the_host_stops_the_watcher...` starts the host, watches a step, stops the host, and sees no more looks and no removal. |
| Architecture rule misses `RestartContainerAsync` | low | patch | Added to the rule and its test. |
| Output has no size cap or retention | medium | defer | Logged in `deferred-work.md`; AR-70 and the loop's retention phase own it. |
| A broken log stream is not followed again | medium | defer | Logged; the failure is reported and the file keeps what came. |
| Timeout after a restart between stop and Signal reads as exited | medium | defer | Logged; Story 3.17 reconstructs from state and the recorded deadline. |
| Index `Bytes` is 0 while a step runs or after a kill | low | none | The file is the record; the field's documentation now says when it is written. |
| Speculative `IAgentAdapter`, `AgentInvocation` in Core | false | none | Criterion 5 of the story requires a scripted fake `IAgentAdapter`, which needs the port; only the invocation half is defined and the notes say so. |
| Issue-less Signals sit with GitHub's; `StepRunId` not indexed | false | none | The spine's AD-18 puts the step-exited Signal in the same inbox; nothing reads by step run yet, and the loop applies it with no change by design. |
| A repeated `StartStepAsync` after the container was removed makes a new one | false | none | The effect journal's recorded result prevents the call (AD-5); the same holds for Story 3.2's runtime and is stated there. |
| Context and Methodology volumes leak when the container cannot be created or started | low | none | The volumes are reused by the retry, which is the same step run; leftovers of an abandoned run are cleanup (3.17), already logged. |
| `unchecked((int)ExitCode)` truncates | false | none | Exit codes are 0 to 255 (up to 255 plus signal offsets); a 64-bit code cannot occur. |

## Design Notes

- The container is watched by polling its state once a second (and at the deadline), not by a long wait: the Docker client's request limit is five minutes and a step may run for hours. The log is followed with `follow`, which ends when the container stops.
- Order at the end of a step: output complete, Signal appended, container removed. A crash after the Signal leaves a labelled container for recovery (3.17).

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: no warnings
- `dotnet test --solution Hephaestus.slnx` -- expected: all pass; real-Engine tests skipped without Docker
- `python3 eng/check-coverage.py TestResults` -- expected: line coverage at or above 80%
- `python3 -m unittest discover -s eng -p "test_*.py"` -- expected: all pass
